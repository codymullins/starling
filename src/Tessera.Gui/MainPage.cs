using System.Diagnostics;
using Microsoft.Maui.Controls.Shapes;
using Tessera.Common.Diagnostics;
using Tessera.Engine;
using EngineSize = SixLabors.ImageSharp.Size;

namespace Tessera.Gui;

public sealed class MainPage : ContentPage
{
    private static readonly EngineSize Viewport = new(1200, 900);

    private readonly Entry _addressEntry;
    private readonly Entry _findEntry;
    private readonly Button _goButton;
    private readonly Button _backButton;
    private readonly Button _forwardButton;
    private readonly Button _reloadButton;
    private readonly Button _findNextButton;
    private readonly ScrollView _pageScroll;
    private readonly Editor _statusLabel;
    private readonly Label _titleLabel;
    private readonly Border _placeholder;
    private readonly BrowserSession _session;
    private readonly IDiagnostics _diag;
    private readonly List<(double Y, string Text)> _findIndex = new();
    private int _findCursor;
    private LaidOutPage? _currentPage;
    private bool _busy;

    public MainPage(IDiagnostics diag)
    {
        Title = "Tessera";
        BackgroundColor = Palette.Page;

        _diag = diag;
        _session = new BrowserSession(diag);

        _addressEntry = new Entry
        {
            Placeholder = "https://example.com or file:///path/to/page.html",
            TextColor = Palette.Text,
            PlaceholderColor = Palette.Muted,
            BackgroundColor = Palette.Input,
            FontSize = 14,
            ReturnType = ReturnType.Go,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
        };
        _addressEntry.Completed += OnAddressCompleted;

        _findEntry = new Entry
        {
            Placeholder = "Find in page",
            TextColor = Palette.Text,
            PlaceholderColor = Palette.Muted,
            BackgroundColor = Palette.Input,
            FontSize = 13,
            ReturnType = ReturnType.Search,
            WidthRequest = 180,
        };
        _findEntry.Completed += (_, _) => FindNext();
        _findEntry.TextChanged += (_, _) => { _findCursor = 0; FindNext(); };

        _backButton = ChromeButton("‹", BackClicked);
        _forwardButton = ChromeButton("›", ForwardClicked);
        _reloadButton = ChromeButton("↻", ReloadClicked);
        _goButton = AccentButton("Go", GoClicked);
        _findNextButton = ChromeButton("⏎", (_, _) => FindNext());
        SetNavButtonStates();

        _pageScroll = new ScrollView
        {
            Orientation = ScrollOrientation.Vertical,
            BackgroundColor = Colors.White,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
        };

        _placeholder = new Border
        {
            BackgroundColor = Palette.Editor,
            Stroke = Palette.Edge,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Content = new Label
            {
                Text = "Type a URL above and press Go to render a page.",
                TextColor = Palette.Muted,
                FontSize = 14,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
            },
        };
        _pageScroll.Content = _placeholder;

        _titleLabel = new Label
        {
            Text = "Tessera",
            TextColor = Palette.Text,
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
        };

        // Editor instead of Label so the text is system-selectable: drag to
        // select, cmd-C / right-click → Copy work natively on Mac Catalyst
        // (UITextView under the hood). IsReadOnly keeps it non-editable.
        _statusLabel = new Editor
        {
            Text = $"Ready. Pure-managed .NET browser; renderer viewport {Viewport.Width}×{Viewport.Height} CSS px.",
            TextColor = Palette.Muted,
            BackgroundColor = Colors.Transparent,
            FontSize = 12,
            IsReadOnly = true,
            AutoSize = EditorAutoSizeOption.TextChanges,
            Margin = new Thickness(-4, -8),
        };

        Content = BuildLayout();

        // Native menu bar. On Mac Catalyst this maps to the system "Edit"
        // menu so Cmd-F resolves through the OS like in every other app.
        var findItem = new MenuFlyoutItem { Text = "Find…" };
        findItem.KeyboardAccelerators.Add(new KeyboardAccelerator
        {
            Key = "F",
            Modifiers = KeyboardAcceleratorModifiers.Cmd,
        });
        findItem.Clicked += (_, _) =>
        {
            _findEntry.Focus();
            _findEntry.CursorPosition = 0;
            _findEntry.SelectionLength = _findEntry.Text?.Length ?? 0;
        };
        var findNextItem = new MenuFlyoutItem { Text = "Find Next" };
        findNextItem.KeyboardAccelerators.Add(new KeyboardAccelerator
        {
            Key = "G",
            Modifiers = KeyboardAcceleratorModifiers.Cmd,
        });
        findNextItem.Clicked += (_, _) => FindNext();

        var editMenu = new MenuBarItem { Text = "Edit" };
        editMenu.Add(findItem);
        editMenu.Add(findNextItem);
        MenuBarItems.Add(editMenu);
    }

