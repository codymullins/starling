using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Starling.Gui.Chrome;
using Starling.Gui.Controls;
using Starling.Gui.DevTools;
using Starling.Gui.Mcp;
using Starling.Gui.Theme;
using Starling.Common;
using Starling.Common.Diagnostics;
using Starling.Css.Media;
using Starling.Engine;
using Starling.Gui;
using Starling.Telemetry;
using EngineSize = SixLabors.ImageSharp.Size;
using RenderOptions = Starling.Engine.RenderOptions;

namespace Starling.Gui;

/// <summary>
/// Composition root — sidebar (bookmarks/pinned/today) | toolbar over
/// <see cref="WebviewPanel"/> over status bar. Owns the <see cref="BrowserSession"/>
/// and the navigation flow. Mirrors the MAUI <c>MainPage</c> chrome.
/// </summary>
public sealed class MainWindow : Window, IBrowserController
{
    private static readonly IReadOnlyList<TabInfo> Bookmarks =
    [
        new TabInfo("b0a", "example.com",      "Example",                 Url: "https://example.com"),
        new TabInfo("b0b", "Todos",            "Todos",                   Url: "https://jsonplaceholder.typicode.com/todos"),
        new TabInfo("b0c", "netclaw.dev",      "netclaw.dev",             Url: "https://netclaw.dev/"),
        new TabInfo("b1", "google.com",       "Google",                  Url: "https://google.com"),
        new TabInfo("b2", "justinjackson.ca", "Words — Justin Jackson",  Url: "https://justinjackson.ca/words.html"),
        new TabInfo("b3", "ladybird.org",     "Ladybird",                Url: "https://ladybird.org/"),
        new TabInfo("b4", "mcmaster.com",     "McMaster-Carr",           Url: "https://www.mcmaster.com/"),
    ];

    private readonly ThemeManager _tm;
    private readonly IDiagnostics _diag;
    private readonly TelemetryStream _telemetry;
    private readonly BrowserSession _session;
    private readonly WebviewPanel _webview;
    private DevToolsPanel? _devtools;
    private GridSplitter? _devSplitter;

    // Chrome controls are rebuilt on every theme change (ChromeKit's static
    // builders bake colors at construction time, so a "theme flip plus a tree
    // rebuild" is the documented way to keep the UI in sync). None of these
    // fields are readonly for that reason.
    private IconButton _backButton = null!;
    private IconButton _forwardButton = null!;
    private IconButton _reloadButton = null!;
    private IconButton _stopButton = null!;
    private IconButton _themeButton = null!;
    private IconButton _devToolsButton = null!;
    private UrlBar _urlBar = null!;
    private StatusBar _statusBar = null!;
    private Border _toolbar = null!;
    private Sidebar _sidebar = null!;
    private Grid _contentStack = null!;

    private CancellationTokenSource? _navCts;
    private bool _busy;

    public MainWindow()
    {
        _tm = Program.Services.GetRequiredService<ThemeManager>();
        _diag = Program.Services.GetRequiredService<IDiagnostics>();
        _telemetry = Program.Services.GetRequiredService<TelemetryStream>();
        _session = new BrowserSession(_diag);
        _webview = new WebviewPanel(_tm, _diag, OnLinkActivated, OnWebviewStatus, RelayoutForResize);

        Title = string.Empty;
        MinWidth = 1024;
        MinHeight = 720;
        Width = 1280;
        Height = 860;

        // Borderless chrome. macOS keeps Full system decorations so the native
        // traffic lights remain visible — ExtendClientAreaToDecorationsHint
        // pushes them on top of the toolbar (we reserve a left gutter for them
        // in BuildChrome). Windows/Linux drop system decorations entirely; the
        // user opted out of custom min/max/close buttons, and resize is handled
        // by the invisible grip overlay added below.
        ExtendClientAreaToDecorationsHint = true;
        WindowDecorations = OperatingSystem.IsMacOS()
            ? WindowDecorations.Full
            : WindowDecorations.None;

        BuildChrome(urlText: string.Empty, statusText: "Ready", devToolsTab: null);
        _tm.Changed += (_, _) => RebuildChromeForTheme();
    }

