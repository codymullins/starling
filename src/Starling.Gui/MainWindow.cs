using System.Diagnostics;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Starling.Gui.Chrome;
using Starling.Gui.Controls;
using Starling.Gui.DevTools;
using Starling.Gui.Mcp;
using Starling.Gui.Theme;
using Starling.Common;
using Starling.Common.Diagnostics;
using Starling.Css.Media;
using Starling.Engine;
using Starling.Paint.Backend;
using Starling.Gui;
using Starling.Html;
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
        new TabInfo("b0t", "localhost:8088",   "Todo",                    Url: "http://localhost:8088/todo/"),
        new TabInfo("b0a", "example.com",      "Example",                 Url: "https://example.com"),
        new TabInfo("b0b", "Todos",            "Todos",                   Url: "https://jsonplaceholder.typicode.com/todos"),
        new TabInfo("b0c", "netclaw.dev",      "netclaw.dev",             Url: "https://netclaw.dev/"),
        new TabInfo("b1", "google.com",       "Google",                  Url: "https://google.com"),
        new TabInfo("b2", "localhost:8088",   "Words",                   Url: "http://localhost:8088/words/"),
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
    private Window? _devWindow;
    private DevToolsDock _dock = DevToolsDock.Bottom;

    // Splitter hairline + default extent of the docked panel along its docking
    // axis (width for left/right, height for bottom).
    private const double SplitterThickness = 4;
    private const double DockExtent = 420;
    private const double DockExtentBottom = 320;

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

    // The page reference currently handed to the webview. Progressive rendering
    // shows page1 at first paint then may return a successor page2; tracking the
    // last-shown reference lets the success branch skip a redundant re-show when
    // the deferred phase changed nothing and returned the same page1 instance
    // (re-showing it would dispose the live page out from under itself).
    private LaidOutPage? _lastShownPage;

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
        // The middle row hosts a Grid that starts as a single "*" cell holding
        // the webview; PlaceDevTools reconfigures it into a split column/row
        // layout (or pops the panel into a floating window) on demand.
        _contentStack = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        _contentStack.Children.Add(_toolbar); Grid.SetRow(_toolbar, 0);
        var middle = new Grid { ColumnDefinitions = new ColumnDefinitions("*") };
        middle.Children.Add(_webview); Grid.SetColumn(_webview, 0);
        _contentStack.Children.Add(middle); Grid.SetRow(middle, 1);
        _contentStack.Children.Add(_statusBar); Grid.SetRow(_statusBar, 2);

        _sidebar = new Sidebar(_tm, Bookmarks, activeId: null, OnSidebarTabActivated, build: GetBuildInfo());

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
            // The dock position lives on _dock (a field), so it carries across
            // the rebuild; OpenDevTools re-hosts at the same spot. The floating
            // window must be torn down here so we don't leak a second one.
            DestroyDevWindow();
            _devtools.Dispose();
            _devtools = null;
            _devSplitter = null;
        }

        BuildChrome(urlText, statusText, activeTab);
    }

    private void OpenDevTools(DevToolsTab tab)
    {
        _devtools = new DevToolsPanel(_tm, _telemetry, tab, _dock);
        _devtools.CloseRequested += (_, _) => CloseDevTools();
        _devtools.DockRequested += (_, dock) => SetDock(dock);
        PlaceDevTools();
    }

    // Switch the docking position of an already-open panel.
    private void SetDock(DevToolsDock dock)
    {
        if (_devtools is null || _dock == dock) return;
        _dock = dock;
        PlaceDevTools();
    }

    // (Re)host _devtools at _dock. Detaches it from whatever currently holds it
    // (the middle grid and/or a floating window), then lays it out afresh. The
    // webview is always re-seated as the primary cell of the middle grid.
    private void PlaceDevTools()
    {
        if (_devtools is null) return;
        var middle = (Grid)_contentStack.Children[1];

        // Clear the grid (removes webview + any splitter + a docked panel) and
        // close the floating window (detaching the panel from it) so _devtools
        // and _webview are parentless and free to re-host below.
        middle.Children.Clear();
        DestroyDevWindow();
        _devSplitter = null;
        _devtools.SetDock(_dock);

        if (_dock == DevToolsDock.Floating)
        {
            middle.ColumnDefinitions = new ColumnDefinitions("*");
            middle.RowDefinitions = new RowDefinitions("*");
            Place(middle, _webview);
            ShowDevWindow();
            return;
        }

        _devSplitter = new GridSplitter
        {
            ResizeDirection = _dock == DevToolsDock.Bottom
                ? GridResizeDirection.Rows
                : GridResizeDirection.Columns,
            Background = new SolidColorBrush(_tm.Tokens.Border),
        };

        switch (_dock)
        {
            case DevToolsDock.Right:
                _devSplitter.Width = SplitterThickness;
                middle.RowDefinitions = new RowDefinitions("*");
                middle.ColumnDefinitions = new ColumnDefinitions($"*,Auto,{DockExtent}");
                Place(middle, _webview, col: 0);
                Place(middle, _devSplitter, col: 1);
                Place(middle, _devtools, col: 2);
                break;
            case DevToolsDock.Left:
                _devSplitter.Width = SplitterThickness;
                middle.RowDefinitions = new RowDefinitions("*");
                middle.ColumnDefinitions = new ColumnDefinitions($"{DockExtent},Auto,*");
                Place(middle, _devtools, col: 0);
                Place(middle, _devSplitter, col: 1);
                Place(middle, _webview, col: 2);
                break;
            case DevToolsDock.Bottom:
                _devSplitter.Height = SplitterThickness;
                middle.ColumnDefinitions = new ColumnDefinitions("*");
                middle.RowDefinitions = new RowDefinitions($"*,Auto,{DockExtentBottom}");
                Place(middle, _webview, row: 0);
                Place(middle, _devSplitter, row: 1);
                Place(middle, _devtools, row: 2);
                break;
        }
    }

    // Add a child to the middle grid at an explicit cell. Both attached
    // properties are set every time because they persist on the control across
    // re-parents and a stale value would land it in the wrong cell after a
    // column<->row dock switch.
    private static void Place(Grid grid, Control child, int col = 0, int row = 0)
    {
        Grid.SetColumn(child, col);
        Grid.SetRow(child, row);
        grid.Children.Add(child);
    }

    private void ShowDevWindow()
    {
        if (_devtools is null) return;
        var t = _tm.Tokens;
        _devWindow = new Window
        {
            Title = "DevTools",
            Width = 720,
            Height = 480,
            MinWidth = 360,
            MinHeight = 240,
            Background = new SolidColorBrush(t.Panel),
            Content = _devtools,
        };
        // A user-initiated close of the floating window tears DevTools down
        // entirely. DestroyDevWindow nulls _devWindow before calling Close, so
        // programmatic teardown is a no-op here (avoids re-entrancy).
        _devWindow.Closed += (s, _) =>
        {
            if (!ReferenceEquals(_devWindow, s)) return;
            _devWindow = null;
            CloseDevTools();
        };
        _devWindow.Show(this);
    }

    // Detach the panel from the floating window and close it, without disposing
    // the panel (the caller decides whether to re-host or dispose).
    private void DestroyDevWindow()
    {
        if (_devWindow is null) return;
        var w = _devWindow;
        _devWindow = null;
        w.Content = null;
        w.Close();
    }

    private void CloseDevTools()
    {
        if (_devtools is null) return;
        var middle = (Grid)_contentStack.Children[1];
        middle.Children.Remove(_devtools);
        if (_devSplitter is not null) middle.Children.Remove(_devSplitter);
        DestroyDevWindow();

        _devtools.Dispose();
        _devtools = null;
        _devSplitter = null;

        // Restore the webview as the sole occupant of the middle area.
        middle.ColumnDefinitions = new ColumnDefinitions("*");
        middle.RowDefinitions = new RowDefinitions("*");
        if (!middle.Children.Contains(_webview)) Place(middle, _webview);
    }

    private async void ToggleTheme()
    {
        _tm.SetTheme(_tm.Theme == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark);
        // The cascade picks up prefers-color-scheme at style-engine
        // construction time, so flipping the theme has to re-run the load
        // for `@media (prefers-color-scheme: dark)` rules to apply.
        if (_session.History.Current is not null && !_busy)
            await RunNavigation((ct, fp) => _session.ReloadInteractiveAsync(BuildOptions(), ct, fp), "Theme reload");
    }

    private void ToggleDevTools()
    {
        if (_devtools is null) OpenDevTools(DevToolsTab.Performance);
        else CloseDevTools();
    }

    private async void BackClicked(object? s, EventArgs e)
    {
        if (!_session.History.CanGoBack || _busy) return;
        await RunNavigation((ct, fp) => _session.BackInteractiveAsync(BuildOptions(), ct, fp), "Back");
    }

    private async void ForwardClicked(object? s, EventArgs e)
    {
        if (!_session.History.CanGoForward || _busy) return;
        await RunNavigation((ct, fp) => _session.ForwardInteractiveAsync(BuildOptions(), ct, fp), "Forward");
    }

    private async void ReloadClicked(object? s, EventArgs e)
    {
        if (_session.History.Current is null || _busy) return;
        await RunNavigation((ct, fp) => _session.ReloadInteractiveAsync(BuildOptions(), ct, fp), "Reload");
    }

    private void StopClicked(object? s, EventArgs e) => _navCts?.Cancel();

    private async void OnLinkActivated(string resolvedUrl)
        => await NavigateAsync(resolvedUrl, ignoreEmpty: true);

    private void OnWebviewStatus(string text, bool isError) => _statusBar.SetHint(text, isError);

    // Build facts for the sidebar footer: the build's commit plus the JS and
    // render engines this process actually selected (single source of truth —
    // the same selectors the engine/paint pipeline read).
    private static BuildInfo GetBuildInfo()
        => new(GetBuildLabel(), GetJsEngineLabel(), GetRenderBackendLabel());

    private static string GetBuildLabel()
    {
        // Short build-sha label from assembly informational version. Falls
        // back to "" if none — the footer just renders a dash.
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? string.Empty;
        var plus = info.IndexOf('+');
        if (plus >= 0) info = info[(plus + 1)..];
        if (info.Length > 8) info = info[..8];
        return info;
    }

    // Canonical names matching the AppHost selection flags / README (--starling/
    // --jint, --imagesharp/--imagesharp-gpu).
    private static string GetJsEngineLabel() => JsEngineSelector.Selected switch
    {
        JsEngineKind.Jint => "jint",
        _ => "starling",
    };

    private static string GetRenderBackendLabel() => PaintBackendSelector.Selected switch
    {
        PaintBackendKind.ImageSharpWebGpu => "imagesharp-gpu",
        _ => "imagesharp",
    };

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
        await RunNavigation((ct, fp) => _session.NavigateInteractiveAsync(url, BuildOptions(), ct, fp), $"GET {url}");
    }

    private async Task RunNavigation(
        Func<CancellationToken, Action<LaidOutPage>, Task<Result<LaidOutPage, RenderError>>> navigate,
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

        // Progressive first paint: the engine invokes this from a background
        // continuation once render-blocking scripts have run, before deferred
        // scripts settle. Marshal to the UI thread and, if this navigation is
        // still current, show the page immediately so the user sees content
        // without waiting on async/analytics scripts.
        void OnFirstPaint(LaidOutPage page) => Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_navCts, myCts)) return;
            // Wall time from navigation start to "page is visible." This is
            // the user-meaningful "loaded and rendered" moment, distinct from
            // the navigate-task's full-settle time (which includes deferred
            // scripts that run after first paint).
            _diag.Log(DiagLevel.Info, "gui",
                $"first-paint: {sw.ElapsedMilliseconds} ms ({opLabel})");
            ApplyShownPage(page, opLabel, sw.ElapsedMilliseconds);
        });

        try
        {
            var result = await navigate(myCts.Token, OnFirstPaint);
            sw.Stop();
            if (!ReferenceEquals(_navCts, myCts)) return;

            if (result.IsErr)
            {
                _statusBar.SetState(StatusState.Error);
                _statusBar.SetHint($"{opLabel} failed: {result.Error.Message}", isError: true);
            }
            else
            {
                // Avoid a redundant double-show: when the deferred phase changed
                // nothing it returns the very page already shown via OnFirstPaint.
                // Re-showing it would dispose the live page (ShowPage disposes the
                // outgoing page, which would be this same instance).
                if (!ReferenceEquals(result.Value, _lastShownPage))
                    ApplyShownPage(result.Value, opLabel, sw.ElapsedMilliseconds);

                // The page's live JS context is attached only once the load
                // settles. In the no-mutation case above we skip the re-show, so
                // bind the live event loop here too (idempotent when ShowPage
                // already bound it for the reflowed-successor case).
                _webview.BindLiveScripting();
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

    /// <summary>
    /// Hand <paramref name="page"/> to the webview and sync the chrome (security
    /// lock, window title, URL bar, status info). Shared by progressive first
    /// paint and the final navigation result. Must run on the UI thread.
    /// </summary>
    private void ApplyShownPage(LaidOutPage page, string opLabel, long elapsedMs)
    {
        _webview.ShowPage(page);
        _lastShownPage = page;
        _urlBar.SetSecurity(MapSecurity(page.Security));
        Title = string.IsNullOrWhiteSpace(page.Title) ? string.Empty : page.Title;
        var current = _session.History.Current;
        if (!string.IsNullOrEmpty(current) && _urlBar.Address.Text != current)
            _urlBar.Address.Text = current;

        _statusBar.SetState(StatusState.Ready);
        _statusBar.SetHint($"{opLabel} → {elapsedMs} ms");
        _statusBar.SetInfo(
            view: $"{page.Viewport.Width}×{page.Viewport.Height}",
            doc:  $"{(int)page.DocumentHeight} px",
            hist: HistoryLabel());
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
        await RunNavigation((c, fp) => _session.BackInteractiveAsync(BuildOptions(), c, fp), "Back");
        return Snapshot(success: true);
    }

    public async Task<BrowserControlResult> ForwardFromToolAsync(CancellationToken ct)
    {
        if (!_session.History.CanGoForward)
            return Snapshot(success: false, error: "No forward history.");
        await RunNavigation((c, fp) => _session.ForwardInteractiveAsync(BuildOptions(), c, fp), "Forward");
        return Snapshot(success: true);
    }

    public async Task<BrowserControlResult> ReloadFromToolAsync(CancellationToken ct)
    {
        if (_session.History.Current is null)
            return Snapshot(success: false, error: "Nothing to reload.");
        await RunNavigation((c, fp) => _session.ReloadInteractiveAsync(BuildOptions(), c, fp), "Reload");
        return Snapshot(success: true);
    }

    private BrowserControlResult Snapshot(bool success, string? error = null, string? detail = null)
    {
        var url = _session.History.Current;
        var title = string.IsNullOrEmpty(Title) ? null : Title;
        return success
            ? BrowserControlResult.Success(url, title, _session.History.CanGoBack, _session.History.CanGoForward, _busy, detail)
            : BrowserControlResult.Failure(error ?? "Unknown error", url, title,
                _session.History.CanGoBack, _session.History.CanGoForward, _busy);
    }

    public Task<BrowserControlResult> ScreenshotFromToolAsync(string path, CancellationToken ct)
    {
        if (_lastShownPage is null)
            return Task.FromResult(Snapshot(success: false, error: "No page is loaded to screenshot."));
        try
        {
            var full = Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? "starling-screenshot.png" : path);
            var outcome = _session.CaptureToPng(_lastShownPage, full, _webview.LiveElapsedMs, fullPage: true);
            return Task.FromResult(Snapshot(success: true,
                detail: $"wrote {outcome.OutputPath} ({outcome.Width}x{outcome.Height})"));
        }
        catch (Exception ex)
        {
            _diag.Log(DiagLevel.Warn, "gui", $"screenshot failed: {ex.Message}");
            return Task.FromResult(Snapshot(success: false, error: $"screenshot failed: {ex.Message}"));
        }
    }

    public Task<BrowserControlResult> InspectFromToolAsync(bool includeHtml, string? logPath, CancellationToken ct)
    {
        try
        {
            var page = _lastShownPage;
            var url = _session.History.Current;

            var logs = _telemetry.Logs.Snapshot();
            var jsLogs = logs.Where(r => r.Category.Contains("engine.js", StringComparison.Ordinal)).ToArray();
            var errors = jsLogs.Count(r => r.Level >= LogLevel.Error);
            var warns = jsLogs.Count(r => r.Level == LogLevel.Warning);

            var html = page?.Document.DocumentElement is { } de ? HtmlSerializer.SerializeNode(de) : null;

            var sb = new StringBuilder();
            sb.Append("url: ").AppendLine(url ?? "(none)");
            sb.Append("title: ").AppendLine(string.IsNullOrEmpty(Title) ? "(none)" : Title);
            sb.Append("busy(loading): ").Append(_busy).AppendLine();
            sb.Append("liveScripting: ").Append(page?.Scripting is not null).AppendLine();
            sb.Append("documentHeightPx: ").Append(page is null ? 0 : (int)page.DocumentHeight).AppendLine();
            sb.Append("outerHtmlBytes: ").Append(html?.Length ?? 0).AppendLine();
            sb.Append("jsConsole: ").Append(jsLogs.Length).Append(" entries, ")
              .Append(errors).Append(" errors, ").Append(warns).AppendLine(" warnings");

            sb.AppendLine("--- recent JS console (warn+error, last 40) ---");
            foreach (var r in jsLogs.Where(r => r.Level >= LogLevel.Warning).TakeLast(40))
                sb.Append('[').Append(r.Level).Append("] ").AppendLine(r.Message);

            if (!string.IsNullOrWhiteSpace(logPath))
            {
                var full = Path.GetFullPath(logPath);
                var report = new StringBuilder();
                report.AppendLine(sb.ToString());
                report.AppendLine("=== ALL TELEMETRY LOGS ===");
                foreach (var r in logs)
                    report.Append(r.TimestampUtc.ToString("HH:mm:ss.fff")).Append(" [").Append(r.Level)
                          .Append("] ").Append(r.Category).Append(": ").AppendLine(r.Message);
                report.AppendLine().AppendLine("=== DOCUMENT outerHTML ===");
                report.AppendLine(html ?? "(no document)");
                var dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(full, report.ToString());
                sb.Append("wrote full report to: ").AppendLine(full);
            }

            if (includeHtml && html is not null)
            {
                sb.AppendLine("--- outerHTML (truncated 100k) ---");
                sb.AppendLine(html.Length > 100_000 ? html[..100_000] + "\n…(truncated)" : html);
            }

            return Task.FromResult(Snapshot(success: true, detail: sb.ToString()));
        }
        catch (Exception ex)
        {
            _diag.Log(DiagLevel.Warn, "gui", $"inspect failed: {ex.Message}");
            return Task.FromResult(Snapshot(success: false, error: $"inspect failed: {ex.Message}"));
        }
    }

    public Task<BrowserControlResult> ClickFromToolAsync(double x, double y, CancellationToken ct)
        => InputTool("click", () => _webview.ClickAt(x, y));

    public Task<BrowserControlResult> MoveMouseFromToolAsync(double x, double y, CancellationToken ct)
        => InputTool("move", () => _webview.MoveTo(x, y));

    public Task<BrowserControlResult> TypeTextFromToolAsync(string text, bool submit, CancellationToken ct)
        => InputTool("type", () => _webview.TypeText(text, submit));

    // Shared shell for the synthetic-input tools: guards page presence, runs the
    // panel action on the (already UI-thread) call, and maps its InputResult onto
    // a navigation-state snapshot (effect string in detail, precondition failure
    // in error).
    private Task<BrowserControlResult> InputTool(string name, Func<Controls.InputResult> action)
    {
        if (_lastShownPage is null)
            return Task.FromResult(Snapshot(success: false, error: $"No page is loaded to {name}."));
        try
        {
            var r = action();
            return Task.FromResult(r.Ok
                ? Snapshot(success: true, detail: r.Detail)
                : Snapshot(success: false, error: r.Detail));
        }
        catch (Exception ex)
        {
            _diag.Log(DiagLevel.Warn, "gui", $"{name} failed: {ex.Message}");
            return Task.FromResult(Snapshot(success: false, error: $"{name} failed: {ex.Message}"));
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _navCts?.Cancel();
        DestroyDevWindow();
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
