using System.Diagnostics;
using Tessera.Common.Diagnostics;
using Tessera.Engine;
using Tessera.Gui.Chrome;
using Tessera.Gui.DevTools;
using Tessera.Gui.Theme;
using EngineSize = SixLabors.ImageSharp.Size;

namespace Tessera.Gui;

/// <summary>
/// The composition root — assembles the Sidecar chrome (sidebar · toolbar ·
/// webview · status bar) and the optional right-docked DevTools, and owns the
/// navigation flow. Theme / density / type changes rebuild the tree; the
/// <see cref="WebviewPanel"/> instance survives a rebuild so the rendered page
/// and its interaction state are preserved.
/// </summary>
public sealed class MainPage : ContentPage
{
    private static readonly EngineSize Viewport = new(1200, 900);

    private static readonly IReadOnlyList<TabInfo> PinnedTabs = new[]
    {
        new TabInfo("p1", "mail.fastmail.com", "Mail"),
        new TabInfo("p2", "cal.tessera.dev", "Calendar"),
    };

    private static readonly IReadOnlyList<TabInfo> TodayTabs = new[]
    {
        new TabInfo("t1", "justinjackson.ca", "Words — Justin Jackson"),
        new TabInfo("t2", "tessera.dev", "M3 release notes", Audio: true),
        new TabInfo("t3", "github.com", "tessera-browser/tessera"),
        new TabInfo("t4", "localhost", "localhost:3000 · dev"),
    };

    private readonly IDiagnostics _diag;
    private readonly ThemeManager _tm;
    private readonly BrowserSession _session;
    private readonly WebviewPanel _webview;

    // State preserved across theme/density/type rebuilds.
    private string _urlText = string.Empty;
    private string _statusText;
    private bool _statusIsError;
    private bool _busy;
    private bool _devtoolsVisible;
    private DevToolsTab _activeDevTool = DevToolsTab.Performance;

    // Rebuilt each pass — captured so navigation can drive them.
    private UrlBar _urlBar = null!;
    private IconButton _backButton = null!;
    private IconButton _forwardButton = null!;
    private IconButton _reloadButton = null!;
    private StatusBar _statusBar = null!;

    public MainPage(IDiagnostics diag, ThemeManager tm)
    {
        _diag = diag;
        _tm = tm;
        _session = new BrowserSession(diag);

        // The page surface and its interaction state outlive theme rebuilds.
        _webview = new WebviewPanel(tm, OnLinkActivated, OnWebviewStatus);

        _statusText = $"trace log → {NativeCallTrace.Path}";
        _statusIsError = false;

        Title = "Tessera";
        BuildMenu();
        _tm.Changed += (_, _) => Rebuild();
        Rebuild();
    }

    // ─── Composition ───────────────────────────────────────────────────────