    private Grid BuildLayout()
    {
        var addressRow = new Grid
        {
            ColumnSpacing = 8,
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
        addressRow.Add(_backButton, 0, 0);
        addressRow.Add(_forwardButton, 1, 0);
        addressRow.Add(_reloadButton, 2, 0);
        addressRow.Add(_addressEntry, 3, 0);
        addressRow.Add(_goButton, 4, 0);
        addressRow.Add(_findEntry, 5, 0);
        addressRow.Add(_findNextButton, 6, 0);

        var headerBar = new Border
        {
            BackgroundColor = Palette.Panel,
            Stroke = Palette.Edge,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(12),
            Content = new VerticalStackLayout
            {
                Spacing = 10,
                Children = { BuildTitleRow(), addressRow },
            },
        };

        var viewportPanel = new Border
        {
            BackgroundColor = Palette.Panel,
            Stroke = Palette.Edge,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(0),
            Content = _pageScroll,
        };

        var statusBar = new Border
        {
            BackgroundColor = Palette.Panel,
            Stroke = Palette.Edge,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(12, 8),
            Content = _statusLabel,
        };

        var root = new Grid
        {
            Padding = new Thickness(16),
            RowSpacing = 12,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            },
        };
        root.Add(headerBar, 0, 0);
        root.Add(viewportPanel, 0, 1);
        root.Add(statusBar, 0, 2);
        return root;
    }

