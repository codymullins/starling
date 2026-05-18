using Microsoft.Maui.Layouts;
using Tessera.Common.Diagnostics;
using Tessera.Css.Properties;
using Tessera.Engine;
using Tessera.Gui.Theme;
using Tessera.Url;
using MauiColor = Microsoft.Maui.Graphics.Color;
using DomElement = Tessera.Dom.Element;

namespace Tessera.Gui.Chrome;

/// <summary>
/// The rendered-page surface and all of its interaction — extracted verbatim
/// (behaviour-for-behaviour) from the M2-era <c>MainPage</c>. Owns the Skia page
/// bitmap, the hover / drag-select overlays, and the Cmd-F find index, all
/// re-derived from the laid-out box tree via <see cref="BoxHitTester"/>.
///
/// The webview deliberately does <em>not</em> follow the chrome theme — it
/// always paints against <c>--web-bg</c> (HANDOFF §6.2).
/// </summary>
public sealed class WebviewPanel : Grid
{
    private readonly ThemeManager _tm;
    private readonly Action<string> _onLinkActivated;
    private readonly Action<string, bool> _onStatus;
    private readonly IDiagnostics _diag;
    private readonly PageRenderer _pageRenderer;

    private readonly ScrollView _pageScroll;
    private readonly Image _pageImage;
    private readonly AbsoluteLayout _pageCanvas;
    private readonly Border _placeholder;
    private readonly List<BoxView> _highlightOverlays = new();
    private readonly List<BoxView> _selectionOverlays = new();

    // Find bar — hidden until invoked via the URL-bar find button or Cmd-F.
    private readonly Grid _findBar;
    private readonly Entry _findEntry;

    private List<BoxHitTester.PlacedFragment> _fragments = new();
    private readonly List<(double Y, string Text)> _findIndex = new();
    private int _findCursor;
    private LaidOutPage? _currentPage;

    private DomElement? _hoverAnchor;
    private (double X, double Y)? _selectAnchor;
    private (double X, double Y)? _panOrigin;
    private (double X, double Y) _lastPointerDoc;

