using System.Diagnostics;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;
using Tessera.Common.Diagnostics;
using Tessera.Css.Properties;
using Tessera.Css.Values;
using Tessera.Engine;
using EngineSize = SixLabors.ImageSharp.Size;
using MauiColor = Microsoft.Maui.Graphics.Color;
using DomElement = Tessera.Dom.Element;

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
    private readonly PageRenderer _pageRenderer = new();

    // The single Skia-painted page surface, replacing BoxTreeRenderer's native
    // MAUI view tree. The page is one flat bitmap (_pageImage); selection and
    // hover highlights are overlay BoxViews siblings of it inside _pageCanvas.
    private readonly AbsoluteLayout _pageCanvas;
    private readonly Image _pageImage;
    private readonly List<BoxView> _highlightOverlays = new();
    private readonly List<BoxView> _selectionOverlays = new();

    // Find index + drag-select hit-testing both walk the box tree's text
    // fragments in document-space CSS px (BoxHitTester.CollectFragments).
    private List<BoxHitTester.PlacedFragment> _fragments = new();
    private readonly List<(double Y, string Text)> _findIndex = new();
    private int _findCursor;
    private LaidOutPage? _currentPage;
    private bool _busy;

    // Hover state: the anchor element currently under the pointer, so we only
    // repaint the overlay when it actually changes.
    private DomElement? _hoverAnchor;
    // Drag-select anchor point in document-space coordinates; null when no
    // drag is in progress. PanGestureRecognizer only reports deltas, so the
    // pan origin is captured from the last pointer-moved position and the
    // running cursor is reconstructed from the cumulative pan delta.
    private (double X, double Y)? _selectAnchor;
    private (double X, double Y)? _panOrigin;
    private (double X, double Y) _lastPointerDoc;

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
            Orientation = ScrollOrientation.Both,
            BackgroundColor = Colors.White,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
        };

        // The Skia-painted page surface: a single Image holding the bitmap
        // SkiaGraphiteBackend produced from the display list. BoxTreeRenderer's
        // per-primitive native view tree is gone — interaction is re-derived
        // from the box tree by BoxHitTester instead of from native sub-views.
        _pageImage = new Image
        {
            Aspect = Aspect.Fill,
        };
        _pageCanvas = new AbsoluteLayout { BackgroundColor = Colors.White };
        AbsoluteLayout.SetLayoutFlags(_pageImage, AbsoluteLayoutFlags.None);
        _pageCanvas.Children.Add(_pageImage);

        // One canvas-level pointer handler drives the :hover re-cascade — the
        // per-Label PointerGestureRecognizers BoxTreeRenderer attached are
        // gone. It hit-tests the box tree and repaints the hover overlay.
        var pagePointer = new PointerGestureRecognizer();
        pagePointer.PointerMoved += OnPagePointerMoved;
        pagePointer.PointerExited += OnPagePointerExited;
        _pageImage.GestureRecognizers.Add(pagePointer);

        // Tap → hit-test the box tree → navigate if over a link box.
        var pageTap = new TapGestureRecognizer();
        pageTap.Tapped += OnPageTapped;
        _pageImage.GestureRecognizers.Add(pageTap);

        // Drag → box-tree text-fragment hit-testing builds a selection
        // highlight that flows across fragments between anchor and cursor.
        var pagePan = new PanGestureRecognizer();
        pagePan.PanUpdated += OnPagePanUpdated;
        _pageImage.GestureRecognizers.Add(pagePan);

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
        // stylesheets) before swapping in the new render.
        _currentPage?.Dispose();
        _currentPage = page;

        // Reset interaction state from the previous page.
        _hoverAnchor = null;
        _selectAnchor = null;
        ClearHighlights();

        // Unified paint path: box tree → DisplayListBuilder → SkiaGraphiteBackend
        // — the exact pipeline the headless renderer runs. The result is a
        // single flat bitmap, not a native MAUI view tree.
        using var bitmap = _pageRenderer.Render(page.Root);
        _pageImage.Source = PageRenderer.ToImageSource(bitmap);

        // Size the surface to the full document so the ScrollView scrolls the
        // whole page; the bitmap is rendered at document dimensions.
        var docWidth = Math.Max(1, page.Root.Frame.Width);
        var docHeight = Math.Max(1, page.Root.Frame.Height);
        AbsoluteLayout.SetLayoutBounds(_pageImage, new Rect(0, 0, docWidth, docHeight));
        _pageCanvas.WidthRequest = docWidth;
        _pageCanvas.HeightRequest = docHeight;
        _pageScroll.Content = _pageCanvas;

        if (!string.IsNullOrWhiteSpace(page.Title)) Title = page.Title!;

        // Collect text fragments once (document-space rects) — shared by the
        // Cmd-F find index and drag-select hit-testing.
        _fragments = BoxHitTester.CollectFragments(page.Root);
        RebuildFindIndex();
    }

    // --- Interaction re-derived from the laid-out box tree ------------------

    private void OnPagePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_currentPage is null) return;
        var pos = e.GetPosition(_pageImage);
        if (pos is not { } p) return;

        // The image is rendered 1:1 at document dimensions, so image-local
        // coordinates already are document-space coordinates. Remember the last
        // position so the PanGestureRecognizer (delta-only) can anchor a drag.
        _lastPointerDoc = (p.X, p.Y);
        var hit = BoxHitTester.HitTest(_currentPage.Root, p.X, p.Y);
        var anchor = hit.LinkAnchor;
        if (ReferenceEquals(anchor, _hoverAnchor)) return;

        _hoverAnchor = anchor;
        ApplyHoverHighlight();
    }

    private void OnPagePointerExited(object? sender, PointerEventArgs e)
    {
        if (_hoverAnchor is null) return;
        _hoverAnchor = null;
        ApplyHoverHighlight();
    }

    /// <summary>
    /// Repaints the hover overlay. The :hover re-cascade fires here: when the
    /// pointer is over a link, the style engine recomputes the anchor's style
    /// with <see cref="Css.Selectors.SelectorMatchContext.HoveredElement"/> set,
    /// and the hovered text colour is drawn as a translucent tint over the
    /// link's text-fragment rects.
    /// </summary>
    /// <remarks>
    /// v1 limitation: this presents the :hover result as an overlay tint rather
    /// than reflowing the whole page. A :hover rule that only changes paint
    /// (colour / text-decoration — the common case) reads correctly; one that
    /// changes layout (font-size, display) is not reflowed. A full re-cascade +
    /// re-layout would need a LayoutEngine that threads the hover context, which
    /// is out of this WP's GUI-only scope — noted in the handoff log.
    /// </remarks>
    private void ApplyHoverHighlight()
    {
        ClearHoverHighlights();
        if (_currentPage is null || _hoverAnchor is null) return;

        var hovered = _currentPage.Style.Compute(
            _hoverAnchor, new Css.Selectors.SelectorMatchContext { HoveredElement = _hoverAnchor });
        var color = hovered.GetColor(PropertyId.Color);
        var tint = MauiColor.FromRgba(color.R, color.G, color.B, (byte)64);

        foreach (var rect in LinkFragmentRects(_hoverAnchor))
        {
            var overlay = new BoxView { Color = tint, InputTransparent = true };
            AbsoluteLayout.SetLayoutFlags(overlay, AbsoluteLayoutFlags.None);
            AbsoluteLayout.SetLayoutBounds(overlay, rect);
            _pageCanvas.Children.Add(overlay);
            _highlightOverlays.Add(overlay);
        }
    }

    /// <summary>
    /// Document-space rects of every text fragment whose nearest enclosing
    /// anchor is <paramref name="anchor"/> — the painted extent of one link.
    /// </summary>
    private IEnumerable<Rect> LinkFragmentRects(DomElement anchor)
    {
        if (_currentPage is null) yield break;
        foreach (var rect in WalkLinkFragments(_currentPage.Root, anchor, 0, 0))
            yield return rect;
    }

    private static IEnumerable<Rect> WalkLinkFragments(
        Tessera.Layout.Box.Box box, DomElement anchor, double originX, double originY)
    {
        var frameX = originX + box.Frame.X;
        var frameY = originY + box.Frame.Y;

        if (box is Tessera.Layout.Box.TextBox tb)
        {
            if (ReferenceEquals(BoxHitTester.FindLinkAnchor(tb), anchor))
            {
                foreach (var frag in tb.Fragments)
                {
                    if (string.IsNullOrWhiteSpace(frag.Text)) continue;
                    yield return new Rect(
                        frameX + frag.X, frameY + frag.Y, frag.Width, frag.Height);
                }
            }
            yield break;
        }

        var contentX = frameX + box.Border.Left + box.Padding.Left;
        var contentY = frameY + box.Border.Top + box.Padding.Top;
        foreach (var child in box.Children)
            foreach (var rect in WalkLinkFragments(child, anchor, contentX, contentY))
                yield return rect;
    }

    private async void OnPageTapped(object? sender, TappedEventArgs e)
    {
        if (_currentPage is null || _busy) return;
        var pos = e.GetPosition(_pageImage);
        if (pos is not { } p) return;

        var hit = BoxHitTester.HitTest(_currentPage.Root, p.X, p.Y);
        var href = hit.LinkAnchor?.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(href)) return;
        await OnLinkActivated(href);
    }

    private void OnPagePanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (_currentPage is null) return;

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                // PanGestureRecognizer reports deltas, not absolute positions;
                // anchor the selection at the last hovered point. The running
                // cursor is reconstructed from the cumulative pan delta.
                _selectAnchor = _lastPointerDoc;
                _panOrigin = _lastPointerDoc;
                ClearSelectionHighlights();
                break;
            case GestureStatus.Running when _selectAnchor is { } anchor && _panOrigin is { } origin:
                var cursor = (origin.X + e.TotalX, origin.Y + e.TotalY);
                UpdateSelectionHighlight(anchor, cursor);
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                _selectAnchor = null;
                _panOrigin = null;
                break;
        }
    }

    /// <summary>
    /// Builds the drag-select highlight: every text fragment between the anchor
    /// point and the cursor (in document order) is tinted. Selection flows
    /// across paragraph boundaries because <see cref="_fragments"/> is in
    /// document order.
    /// </summary>
    private void UpdateSelectionHighlight((double X, double Y) anchor, (double X, double Y) cursor)
    {
        ClearSelectionHighlights();
        if (_fragments.Count == 0) return;

        var startIdx = NearestFragmentIndex(anchor);
        var endIdx = NearestFragmentIndex(cursor);
        if (startIdx < 0 || endIdx < 0) return;
        if (startIdx > endIdx) (startIdx, endIdx) = (endIdx, startIdx);

        for (var i = startIdx; i <= endIdx; i++)
        {
            var f = _fragments[i];
            var overlay = new BoxView
            {
                Color = MauiColor.FromRgba(80, 140, 255, 96),
                InputTransparent = true,
            };
            AbsoluteLayout.SetLayoutFlags(overlay, AbsoluteLayoutFlags.None);
            AbsoluteLayout.SetLayoutBounds(overlay, new Rect(f.X, f.Y, f.Width, f.Height));
            _pageCanvas.Children.Add(overlay);
            _highlightOverlays.Add(overlay);
            _selectionOverlays.Add(overlay);
        }
        var text = string.Join(" ", _fragments.GetRange(startIdx, endIdx - startIdx + 1)
            .ConvertAll(f => f.Text));
        SetStatus($"Selected {text.Length} chars", isError: false);
    }

    /// <summary>
    /// Index of the fragment nearest the point — the one containing it, or the
    /// closest by centre distance if the point is in inter-fragment whitespace.
    /// </summary>
    private int NearestFragmentIndex((double X, double Y) point)
    {
        var best = -1;
        var bestDist = double.MaxValue;
        for (var i = 0; i < _fragments.Count; i++)
        {
            var f = _fragments[i];
            if (point.X >= f.X && point.X < f.X + f.Width &&
                point.Y >= f.Y && point.Y < f.Y + f.Height)
                return i;
            var cx = f.X + (f.Width / 2);
            var cy = f.Y + (f.Height / 2);
            var dist = ((point.X - cx) * (point.X - cx)) + ((point.Y - cy) * (point.Y - cy));
            if (dist < bestDist) { bestDist = dist; best = i; }
        }
        return best;
    }

    private void ClearHighlights()
    {
        foreach (var overlay in _highlightOverlays)
            _pageCanvas.Children.Remove(overlay);
        _highlightOverlays.Clear();
        _selectionOverlays.Clear();
    }

    private void ClearHoverHighlights()
    {
        // Hover overlays are the highlight overlays that are not selection
        // overlays; rebuild the list keeping selection overlays.
        for (var i = _highlightOverlays.Count - 1; i >= 0; i--)
        {
            var overlay = _highlightOverlays[i];
            if (_selectionOverlays.Contains(overlay)) continue;
            _pageCanvas.Children.Remove(overlay);
            _highlightOverlays.RemoveAt(i);
        }
    }

    private void ClearSelectionHighlights()
    {
        foreach (var overlay in _selectionOverlays)
        {
            _pageCanvas.Children.Remove(overlay);
            _highlightOverlays.Remove(overlay);
        }
        _selectionOverlays.Clear();
    }

    private async Task OnLinkActivated(string href)
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

    /// <summary>
    /// Rebuilds the Cmd-F find index from the box tree's text fragments — the
    /// same document-space fragments drag-select hit-tests. Each entry maps a
    /// match string to its absolute Y so a hit can be scrolled into view.
    /// </summary>
    private void RebuildFindIndex()
    {
        _findIndex.Clear();
        _findCursor = 0;
        foreach (var frag in _fragments)
            _findIndex.Add((frag.Y, frag.Text));
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