    private Grid BuildTitleRow()
    {
        return new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            Children =
            {
                _titleLabel,
                BuildPill("M2 — static rendering · live HTTPS · keep-alive · WHATWG encoding"),
            },
        };
    }

    private static Border BuildPill(string text)
    {
        var pill = new Border
        {
            BackgroundColor = Palette.Pill,
            Stroke = Palette.Edge,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(10, 6),
            Content = new Label
            {
                Text = text,
                TextColor = Palette.Accent,
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
            },
        };
        Grid.SetColumn(pill, 1);
        return pill;
    }

    private static Button ChromeButton(string text, EventHandler handler)
    {
        var button = new Button
        {
            Text = text,
            BackgroundColor = Palette.Button,
            TextColor = Palette.Text,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            WidthRequest = 38,
            HeightRequest = 38,
            Padding = new Thickness(0),
            CornerRadius = 8,
        };
        button.Clicked += handler;
        return button;
    }

    private static Button AccentButton(string text, EventHandler handler)
    {
        var button = new Button
        {
            Text = text,
            BackgroundColor = Palette.Accent,
            TextColor = Colors.Black,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            Padding = new Thickness(18, 8),
            CornerRadius = 8,
        };
        button.Clicked += handler;
        return button;
    }

    private async void OnAddressCompleted(object? sender, EventArgs e)
        => await NavigateAsync(_addressEntry.Text, ignoreEmpty: false);

    private async void GoClicked(object? sender, EventArgs e)
        => await NavigateAsync(_addressEntry.Text, ignoreEmpty: false);

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

    private async Task NavigateAsync(string? rawUrl, bool ignoreEmpty)
    {
        if (_busy) return;
        var url = (rawUrl ?? string.Empty).Trim();
        if (url.Length == 0)
        {
            if (!ignoreEmpty) SetStatus("Enter a URL first.", isError: true);
            return;
        }
        _addressEntry.Text = url;
        await RunNavigation(ct => _session.NavigateInteractiveAsync(url, BuildOptions(), ct), $"GET {url}");
    }

    private async Task RunNavigation(Func<CancellationToken, Task<Common.Result<LaidOutPage, RenderError>>> navigate, string opLabel)
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

            ShowPage(result.Value);
            var current = _session.History.Current ?? "(no url)";
            SetStatus(
                $"{opLabel} → {result.Value.Viewport.Width}×{(int)result.Value.DocumentHeight} px, " +
                $"{stopwatch.ElapsedMilliseconds} ms · {current}",
                isError: false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or HttpRequestException)
        {
            SetStatus($"{opLabel} threw: {ex.Message}", isError: true);
        }
        finally
        {
            EndBusy();
        }
    }

    private void ShowPage(LaidOutPage page)
    {
        // Dispose the previously-shown page (releases its image bitmaps and
        // stylesheets) before swapping in the new view tree.
        _currentPage?.Dispose();
        _currentPage = page;

        var view = BoxTreeRenderer.Build(page.Root, page.Style, OnLinkActivated);
        _pageScroll.Content = view;
        if (!string.IsNullOrWhiteSpace(page.Title)) Title = page.Title!;

        // Rebuild the find index so Cmd-F / the find bar can scroll to matches.
        RebuildFindIndex(page);
    }

    private async void OnLinkActivated(string href)
    {
        if (_busy) return;
        var current = _currentPage?.Url;
        var resolved = ResolveLink(href, current);
        if (resolved is null)
        {
            SetStatus($"Bad link: {href}", isError: true);
            return;
        }
        await NavigateAsync(resolved, ignoreEmpty: true);
    }

    private static string? ResolveLink(string href, string? baseUrl)
    {
        href = href.Trim();
        if (href.Length == 0 || href.StartsWith("#", StringComparison.Ordinal))
            return null;
        if (Uri.TryCreate(href, UriKind.Absolute, out var abs)) return abs.ToString();
        if (baseUrl is not null && Uri.TryCreate(new Uri(baseUrl), href, out var combined))
            return combined.ToString();
        return null;
    }

    private void RebuildFindIndex(LaidOutPage page)
    {
        _findIndex.Clear();
        _findCursor = 0;
        WalkForFind(page.Root, originX: 0, originY: 0);
    }

    private void WalkForFind(Tessera.Layout.Box.Box box, double originX, double originY)
    {
        var fx = originX + box.Frame.X;
        var fy = originY + box.Frame.Y;
        if (box is Tessera.Layout.Box.TextBox tb)
        {
            foreach (var frag in tb.Fragments)
            {
                if (string.IsNullOrWhiteSpace(frag.Text)) continue;
                _findIndex.Add((fy + frag.Y, frag.Text));
            }
            return;
        }
        var cx = fx + box.Border.Left + box.Padding.Left;
        var cy = fy + box.Border.Top + box.Padding.Top;
        foreach (var child in box.Children) WalkForFind(child, cx, cy);
    }

    private async void FindNext()
    {
        var query = (_findEntry.Text ?? string.Empty).Trim();
        if (query.Length == 0 || _findIndex.Count == 0) return;

        // Search from the cursor; wrap if we hit the end.
        for (var i = 0; i < _findIndex.Count; i++)
        {
            var idx = (_findCursor + i) % _findIndex.Count;
            var (y, text) = _findIndex[idx];
            if (text.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                _findCursor = idx + 1;
                // Anchor the match a third of the way down the visible area.
                var targetY = Math.Max(0, y - _pageScroll.Height / 3);
                await _pageScroll.ScrollToAsync(0, targetY, animated: true);
                SetStatus($"Find: '{query}' at y={y:F0}", isError: false);
                return;
            }
        }
        SetStatus($"Find: '{query}' — no matches", isError: true);
    }

    private void BeginBusy(string label)
    {
        _busy = true;
        _goButton.IsEnabled = false;
        _backButton.IsEnabled = false;
        _forwardButton.IsEnabled = false;
        _reloadButton.IsEnabled = false;
        SetStatus($"{label}…", isError: false);
    }

    private void EndBusy()
    {
        _busy = false;
        _goButton.IsEnabled = true;
        SetNavButtonStates();
    }

    private void SetNavButtonStates()
    {
        _backButton.IsEnabled = _session.History.CanGoBack;
        _forwardButton.IsEnabled = _session.History.CanGoForward;
        _reloadButton.IsEnabled = _session.History.Current is not null;
    }

    private void SetStatus(string text, bool isError)
    {
        _statusLabel.Text = text;
        _statusLabel.TextColor = isError ? Palette.Danger : Palette.Muted;
    }

    private static RenderOptions BuildOptions()
        => new(Viewport, FontSize: 16f);

    private static class Palette
    {
        public static readonly Color Page = Color.FromArgb("#0E1115");
        public static readonly Color Panel = Color.FromArgb("#181D22");
        public static readonly Color Editor = Color.FromArgb("#0B0E10");
        public static readonly Color Input = Color.FromArgb("#11161A");
        public static readonly Color Edge = Color.FromArgb("#303941");
        public static readonly Color Text = Color.FromArgb("#E9EEF2");
        public static readonly Color Muted = Color.FromArgb("#9AA7B2");
        public static readonly Color Accent = Color.FromArgb("#9EE493");
        public static readonly Color Danger = Color.FromArgb("#FF7A7A");
        public static readonly Color Button = Color.FromArgb("#24313A");
        public static readonly Color Pill = Color.FromArgb("#142116");
    }
}