    public WebviewPanel(ThemeManager tm, Action<string> onLinkActivated, Action<string, bool> onStatus, IDiagnostics? diagnostics = null)
    {
        _tm = tm;
        _onLinkActivated = onLinkActivated;
        _onStatus = onStatus;
        _diag = diagnostics ?? NoopDiagnostics.Instance;
        _pageRenderer = new PageRenderer(_diag);
        var t = tm.Tokens;

        RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // find bar
        RowDefinitions.Add(new RowDefinition(GridLength.Star)); // page surface

        _findEntry = new Entry
        {
            Placeholder = "Find in page",
            FontSize = tm.Metrics.FsSm,
            ReturnType = ReturnType.Search,
            BackgroundColor = t.Surface,
            TextColor = t.Text,
            PlaceholderColor = t.Muted,
            HorizontalOptions = LayoutOptions.Fill,
        };
        _findEntry.Completed += (_, _) => FindNext();
        _findEntry.TextChanged += (_, _) => { _findCursor = 0; FindNext(); };

        var findNext = new IconButton(tm, Icons.Enter, "Find next");
        findNext.Clicked += (_, _) => FindNext();
        var findClose = new IconButton(tm, Icons.Close, "Close find");
        findClose.Clicked += (_, _) => _findBar.IsVisible = false;

        _findBar = new Grid
        {
            IsVisible = false,
            BackgroundColor = t.Panel,
            Padding = new Thickness(12, 6),
            ColumnSpacing = 6,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        _findBar.Add(_findEntry, 0, 0);
        _findBar.Add(findNext, 1, 0);
        _findBar.Add(findClose, 2, 0);
        this.Add(_findBar, 0, 0);

        _pageImage = new Image { Aspect = Aspect.Fill };
        _pageCanvas = new AbsoluteLayout { BackgroundColor = t.WebBg };
        AbsoluteLayout.SetLayoutFlags(_pageImage, AbsoluteLayoutFlags.None);
        _pageCanvas.Children.Add(_pageImage);

        var pagePointer = new PointerGestureRecognizer();
        pagePointer.PointerMoved += OnPagePointerMoved;
        pagePointer.PointerExited += OnPagePointerExited;
        _pageImage.GestureRecognizers.Add(pagePointer);

        var pageTap = new TapGestureRecognizer();
        pageTap.Tapped += OnPageTapped;
        _pageImage.GestureRecognizers.Add(pageTap);

        var pagePan = new PanGestureRecognizer();
        pagePan.PanUpdated += OnPagePanUpdated;
        _pageImage.GestureRecognizers.Add(pagePan);

        _pageScroll = new ScrollView
        {
            Orientation = ScrollOrientation.Both,
            BackgroundColor = t.WebBg,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
        };

        _placeholder = new Border
        {
            BackgroundColor = t.Panel,
            Stroke = Colors.Transparent,
            StrokeThickness = 0,
            Content = new Label
            {
                Text = "Type a URL above and press Go to render a page.",
                TextColor = t.Muted,
                FontFamily = tm.ChromeFont,
                FontSize = tm.Metrics.FsLg,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
            },
        };
        _pageScroll.Content = _placeholder;
        this.Add(_pageScroll, 0, 1);
    }

    /// <summary>Reveals the find bar and focuses its entry.</summary>
    public void FocusFind()
    {
        _findBar.IsVisible = true;
        _findEntry.Focus();
        _findEntry.CursorPosition = 0;
        _findEntry.SelectionLength = _findEntry.Text?.Length ?? 0;
    }

    /// <summary>Advances to the next find match (also reachable via the Find Next menu).</summary>
    public void FindNextFromMenu() => FindNext();

    public void ShowPage(LaidOutPage page)
    {
        using var span = _diag.Span("gui", "show_page");

        _currentPage?.Dispose();
        _currentPage = page;

        _hoverAnchor = null;
        _selectAnchor = null;
        ClearHighlights();

        // Render at physical resolution × UiScale: physical pixels for crispness,
        // UiScale for visual size (compensates for the iPad-idiom upscale Catalyst
        // used to apply implicitly). UIImage.Scale stays at the device density,
        // so the bitmap displays at (CSS px × UiScale) logical points.
        var density = (float)DeviceDisplay.Current.MainDisplayInfo.Density;
        if (!(density > 0.0f)) density = 1.0f;
        var uiScale = (float)ThemeManager.UiScale;

        ImageSource imageSource;
        using (_diag.Span("gui", "show_page.render"))
        using (var bitmap = _pageRenderer.Render(page.Root, density * uiScale))
        {
            using (_diag.Span("gui", "show_page.to_image_source"))
                imageSource = PageRenderer.ToImageSource(bitmap, density);
        }
        _pageImage.Source = imageSource;

        using (_diag.Span("gui", "show_page.layout_invalidate"))
        {
            var docWidth = Math.Max(1, page.Root.Frame.Width) * uiScale;
            var docHeight = Math.Max(1, page.Root.Frame.Height) * uiScale;
            AbsoluteLayout.SetLayoutBounds(_pageImage, new Rect(0, 0, docWidth, docHeight));
            _pageCanvas.WidthRequest = docWidth;
            _pageCanvas.HeightRequest = docHeight;
            _pageScroll.Content = _pageCanvas;
        }

        using (_diag.Span("gui", "show_page.hit_index"))
            _fragments = BoxHitTester.CollectFragments(page.Root);
        using (_diag.Span("gui", "show_page.find_index"))
            RebuildFindIndex();
    }

    // --- Interaction re-derived from the laid-out box tree ------------------

    private void OnPagePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_currentPage is null) return;
        var pos = e.GetPosition(_pageImage);
        if (pos is not { } p) return;

        // Pointer coords arrive in the _pageImage's logical-point space (CSS px
        // × UiScale); the box tree and hit tester are in CSS px.
        var cssX = p.X / ThemeManager.UiScale;
        var cssY = p.Y / ThemeManager.UiScale;
        _lastPointerDoc = (cssX, cssY);
        var hit = BoxHitTester.HitTest(_currentPage.Root, cssX, cssY);

#if MACCATALYST
        // Update the system cursor on every move. NSCursor.Set is idempotent
        // and the AppKit cursor stack remembers the last call until the next
        // move event, so re-affirming here is cheap and persistent.
        Platforms.MacCatalyst.PointerCursor.Set(BoxHitTester.ResolveCursor(hit));
#endif

        var anchor = hit.LinkAnchor;
        if (ReferenceEquals(anchor, _hoverAnchor)) return;

        _hoverAnchor = anchor;
        ApplyHoverHighlight();

