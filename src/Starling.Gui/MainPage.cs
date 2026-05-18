using System.Diagnostics;
using Tessera.Common.Diagnostics;
using Tessera.Engine;
using Tessera.Gui.Chrome;
using Tessera.Gui.DevTools;
using Tessera.Gui.Mcp;
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
public sealed class MainPage : ContentPage, IBrowserController
{
    private static readonly EngineSize Viewport = new(1200, 900);

    private static readonly IReadOnlyList<TabInfo> Bookmarks = new[]
    {
        new TabInfo("b1", "google.com",      "Google",            Url: "https://google.com"),
        new TabInfo("b2", "justinjackson.ca", "Words — Justin Jackson", Url: "https://justinjackson.ca/words.html"),
        new TabInfo("b3", "ladybird.org",    "Ladybird",          Url: "https://ladybird.org/"),
        new TabInfo("b4", "mcmaster.com",    "McMaster-Carr",     Url: "https://www.mcmaster.com/"),
    };

    private static readonly IReadOnlyList<TabInfo> PinnedTabs = new[]
    {
        new TabInfo("p1", "mail.fastmail.com", "Mail"),
        new TabInfo("p2", "cal.starling.dev", "Calendar"),
    };

    private static readonly IReadOnlyList<TabInfo> TodayTabs = new[]
    {
        new TabInfo("t1", "justinjackson.ca", "Words — Justin Jackson"),
        new TabInfo("t2", "starling.dev", "M3 release notes", Audio: true),
        new TabInfo("t3", "github.com", "starling-browser/starling"),
        new TabInfo("t4", "localhost", "localhost:3000 · dev"),
    };

    private readonly IDiagnostics _diag;
    private readonly ThemeManager _tm;
    private readonly BrowserSession _session;
    private readonly WebviewPanel _webview;
    private readonly BrowserControlBridge _browserControl;
    private readonly GuiMcpServer _mcpServer;

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
    private IconButton _stopButton = null!;
    private StatusBar _statusBar = null!;

    // The active navigation's cancellation source. Each new user-initiated
    // navigation replaces this and cancels the previous one, so the in-flight
    // HTTP / decode / layout pipeline tears down promptly. Stop click cancels
    // without replacing so the cancelled op surfaces its own "canceled" status.
    private CancellationTokenSource? _navCts;

    public MainPage(
        IDiagnostics diag,
        ThemeManager tm,
        BrowserControlBridge browserControl,
        GuiMcpServer mcpServer)
    {
        _diag = diag;
        _tm = tm;
        _browserControl = browserControl;
        _mcpServer = mcpServer;
        _session = new BrowserSession(diag);
        _browserControl.Attach(this);

        // The page surface and its interaction state outlive theme rebuilds.
        _webview = new WebviewPanel(tm, OnLinkActivated, OnWebviewStatus, diag);

        _statusText = $"mcp → {_mcpServer.Endpoint} · trace log → {NativeCallTrace.Path}";
        _statusIsError = false;

        Title = "Starling";
        BuildMenu();
        _tm.Changed += (_, _) => Rebuild();
        Rebuild();
    }

    // ─── Composition ───────────────────────────────────────────────────────

