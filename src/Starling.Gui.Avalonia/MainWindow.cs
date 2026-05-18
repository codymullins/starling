using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Starling.Gui.Avalonia.Chrome;
using Starling.Gui.Avalonia.Controls;
using Starling.Gui.Avalonia.DevTools;
using Starling.Gui.Avalonia.Mcp;
using Starling.Gui.Avalonia.Theme;
using Tessera.Common;
using Tessera.Common.Diagnostics;
using Tessera.Engine;
using Tessera.Telemetry;
using EngineSize = SixLabors.ImageSharp.Size;
using RenderOptions = Tessera.Engine.RenderOptions;

namespace Starling.Gui.Avalonia;

/// <summary>
/// Composition root — sidebar (bookmarks/pinned/today) | toolbar over
/// <see cref="WebviewPanel"/> over status bar. Owns the <see cref="BrowserSession"/>
/// and the navigation flow. Mirrors the MAUI <c>MainPage</c> chrome.
/// </summary>
public sealed class MainWindow : Window, IBrowserController
{
    private static readonly EngineSize Viewport = new(1200, 900);

    // Hardcoded chrome content — port of MainPage.cs:23-43 verbatim so the
    // Avalonia sidebar matches the MAUI sidebar's seed state.
    private static readonly IReadOnlyList<TabInfo> Bookmarks =
    [
        new TabInfo("b1", "google.com",       "Google",                  Url: "https://google.com"),
        new TabInfo("b2", "justinjackson.ca", "Words — Justin Jackson",  Url: "https://justinjackson.ca/words.html"),
        new TabInfo("b3", "ladybird.org",     "Ladybird",                Url: "https://ladybird.org/"),
        new TabInfo("b4", "mcmaster.com",     "McMaster-Carr",           Url: "https://www.mcmaster.com/"),
    ];

    private static readonly IReadOnlyList<TabInfo> PinnedTabs =
    [
        new TabInfo("p1", "mail.fastmail.com", "Inbox",         Url: "https://mail.fastmail.com/"),
        new TabInfo("p2", "cal.starling.dev",  "Calendar",      Url: "https://cal.starling.dev/"),
    ];

    private static readonly IReadOnlyList<TabInfo> TodayTabs =
    [
        new TabInfo("t1", "justinjackson.ca", "Words — Justin Jackson", Audio: false, Url: "https://justinjackson.ca/words.html"),
        new TabInfo("t2", "starling.dev",     "Starling — Status",      Url: "https://starling.dev/"),
        new TabInfo("t3", "github.com",       "github.com/starling",    Url: "https://github.com/"),
        new TabInfo("t4", "localhost:3000",   "localhost:3000",         Url: "http://localhost:3000/"),
    ];

    private readonly ThemeManager _tm;
    private readonly IDiagnostics _diag;
    private readonly TelemetryStream _telemetry;
    private readonly BrowserSession _session;
    private readonly WebviewPanel _webview;
    private DevToolsPanel? _devtools;
    private GridSplitter? _devSplitter;

    private readonly IconButton _backButton;
    private readonly IconButton _forwardButton;
    private readonly IconButton _reloadButton;
    private readonly IconButton _stopButton;
    private readonly IconButton _themeButton;
    private readonly IconButton _devToolsButton;
    private readonly UrlBar _urlBar;
    private readonly StatusBar _statusBar;
    private readonly Border _toolbar;
    private readonly Sidebar _sidebar;
    private readonly Grid _contentStack;

    private CancellationTokenSource? _navCts;
    private bool _busy;