        var href = anchor?.GetAttribute("href");
        if (!string.IsNullOrWhiteSpace(href))
            _onStatus($"↪ link to {href}", false);
    }

    private void OnPagePointerExited(object? sender, PointerEventArgs e)
    {
#if MACCATALYST
        // Hand the cursor back to whatever chrome / scroll-bar / window edge
        // the pointer is moving over so we don't leave the I-beam stuck.
        Platforms.MacCatalyst.PointerCursor.Reset();
#endif
        if (_hoverAnchor is null) return;
        _hoverAnchor = null;
        ApplyHoverHighlight();
    }

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
            AbsoluteLayout.SetLayoutBounds(overlay, ScaleToPoints(rect));
            _pageCanvas.Children.Add(overlay);
            _highlightOverlays.Add(overlay);
        }
    }

    // Fragments are in CSS px; the _pageCanvas places overlays in logical
    // points (CSS px × UiScale). Convert at the boundary.
    private static Rect ScaleToPoints(Rect r)
    {
        var s = ThemeManager.UiScale;
        return new Rect(r.X * s, r.Y * s, r.Width * s, r.Height * s);
    }

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

    private void OnPageTapped(object? sender, TappedEventArgs e)
    {
        if (_currentPage is null) return;
        var pos = e.GetPosition(_pageImage);
        if (pos is not { } p) return;

        // A primary click always collapses any existing selection. Right-clicks
        // don't reach this handler (TapGestureRecognizer is primary-only), so
        // the context-menu path keeps the selection intact.
        if (_selectionOverlays.Count > 0)
            ClearSelectionHighlights();

        var cssX = p.X / ThemeManager.UiScale;
        var cssY = p.Y / ThemeManager.UiScale;
        var hit = BoxHitTester.HitTest(_currentPage.Root, cssX, cssY);
        var href = hit.LinkAnchor?.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(href)) return;

        var resolved = ResolveLink(href, _currentPage.Url);
        if (resolved is null)
        {
            _onStatus($"Bad link: {href}", true);
            return;
        }
        _onLinkActivated(resolved);
    }

    private void OnPagePanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (_currentPage is null) return;

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _selectAnchor = _lastPointerDoc;
                _panOrigin = _lastPointerDoc;
                ClearSelectionHighlights();
                break;
            case GestureStatus.Running when _selectAnchor is { } anchor && _panOrigin is { } origin:
                // e.TotalX/Y is in logical points; origin is in CSS px.
                var cursor = (origin.X + (e.TotalX / ThemeManager.UiScale),
                              origin.Y + (e.TotalY / ThemeManager.UiScale));
                UpdateSelectionHighlight(anchor, cursor);
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                _selectAnchor = null;
                _panOrigin = null;
                break;
        }
    }

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
            AbsoluteLayout.SetLayoutBounds(overlay, ScaleToPoints(new Rect(f.X, f.Y, f.Width, f.Height)));
            _pageCanvas.Children.Add(overlay);
            _highlightOverlays.Add(overlay);
            _selectionOverlays.Add(overlay);
        }
        var text = string.Join(" ", _fragments.GetRange(startIdx, endIdx - startIdx + 1)
            .ConvertAll(f => f.Text));
        _onStatus($"Selected {text.Length} chars", false);
    }

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

        for (var i = 0; i < _findIndex.Count; i++)
        {
            var idx = (_findCursor + i) % _findIndex.Count;
            var (y, text) = _findIndex[idx];
            if (text.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                _findCursor = idx + 1;
                // Fragment Y is CSS px; scroll target lives in logical points.
                var targetY = Math.Max(0, (y * ThemeManager.UiScale) - (_pageScroll.Height / 3));
                await _pageScroll.ScrollToAsync(0, targetY, animated: true);
                _onStatus($"Find: '{query}' at y={y:F0}", false);
                return;
            }
        }
        _onStatus($"Find: '{query}' — no matches", true);
    }

    private static string? ResolveLink(string href, string? baseUrl)
    {
        href = href.Trim();
        if (href.Length == 0 || href.StartsWith("#", StringComparison.Ordinal))
            return null;

        // System.Uri is unusable here: it parses protocol-relative hrefs like
        // `//cdn.example.com/x` as UNC paths and hands back `file://cdn...`,
        // which sent the URL bar to file:/// on any page that used them. Route
        // through Tessera's WHATWG URL parser, which inherits the scheme from
        // the base for `//host` and resolves relative paths correctly.
        Tessera.Url.Url? parsedBase = null;
        if (!string.IsNullOrEmpty(baseUrl))
        {
            var baseResult = UrlParser.Parse(baseUrl);
            if (baseResult.IsOk) parsedBase = baseResult.Value;
        }

        var parsed = UrlParser.Parse(href, parsedBase);
        return parsed.IsOk ? parsed.Value.ToString() : null;
    }
}