    private void Rebuild()
    {
        var t = _tm.Tokens;
        BackgroundColor = t.Bg;

        var sidebar = new Sidebar(_tm, Bookmarks, PinnedTabs, TodayTabs, activeId: "t1", OnSidebarTabActivated);

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

        _statusBar = new StatusBar(_tm);
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
        _stopButton = new IconButton(_tm, Icons.Stop, "Stop");
        _backButton.Clicked += BackClicked;
        _forwardButton.Clicked += ForwardClicked;
        _reloadButton.Clicked += ReloadClicked;
        _stopButton.Clicked += StopClicked;

        // Reload morphs to Stop while a navigation is in flight. We overlay
        // both buttons in the same grid cell and toggle IsVisible — hiding
        // also stops the hidden one from intercepting taps.
        _reloadButton.IsVisible = !_busy;
        _stopButton.IsVisible = _busy;

        _urlBar = new UrlBar(_tm);
        _urlBar.Address.Text = _urlText;
        _urlBar.Address.TextChanged += (_, e) => _urlText = e.NewTextValue ?? string.Empty;
        _urlBar.Address.Completed += async (_, _) => await NavigateAsync(_urlBar.Address.Text, ignoreEmpty: false);
        _urlBar.FindClicked += (_, _) => _webview.FocusFind();
        _urlBar.LoadChartClicked += (_, _) => ShowDevTools(DevToolsTab.Performance);
        if (_busy) _urlBar.ShowLoadChart(SampleData.LoadPhases, SampleData.LoadTotalMs);

        var star = new IconButton(_tm, Icons.Star, "Save");
        var isDark = _tm.Theme == ThemeMode.Dark;
        var themeToggle = new IconButton(
            _tm,
            isDark ? Icons.Sun : Icons.Moon,
            isDark ? "Switch to light mode" : "Switch to dark mode");
        themeToggle.Clicked += (_, _) => _tm.SetTheme(isDark ? ThemeMode.Light : ThemeMode.Dark);
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
                new ColumnDefinition(GridLength.Auto),
            },
        };
        grid.Add(_backButton, 0, 0);
        grid.Add(_forwardButton, 1, 0);
        grid.Add(_reloadButton, 2, 0);
        grid.Add(_stopButton, 2, 0);
        grid.Add(_urlBar, 3, 0);
        grid.Add(star, 4, 0);
        grid.Add(themeToggle, 5, 0);
        grid.Add(devtoolsToggle, 6, 0);
        grid.Add(more, 7, 0);
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

    private async void OnSidebarTabActivated(TabInfo tab)
    {
        if (string.IsNullOrWhiteSpace(tab.Url)) return;
        await NavigateAsync(tab.Url, ignoreEmpty: true);
    }

    private void StopClicked(object? sender, EventArgs e)
    {
        // Cancel the in-flight navigation but leave _navCts pointing at it —
        // the cancelled op's finalizer is what surfaces the "canceled" status
        // and calls EndBusy. Replacing here would race with that finalizer.
        _navCts?.Cancel();
    }

    private async Task NavigateAsync(string? rawUrl, bool ignoreEmpty)
    {
        // Note: no _busy early-return — clicking a bookmark or link while a
        // page is still loading cancels the in-flight load and starts this
        // one. RunNavigation handles the CTS swap.
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

    public Task<BrowserControlResult> NavigateFromToolAsync(string url, CancellationToken ct)
    {
        if (_busy) return Task.FromResult(CurrentFailure("The browser is busy."));

        var trimmed = (url ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return Task.FromResult(CurrentFailure("The url argument is required."));

        _urlText = trimmed;
        if (_urlBar.Address.Text != trimmed) _urlBar.Address.Text = trimmed;
        return RunNavigation(
            token => _session.NavigateInteractiveAsync(trimmed, BuildOptions(), token),
            $"GET {trimmed}",
            ct);
    }

    public Task<BrowserControlResult> BackFromToolAsync(CancellationToken ct)
    {
        if (_busy) return Task.FromResult(CurrentFailure("The browser is busy."));
        if (!_session.History.CanGoBack)
            return Task.FromResult(CurrentFailure("Cannot go back because there is no previous history entry."));
        return RunNavigation(token => _session.BackInteractiveAsync(BuildOptions(), token), "Back", ct);
    }

    public Task<BrowserControlResult> ForwardFromToolAsync(CancellationToken ct)
    {
        if (_busy) return Task.FromResult(CurrentFailure("The browser is busy."));
        if (!_session.History.CanGoForward)
            return Task.FromResult(CurrentFailure("Cannot go forward because there is no next history entry."));
        return RunNavigation(token => _session.ForwardInteractiveAsync(BuildOptions(), token), "Forward", ct);
    }

    public Task<BrowserControlResult> ReloadFromToolAsync(CancellationToken ct)
    {
        if (_busy) return Task.FromResult(CurrentFailure("The browser is busy."));
        if (_session.History.Current is null)
            return Task.FromResult(CurrentFailure("Cannot reload before the first navigation."));
        return RunNavigation(token => _session.ReloadInteractiveAsync(BuildOptions(), token), "Reload", ct);
    }

    private async Task<BrowserControlResult> RunNavigation(
        Func<CancellationToken, Task<Common.Result<LaidOutPage, RenderError>>> navigate,
        string opLabel,
        CancellationToken externalCt = default)
    {
        externalCt.ThrowIfCancellationRequested();

        // Cancel any in-flight navigation by replacing the active CTS first,
        // then signalling the previous one. The previous op's finally sees
        // _navCts != its own CTS and bows out without touching UI; this op
        // owns the UI from here.
        var previousCts = _navCts;
        var myCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        _navCts = myCts;
        previousCts?.Cancel();

        BeginBusy(opLabel);
        var stopwatch = Stopwatch.StartNew();
        using var navSpan = _diag.Span("gui", "navigate");
        Activity.Current?.SetTag("gui.op", opLabel);
        var ok = false;
        string? error = null;
        try
        {
            var result = await navigate(myCts.Token);
            stopwatch.Stop();

            // If we've been superseded mid-flight, drop the result silently —
            // the newer op owns the UI and will paint when it lands.
            if (!ReferenceEquals(_navCts, myCts))
                return CurrentFailure($"{opLabel} superseded.");

            if (result.IsErr)
            {
                error = result.Error.Message;
                SetStatus($"{opLabel} failed: {error}", isError: true);
            }
            else
            {
                _webview.ShowPage(result.Value);
                if (!string.IsNullOrWhiteSpace(result.Value.Title)) Title = result.Value.Title!;
                var current = _session.History.Current ?? "(no url)";
                if (current != "(no url)") { _urlText = current; _urlBar.Address.Text = current; }
                SetStatus(
                    $"{opLabel} → {result.Value.Viewport.Width}×{(int)result.Value.DocumentHeight} px, " +
                    $"{stopwatch.ElapsedMilliseconds} ms · {current}",
                    isError: false);
                ok = true;
            }
        }
        catch (OperationCanceledException) when (myCts.IsCancellationRequested)
        {
            error = $"{opLabel} canceled.";
            // Only surface status if we're still the active op (stop click).
            // A supersession leaves _navCts pointing to the newer op, whose
            // own status text should win.
            if (ReferenceEquals(_navCts, myCts))
                SetStatus(error, isError: true);
        }
        catch (Exception ex)
        {
            // Chokepoint for every navigation flow: the async void event
            // handlers above have nowhere to surface an exception.
            if (ReferenceEquals(_navCts, myCts))
                SetStatus($"{opLabel} threw: {ex.Message}", isError: true);
            error = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_navCts, myCts))
            {
                EndBusy();
                _navCts = null;
            }
            myCts.Dispose();
        }

        return ok ? CurrentSuccess() : CurrentFailure(error ?? $"{opLabel} failed.");
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
        _reloadButton.IsVisible = false;
        _stopButton.IsVisible = true;
    }

    private void EndBusy()
    {
        _busy = false;
        _urlBar.HideLoadChart();
        _stopButton.IsVisible = false;
        _reloadButton.IsVisible = true;
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

    private BrowserControlResult CurrentSuccess()
        => BrowserControlResult.Success(
            _session.History.Current,
            Title,
            _session.History.CanGoBack,
            _session.History.CanGoForward,
            _busy);

    private BrowserControlResult CurrentFailure(string error)
        => BrowserControlResult.Failure(
            error,
            _session.History.Current,
            Title,
            _session.History.CanGoBack,
            _session.History.CanGoForward,
            _busy);
}