    private void BuildChrome(string urlText, string statusText, DevToolsTab? devToolsTab)
    {
        _backButton = new IconButton(_tm, Icons.Back, "Back");
        _backButton.Clicked += BackClicked;
        _forwardButton = new IconButton(_tm, Icons.Fwd, "Forward");
        _forwardButton.Clicked += ForwardClicked;
        _reloadButton = new IconButton(_tm, Icons.Reload, "Reload");
        _reloadButton.Clicked += ReloadClicked;
        _stopButton = new IconButton(_tm, Icons.Stop, "Stop") { IsVisible = _busy };
        _stopButton.Clicked += StopClicked;
        _themeButton = new IconButton(_tm, _tm.Theme == ThemeMode.Dark ? Icons.Sun : Icons.Moon, "Toggle theme");
        _themeButton.Clicked += (_, _) => ToggleTheme();
        _devToolsButton = new IconButton(_tm, Icons.Bug, "Toggle DevTools");
        _devToolsButton.Clicked += (_, _) => ToggleDevTools();
        _reloadButton.IsVisible = !_busy;

        _urlBar = new UrlBar(_tm);
        _urlBar.Address.Text = urlText;
        _urlBar.Submitted += async (_, _) => await NavigateAsync(_urlBar.Address.Text, ignoreEmpty: false);
        _urlBar.FindClicked += (_, _) => _webview.FocusFind();

        var reloadCell = new Panel { Children = { _reloadButton, _stopButton } };

        // Nav cluster on the left, with a small column gap that keeps the
        // three buttons visually grouped but not boxed.
        var navCluster = new StackPanel
        {
            Orientation = global::Avalonia.Layout.Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
        };
        navCluster.Children.Add(_backButton);
        navCluster.Children.Add(_forwardButton);
        navCluster.Children.Add(reloadCell);

        var rightCluster = new StackPanel
        {
            Orientation = global::Avalonia.Layout.Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
        };
        rightCluster.Children.Add(_devToolsButton);
        rightCluster.Children.Add(_themeButton);

        // Reserve ~72px on the left for macOS traffic lights so they don't
        // overlap the back/forward/reload cluster when the sidebar is
        // collapsed. Other platforms get the original 16px inset.
        var toolbarLeftInset = 16.0;
        var toolbarGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(toolbarLeftInset, 0, 16, 0),
            ColumnSpacing = 14,
            VerticalAlignment = VerticalAlignment.Center,
        };
        toolbarGrid.Children.Add(navCluster); Grid.SetColumn(navCluster, 0);
        toolbarGrid.Children.Add(_urlBar); Grid.SetColumn(_urlBar, 1);
        toolbarGrid.Children.Add(rightCluster); Grid.SetColumn(rightCluster, 2);

        var t = _tm.Tokens;
        _toolbar = new Border
        {
            Child = toolbarGrid,
            Height = 58,
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(t.Bg),
        };
        // Lets the OS treat the toolbar as the window's title bar — handles
        // click-and-drag to move and double-click-to-maximize. Interactive
        // children (buttons, the URL TextBox) still get input normally.
        WindowDecorationProperties.SetElementRole(_toolbar, WindowDecorationsElementRole.TitleBar);

        _statusBar = new StatusBar(_tm);
        _statusBar.SetHint(statusText);

        // Content stack: toolbar / [webview | optional devtools] / status bar.
        // The middle row is a Grid("*,Auto,Auto") so the DevTools panel can be
        // injected on the right when toggled; columns 1 and 2 carry the
        // splitter + panel and stay collapsed until then.
        _contentStack = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        _contentStack.Children.Add(_toolbar); Grid.SetRow(_toolbar, 0);
        var middle = new Grid { ColumnDefinitions = new ColumnDefinitions("*") };
        middle.Children.Add(_webview); Grid.SetColumn(_webview, 0);
        _contentStack.Children.Add(middle); Grid.SetRow(middle, 1);
        _contentStack.Children.Add(_statusBar); Grid.SetRow(_statusBar, 2);