    private void Rebuild()
    {
        var t = _tm.Tokens;
        BackgroundColor = t.Bg;

        var sidebar = new Sidebar(_tm, PinnedTabs, TodayTabs, activeId: "t1");

        var mainColumn = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto), // toolbar
                new RowDefinition(GridLength.Star), // content
                new RowDefinition(GridLength.Auto), // status bar
            },
        };
        mainColumn.Add(BuildToolbar(), 0, 0);
        mainColumn.Add(BuildContentRow(), 0, 1);

        _statusBar = new StatusBar(_tm, dom: "87", bytes: "4.2 kB", ttfb: "318ms", heap: "16.4MB");
        _statusBar.SetLeft(_statusText, _statusIsError);
        var statusWrap = new Grid();
        statusWrap.Add(new BoxView { Color = t.Border, HeightRequest = 1, VerticalOptions = LayoutOptions.Start });
        statusWrap.Add(_statusBar);
        mainColumn.Add(statusWrap, 0, 2);

        var root = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(Sidebar.Width),
                new ColumnDefinition(GridLength.Star),
            },
        };
        root.Add(sidebar, 0, 0);
        root.Add(mainColumn, 1, 0);
        Content = root;

        SetNavButtonStates();
        if (_busy) BeginBusyVisual();
    }

    private View BuildToolbar()
    {
        _backButton = new IconButton(_tm, Icons.Back, "Back");
        _forwardButton = new IconButton(_tm, Icons.Fwd, "Forward");
        _reloadButton = new IconButton(_tm, Icons.Reload, "Reload");
        _backButton.Clicked += BackClicked;
        _forwardButton.Clicked += ForwardClicked;
        _reloadButton.Clicked += ReloadClicked;

        _urlBar = new UrlBar(_tm);
        _urlBar.Address.Text = _urlText;
        _urlBar.Address.TextChanged += (_, e) => _urlText = e.NewTextValue ?? string.Empty;
        _urlBar.Address.Completed += async (_, _) => await NavigateAsync(_urlBar.Address.Text, ignoreEmpty: false);
        _urlBar.FindClicked += (_, _) => _webview.FocusFind();
        _urlBar.LoadChartClicked += (_, _) => ShowDevTools(DevToolsTab.Performance);
        if (_busy) _urlBar.ShowLoadChart(SampleData.LoadPhases, SampleData.LoadTotalMs);

        var star = new IconButton(_tm, Icons.Star, "Save");
        var devtoolsToggle = new IconButton(_tm, Icons.Bug, "DevTools", isOn: _devtoolsVisible);
        devtoolsToggle.Clicked += (_, _) => ToggleDevTools();
        var more = new IconButton(_tm, Icons.More, "More");

        var grid = new Grid
        {
            HeightRequest = 44,
            Padding = new Thickness(12, 0),
            ColumnSpacing = 8,
            VerticalOptions = LayoutOptions.Center,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        grid.Add(_backButton, 0, 0);
        grid.Add(_forwardButton, 1, 0);
        grid.Add(_reloadButton, 2, 0);
        grid.Add(_urlBar, 3, 0);
        grid.Add(star, 4, 0);
        grid.Add(devtoolsToggle, 5, 0);
        grid.Add(more, 6, 0);
        return grid;
    }

    private View BuildContentRow()
    {
        var row = new Grid
        {
            Padding = new Thickness(12, 0, 12, 8),
            ColumnSpacing = 8,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star) },
        };

        var webviewFrame = WrapPanel(_webview);
        row.Add(webviewFrame, 0, 0);

        if (_devtoolsVisible)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var devtools = new DevToolsPanel(_tm, _activeDevTool);
            devtools.CloseRequested += (_, _) => ToggleDevTools();
            row.Add(WrapPanel(devtools), 1, 0);
        }

        return row;
    }

    /// <summary>Wraps a panel in the rounded, hairline-bordered frame the
    /// design uses for the webview and devtools slabs.</summary>
    private Border WrapPanel(View content) => new()
    {
        BackgroundColor = _tm.Tokens.Panel,
        Stroke = _tm.Tokens.Border,
        StrokeThickness = 1,
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
        {
            CornerRadius = _tm.Metrics.R,
        },
        Padding = new Thickness(0),
        Content = content,
    };

    private void BuildMenu()
    {
        var findItem = new MenuFlyoutItem { Text = "Find…" };
        findItem.KeyboardAccelerators.Add(new KeyboardAccelerator
        {
            Key = "F",
            Modifiers = KeyboardAcceleratorModifiers.Cmd,
        });
        findItem.Clicked += (_, _) => _webview.FocusFind();

        var findNextItem = new MenuFlyoutItem { Text = "Find Next" };
        findNextItem.KeyboardAccelerators.Add(new KeyboardAccelerator
        {
            Key = "G",
            Modifiers = KeyboardAcceleratorModifiers.Cmd,
        });
        findNextItem.Clicked += (_, _) => _webview.FindNextFromMenu();

        var editMenu = new MenuBarItem { Text = "Edit" };
        editMenu.Add(findItem);
        editMenu.Add(findNextItem);
        MenuBarItems.Add(editMenu);
    }

    // ─── DevTools ──────────────────────────────────────────────────────────

    private void ToggleDevTools()
    {
        _devtoolsVisible = !_devtoolsVisible;
        Rebuild();
    }

    private void ShowDevTools(DevToolsTab tab)
    {
        _activeDevTool = tab;
        _devtoolsVisible = true;
        Rebuild();
    }

    // ─── Navigation ────────────────────────────────────────────────────────

    private async void BackClicked(object? sender, EventArgs e)
    {
        if (!_session.History.CanGoBack || _busy) return;
        await RunNavigation(ct => _session.BackInteractiveAsync(BuildOptions(), ct), "Back");
    }

    private async void ForwardClicked(object? sender, EventArgs e)
    {
        if (!_session.History.CanGoForward || _busy) return;
        await RunNavigation(ct => _session.ForwardInteractiveAsync(BuildOptions(), ct), "Forward");
    }

    private async void ReloadClicked(object? sender, EventArgs e)
    {
        if (_session.History.Current is null || _busy) return;
        await RunNavigation(ct => _session.ReloadInteractiveAsync(BuildOptions(), ct), "Reload");
    }

    private async void OnLinkActivated(string resolvedUrl)
        => await NavigateAsync(resolvedUrl, ignoreEmpty: true);

    private async Task NavigateAsync(string? rawUrl, bool ignoreEmpty)
    {
        if (_busy) return;
        var url = (rawUrl ?? string.Empty).Trim();
        if (url.Length == 0)
        {
            if (!ignoreEmpty) SetStatus("Enter a URL first.", isError: true);
            return;
        }
        _urlText = url;
        if (_urlBar.Address.Text != url) _urlBar.Address.Text = url;
        await RunNavigation(ct => _session.NavigateInteractiveAsync(url, BuildOptions(), ct), $"GET {url}");
    }

    private async Task RunNavigation(
        Func<CancellationToken, Task<Common.Result<LaidOutPage, RenderError>>> navigate, string opLabel)
    {
        BeginBusy(opLabel);
        var stopwatch = Stopwatch.StartNew();
        using var navSpan = _diag.Span("gui", "navigate");
        Activity.Current?.SetTag("gui.op", opLabel);
        try
        {
            var result = await navigate(CancellationToken.None);
            stopwatch.Stop();
            if (result.IsErr)
            {
                SetStatus($"{opLabel} failed: {result.Error.Message}", isError: true);
                return;
            }

            _webview.ShowPage(result.Value);
            if (!string.IsNullOrWhiteSpace(result.Value.Title)) Title = result.Value.Title!;
            var current = _session.History.Current ?? "(no url)";
            if (current != "(no url)") { _urlText = current; _urlBar.Address.Text = current; }
            SetStatus(
                $"{opLabel} → {result.Value.Viewport.Width}×{(int)result.Value.DocumentHeight} px, " +
                $"{stopwatch.ElapsedMilliseconds} ms · {current}",
                isError: false);
        }
        catch (Exception ex)
        {
            // Chokepoint for every navigation flow: the async void event
            // handlers above have nowhere to surface an exception.
            SetStatus($"{opLabel} threw: {ex.Message}", isError: true);
        }
        finally
        {
            EndBusy();
        }
    }

    private void BeginBusy(string label)
    {
        _busy = true;
        BeginBusyVisual();
        _urlBar.ShowLoadChart(SampleData.LoadPhases, SampleData.LoadTotalMs);
        SetStatus($"{label}…", isError: false);
    }

    private void BeginBusyVisual()
    {
        _backButton.SetEnabled(false);
        _forwardButton.SetEnabled(false);
        _reloadButton.SetEnabled(false);
    }

    private void EndBusy()
    {
        _busy = false;
        _urlBar.HideLoadChart();
        SetNavButtonStates();
    }

    private void SetNavButtonStates()
    {
        _backButton.SetEnabled(_session.History.CanGoBack && !_busy);
        _forwardButton.SetEnabled(_session.History.CanGoForward && !_busy);
        _reloadButton.SetEnabled(_session.History.Current is not null && !_busy);
    }

    private void SetStatus(string text, bool isError)
    {
        _statusText = text;
        _statusIsError = isError;
        _statusBar.SetLeft(text, isError);
    }

    private void OnWebviewStatus(string text, bool isError) => SetStatus(text, isError);

    private static RenderOptions BuildOptions() => new(Viewport, FontSize: 16f);
}