    public MainWindow()
    {
        _tm = Program.Services.GetRequiredService<ThemeManager>();
        _diag = Program.Services.GetRequiredService<IDiagnostics>();
        _telemetry = Program.Services.GetRequiredService<TelemetryStream>();
        _session = new BrowserSession(_diag);
        _webview = new WebviewPanel(_tm, _diag, OnLinkActivated, OnWebviewStatus);

        Title = "Starling (Avalonia)";
        MinWidth = 1024;
        MinHeight = 720;
        Width = 1280;
        Height = 860;

        _backButton = new IconButton(_tm, Icons.Back, "Back");
        _backButton.Clicked += BackClicked;
        _forwardButton = new IconButton(_tm, Icons.Fwd, "Forward");
        _forwardButton.Clicked += ForwardClicked;
        _reloadButton = new IconButton(_tm, Icons.Reload, "Reload");
        _reloadButton.Clicked += ReloadClicked;
        _stopButton = new IconButton(_tm, Icons.Stop, "Stop") { IsVisible = false };
        _stopButton.Clicked += StopClicked;
        _themeButton = new IconButton(_tm, _tm.Theme == ThemeMode.Dark ? Icons.Sun : Icons.Moon, "Toggle theme");
        _themeButton.Clicked += (_, _) => ToggleTheme();
        _devToolsButton = new IconButton(_tm, Icons.Bug, "Toggle DevTools");
        _devToolsButton.Clicked += (_, _) => ToggleDevTools();

        _urlBar = new UrlBar(_tm);
        _urlBar.Submitted += async (_, _) => await NavigateAsync(_urlBar.Address.Text, ignoreEmpty: false);

        var reloadCell = new Panel { Children = { _reloadButton, _stopButton } };

        var toolbarGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto,Auto"),
            Margin = new Thickness(12, 6),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        toolbarGrid.Children.Add(_backButton); Grid.SetColumn(_backButton, 0);
        toolbarGrid.Children.Add(_forwardButton); Grid.SetColumn(_forwardButton, 1);
        toolbarGrid.Children.Add(reloadCell); Grid.SetColumn(reloadCell, 2);
        toolbarGrid.Children.Add(_urlBar); Grid.SetColumn(_urlBar, 3);
        toolbarGrid.Children.Add(_devToolsButton); Grid.SetColumn(_devToolsButton, 4);
        toolbarGrid.Children.Add(_themeButton); Grid.SetColumn(_themeButton, 5);

        _toolbar = new Border
        {
            Child = toolbarGrid,
            BorderThickness = new Thickness(0, 0, 0, 1),
        };

        _statusBar = new StatusBar(_tm);

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

        // The "t1" id matches MAUI's seed-active state — the words.html tab in
        // the Today section is rendered with the active rail/surface tint.
        _sidebar = new Sidebar(_tm, Bookmarks, PinnedTabs, TodayTabs, activeId: "t1", OnSidebarTabActivated);

        var rootGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        rootGrid.Children.Add(_sidebar); Grid.SetColumn(_sidebar, 0);
        rootGrid.Children.Add(_contentStack); Grid.SetColumn(_contentStack, 1);
        Content = rootGrid;

        ApplyTheme();
        _tm.Changed += (_, _) => ApplyTheme();
        _statusBar.SetLeft("Ready");
        UpdateNavButtonStates();
    }

    private void ApplyTheme()
    {
        var t = _tm.Tokens;
        Background = new SolidColorBrush(t.Bg);
        _toolbar.Background = new SolidColorBrush(t.Panel);
        _toolbar.BorderBrush = new SolidColorBrush(t.Border);
    }

    private void ToggleTheme()
        => _tm.SetTheme(_tm.Theme == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark);

    private void ToggleDevTools()
    {
        // The middle row of _contentStack hosts a Grid with one column (the
        // webview). Toggle DevTools by replacing that Grid with a 3-column
        // layout adding a splitter + DevToolsPanel on the right; tearing down
        // reverts to single-column.
        var middle = (Grid)((Control)_contentStack.Children[1]);
        if (_devtools is null)
        {
            _devtools = new DevToolsPanel(_tm, _telemetry);
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

    private void OnWebviewStatus(string text, bool isError) => _statusBar.SetLeft(text, isError);

    private async void OnSidebarTabActivated(TabInfo tab)
    {
        if (string.IsNullOrWhiteSpace(tab.Url)) return;
        await NavigateAsync(tab.Url, ignoreEmpty: true);
    }

    private async Task NavigateAsync(string? raw, bool ignoreEmpty)
    {
        var url = (raw ?? string.Empty).Trim();
        if (url.Length == 0)
        {
            if (!ignoreEmpty) _statusBar.SetLeft("Enter a URL first.", isError: true);
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
        using var navSpan = _diag.Span("gui", "navigate");
        try
        {
            var result = await navigate(myCts.Token);
            sw.Stop();
            if (!ReferenceEquals(_navCts, myCts)) return;

            if (result.IsErr)
            {
                _statusBar.SetLeft($"{opLabel} failed: {result.Error.Message}", isError: true);
            }
            else
            {
                _webview.ShowPage(result.Value);
                if (!string.IsNullOrWhiteSpace(result.Value.Title))
                    Title = $"{result.Value.Title} — Starling (Avalonia)";
                var current = _session.History.Current;
                if (!string.IsNullOrEmpty(current) && _urlBar.Address.Text != current)
                    _urlBar.Address.Text = current;
                _statusBar.SetLeft(
                    $"{opLabel} → {result.Value.Viewport.Width}×{(int)result.Value.DocumentHeight} px · {sw.ElapsedMilliseconds} ms");
            }
        }
        catch (OperationCanceledException) when (myCts.IsCancellationRequested)
        {
            if (ReferenceEquals(_navCts, myCts))
                _statusBar.SetLeft($"{opLabel} canceled.", isError: true);
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_navCts, myCts))
                _statusBar.SetLeft($"{opLabel} threw: {ex.Message}", isError: true);
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
        _statusBar.SetLeft($"{label}…");
    }

    private void EndBusy()
    {
        _busy = false;
        _stopButton.IsVisible = false;
        _reloadButton.IsVisible = true;
        UpdateNavButtonStates();
    }

    private void UpdateNavButtonStates()
    {
        _backButton.SetEnabled(_session.History.CanGoBack && !_busy);
        _forwardButton.SetEnabled(_session.History.CanGoForward && !_busy);
        _reloadButton.SetEnabled(_session.History.Current is not null && !_busy);
    }

    private static RenderOptions BuildOptions() => new(Viewport, FontSize: 16f);

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
}