        _sidebar = new Sidebar(_tm, Bookmarks, activeId: null, OnSidebarTabActivated, buildLabel: GetBuildLabel());

        var rootGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        rootGrid.Children.Add(_sidebar); Grid.SetColumn(_sidebar, 0);
        rootGrid.Children.Add(_contentStack); Grid.SetColumn(_contentStack, 1);
        Background = new SolidColorBrush(t.Bg);
        Content = OperatingSystem.IsMacOS() ? (Control)rootGrid : WrapWithResizeGrips(rootGrid);

        if (devToolsTab is { } tab) OpenDevTools(tab);
        UpdateNavButtonStates();
    }

    // Avalonia drops native resize edges when SystemDecorations == None, so
    // on Windows/Linux we overlay an 8px transparent border around the
    // window content and tag each edge/corner with the appropriate
    // WindowDecorationsElementRole. The OS picks up the hit-testing role
    // and gives users back native resize behavior + cursors.
    private static Control WrapWithResizeGrips(Control inner)
    {
        const double grip = 8.0;
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions($"{grip},*,{grip}"),
            ColumnDefinitions = new ColumnDefinitions($"{grip},*,{grip}"),
        };

        static Border Strip(WindowDecorationsElementRole role)
        {
            var b = new Border { Background = Brushes.Transparent };
            WindowDecorationProperties.SetElementRole(b, role);
            return b;
        }

        var nw = Strip(WindowDecorationsElementRole.ResizeNW);
        var n  = Strip(WindowDecorationsElementRole.ResizeN);
        var ne = Strip(WindowDecorationsElementRole.ResizeNE);
        var w  = Strip(WindowDecorationsElementRole.ResizeW);
        var e  = Strip(WindowDecorationsElementRole.ResizeE);
        var sw = Strip(WindowDecorationsElementRole.ResizeSW);
        var s  = Strip(WindowDecorationsElementRole.ResizeS);
        var se = Strip(WindowDecorationsElementRole.ResizeSE);

        Grid.SetRow(inner, 1); Grid.SetColumn(inner, 1);
        Grid.SetRow(nw, 0); Grid.SetColumn(nw, 0);
        Grid.SetRow(n,  0); Grid.SetColumn(n,  1);
        Grid.SetRow(ne, 0); Grid.SetColumn(ne, 2);
        Grid.SetRow(w,  1); Grid.SetColumn(w,  0);
        Grid.SetRow(e,  1); Grid.SetColumn(e,  2);
        Grid.SetRow(sw, 2); Grid.SetColumn(sw, 0);
        Grid.SetRow(s,  2); Grid.SetColumn(s,  1);
        Grid.SetRow(se, 2); Grid.SetColumn(se, 2);

        grid.Children.Add(inner);
        grid.Children.Add(nw); grid.Children.Add(n); grid.Children.Add(ne);
        grid.Children.Add(w);  grid.Children.Add(e);
        grid.Children.Add(sw); grid.Children.Add(s); grid.Children.Add(se);
        return grid;
    }

    private void RebuildChromeForTheme()
    {
        var urlText = _urlBar.Address.Text ?? string.Empty;
        var statusText = _statusBar.HintText;
        DevToolsTab? activeTab = _devtools?.ActiveTab;

        // Detach _webview from the old middle grid so it can be re-parented to
        // the new one — Avalonia controls reject being added to a second parent.
        if (_webview.Parent is Panel oldParent) oldParent.Children.Remove(_webview);

        if (_devtools is not null)
        {
            _devtools.Dispose();
            _devtools = null;
            _devSplitter = null;
        }

        BuildChrome(urlText, statusText, activeTab);
    }

    private void OpenDevTools(DevToolsTab tab)
    {
        var middle = (Grid)_contentStack.Children[1];
        _devtools = new DevToolsPanel(_tm, _telemetry, tab);
        _devtools.CloseRequested += (_, _) => ToggleDevTools();
        _devSplitter = new GridSplitter
        {
            Width = 4,
            ResizeDirection = GridResizeDirection.Columns,
            Background = new SolidColorBrush(_tm.Tokens.Border),
        };
        middle.ColumnDefinitions = new ColumnDefinitions("*,Auto,420");
        middle.Children.Add(_devSplitter); Grid.SetColumn(_devSplitter, 1);
        middle.Children.Add(_devtools); Grid.SetColumn(_devtools, 2);
    }

    private async void ToggleTheme()
    {
        _tm.SetTheme(_tm.Theme == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark);
        // The cascade picks up prefers-color-scheme at style-engine
        // construction time, so flipping the theme has to re-run the load
        // for `@media (prefers-color-scheme: dark)` rules to apply.
        if (_session.History.Current is not null && !_busy)
            await RunNavigation(ct => _session.ReloadInteractiveAsync(BuildOptions(), ct), "Theme reload");
    }

    private void ToggleDevTools()
    {
        // The middle row of _contentStack hosts a Grid with one column (the
        // webview). Toggle DevTools by replacing that Grid with a 3-column
        // layout adding a splitter + DevToolsPanel on the right; tearing down
        // reverts to single-column.
        var middle = (Grid)_contentStack.Children[1];
        if (_devtools is null)
        {
            OpenDevTools(DevToolsTab.Performance);
        }
        else
        {
            middle.Children.Remove(_devtools);
            if (_devSplitter is not null) middle.Children.Remove(_devSplitter);
            _devtools.Dispose();
            _devtools = null;
            _devSplitter = null;
            middle.ColumnDefinitions = new ColumnDefinitions("*");
        }
    }

    private async void BackClicked(object? s, EventArgs e)
    {
        if (!_session.History.CanGoBack || _busy) return;
        await RunNavigation(ct => _session.BackInteractiveAsync(BuildOptions(), ct), "Back");
    }

    private async void ForwardClicked(object? s, EventArgs e)
    {
        if (!_session.History.CanGoForward || _busy) return;
        await RunNavigation(ct => _session.ForwardInteractiveAsync(BuildOptions(), ct), "Forward");
    }

    private async void ReloadClicked(object? s, EventArgs e)
    {
        if (_session.History.Current is null || _busy) return;
        await RunNavigation(ct => _session.ReloadInteractiveAsync(BuildOptions(), ct), "Reload");
    }

    private void StopClicked(object? s, EventArgs e) => _navCts?.Cancel();

    private async void OnLinkActivated(string resolvedUrl)
        => await NavigateAsync(resolvedUrl, ignoreEmpty: true);

    private void OnWebviewStatus(string text, bool isError) => _statusBar.SetHint(text, isError);

    private static string GetBuildLabel()
    {
        // Short build-sha label from assembly informational version. Falls
        // back to "" if none — the footer just renders nothing.
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? string.Empty;
        var plus = info.IndexOf('+');
        if (plus >= 0) info = info[(plus + 1)..];
        if (info.Length > 8) info = info[..8];
        return info;
    }

    private async void OnSidebarTabActivated(TabInfo tab)
    {
        if (string.IsNullOrWhiteSpace(tab.Url)) return;
        await NavigateAsync(tab.Url, ignoreEmpty: true);
    }

    private async Task NavigateAsync(string? raw, bool ignoreEmpty)
    {
        var trimmed = (raw ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            if (!ignoreEmpty) _statusBar.SetHint("Enter a URL first.", isError: true);
            return;
        }

        var url = UrlBarInputNormalizer.Normalize(trimmed);
        if (url is null)
        {
            _statusBar.SetHint($"Can't navigate: '{trimmed}' is not a URL.", isError: true);
            return;
        }
        if (_urlBar.Address.Text != url) _urlBar.Address.Text = url;
        await RunNavigation(ct => _session.NavigateInteractiveAsync(url, BuildOptions(), ct), $"GET {url}");
    }

    private async Task RunNavigation(
        Func<CancellationToken, Task<Result<LaidOutPage, RenderError>>> navigate,
        string opLabel)
    {
        var previousCts = _navCts;
        var myCts = new CancellationTokenSource();
        _navCts = myCts;
        previousCts?.Cancel();

        BeginBusy(opLabel);
        var sw = Stopwatch.StartNew();

        // Each navigation must be its own trace. RunNavigation is async and
        // _diag.Span starts an Activity before the first await; that AsyncLocal
        // assignment leaks onto the UI thread's ambient context, but the matching
        // dispose runs in the awaited continuation and never restores the UI
        // thread's Current. Without this reset, every later navigation would
        // parent under the previous (finished) navigate span and the dashboard
        // would show one ever-growing trace instead of one trace per navigation.
        Activity.Current = null;
        using var navSpan = _diag.Span("gui", "navigate");
        try
        {
            var result = await navigate(myCts.Token);
            sw.Stop();
            if (!ReferenceEquals(_navCts, myCts)) return;

            if (result.IsErr)
            {
                _statusBar.SetState(StatusState.Error);
                _statusBar.SetHint($"{opLabel} failed: {result.Error.Message}", isError: true);
            }
            else
            {
                _webview.ShowPage(result.Value);
                _urlBar.SetSecurity(MapSecurity(result.Value.Security));
                Title = string.IsNullOrWhiteSpace(result.Value.Title)
                    ? string.Empty
                    : result.Value.Title;
                var current = _session.History.Current;
                if (!string.IsNullOrEmpty(current) && _urlBar.Address.Text != current)
                    _urlBar.Address.Text = current;

                _statusBar.SetState(StatusState.Ready);
                _statusBar.SetHint($"{opLabel} → {sw.ElapsedMilliseconds} ms");
                _statusBar.SetInfo(
                    view: $"{result.Value.Viewport.Width}×{result.Value.Viewport.Height}",
                    doc:  $"{(int)result.Value.DocumentHeight} px",
                    hist: HistoryLabel());
            }
        }
        catch (OperationCanceledException) when (myCts.IsCancellationRequested)
        {
            if (ReferenceEquals(_navCts, myCts))
            {
                _statusBar.SetState(StatusState.Error);
                _statusBar.SetHint($"{opLabel} canceled.", isError: true);
            }
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_navCts, myCts))
            {
                _statusBar.SetState(StatusState.Error);
                _statusBar.SetHint($"{opLabel} threw: {ex.Message}", isError: true);
            }
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
    }

    private void BeginBusy(string label)
    {
        _busy = true;
        _backButton.SetEnabled(false);
        _forwardButton.SetEnabled(false);
        _reloadButton.IsVisible = false;
        _stopButton.IsVisible = true;
        _statusBar.SetState(StatusState.Loading);
        _statusBar.SetHint($"{label}…");
        _urlBar.ShowProgress();
    }

    private void EndBusy()
    {
        _busy = false;
        _stopButton.IsVisible = false;
        _reloadButton.IsVisible = true;
        _urlBar.HideProgress();
        UpdateNavButtonStates();
    }

    private static SiteSecurity? MapSecurity(Starling.Net.Http.ConnectionSecurity? cs)
    {
        if (cs is null) return null;
        var cert = cs.Certificate;
        return new SiteSecurity(
            Encrypted: cs.IsEncrypted,
            Secure: cs.IsSecure,
            Protocol: cs.Protocol,
            Certificate: cert is not null,
            CertSubject: cert?.Subject,
            CertIssuer: cert?.Issuer,
            CertNotBefore: cert?.NotBefore,
            CertNotAfter: cert?.NotAfter);
    }

    private string HistoryLabel()
    {
        var h = _session.History;
        if (h.Count == 0 || h.Index < 0) return "0 / 0";
        return $"{h.Index + 1} / {h.Count}";
    }

    private void UpdateNavButtonStates()
    {
        _backButton.SetEnabled(_session.History.CanGoBack && !_busy);
        _forwardButton.SetEnabled(_session.History.CanGoForward && !_busy);
        _reloadButton.SetEnabled(_session.History.Current is not null && !_busy);
    }

    // Layout viewport tracks the live webview area so pages reflow to the
    // window size (and to sidebar/DevTools toggles), instead of a fixed size.
    private RenderOptions BuildOptions() => BuildOptionsFor(_webview.CurrentViewportSize());

    private RenderOptions BuildOptionsFor(EngineSize viewport) => new(viewport, FontSize: 16f)
    {
        PreferredColorScheme = _tm.Theme == ThemeMode.Dark ? ColorScheme.Dark : ColorScheme.Light,
    };

    // Re-layout callback handed to WebviewPanel: reflow the current page at the
    // new viewport reusing its document/resources (no network). Skipped while a
    // navigation is in flight — that load already picks up the latest size.
    private LaidOutPage? RelayoutForResize(LaidOutPage page, EngineSize viewport)
    {
        if (_busy) return null;
        try
        {
            return _session.RelayoutCurrent(page, BuildOptionsFor(viewport));
        }
        catch (Exception ex)
        {
            _diag.Log(DiagLevel.Warn, "gui", $"resize relayout failed: {ex.Message}");
            return null;
        }
    }

    // ---- IBrowserController -------------------------------------------------
    // MCP tool calls land here, marshaled to the UI thread by BrowserControlBridge.
    // Each method drives the same navigation flow as the toolbar buttons and
    // returns a BrowserControlResult so the agent sees the post-state.

    public async Task<BrowserControlResult> NavigateFromToolAsync(string url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
            return Snapshot(success: false, error: "browser_navigate requires a non-empty url.");
        await NavigateAsync(url, ignoreEmpty: false);
        return Snapshot(success: true);
    }

    public async Task<BrowserControlResult> BackFromToolAsync(CancellationToken ct)
    {
        if (!_session.History.CanGoBack)
            return Snapshot(success: false, error: "No back history.");
        await RunNavigation(c => _session.BackInteractiveAsync(BuildOptions(), c), "Back");
        return Snapshot(success: true);
    }

    public async Task<BrowserControlResult> ForwardFromToolAsync(CancellationToken ct)
    {
        if (!_session.History.CanGoForward)
            return Snapshot(success: false, error: "No forward history.");
        await RunNavigation(c => _session.ForwardInteractiveAsync(BuildOptions(), c), "Forward");
        return Snapshot(success: true);
    }

    public async Task<BrowserControlResult> ReloadFromToolAsync(CancellationToken ct)
    {
        if (_session.History.Current is null)
            return Snapshot(success: false, error: "Nothing to reload.");
        await RunNavigation(c => _session.ReloadInteractiveAsync(BuildOptions(), c), "Reload");
        return Snapshot(success: true);
    }

    private BrowserControlResult Snapshot(bool success, string? error = null)
    {
        var url = _session.History.Current;
        var title = string.IsNullOrEmpty(Title) ? null : Title;
        return success
            ? BrowserControlResult.Success(url, title, _session.History.CanGoBack, _session.History.CanGoForward, _busy)
            : BrowserControlResult.Failure(error ?? "Unknown error", url, title,
                _session.History.CanGoBack, _session.History.CanGoForward, _busy);
    }

    protected override void OnClosed(EventArgs e)
    {
        _navCts?.Cancel();
        _webview.Dispose();
        _session.Dispose();
        base.OnClosed(e);
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        // Cmd on macOS, Ctrl elsewhere — Avalonia's KeyModifiers.Meta maps to
        // Command on macOS so we accept either to keep behaviour uniform.
        var primary = e.KeyModifiers.HasFlag(KeyModifiers.Meta)
                   || e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (primary && e.Key == Key.F)
        {
            e.Handled = true;
            _webview.FocusFind();
            return;
        }
        if (primary && e.Key == Key.C)
        {
            // Don't steal Cmd-C from a focused TextBox (URL bar, find entry).
            if (FocusManager?.GetFocusedElement() is TextBox) { base.OnKeyDown(e); return; }
            e.Handled = true;
            await _webview.CopySelectionAsync();
            return;
        }
        base.OnKeyDown(e);
    }
}
