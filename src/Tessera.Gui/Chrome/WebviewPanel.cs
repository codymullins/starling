using Microsoft.Maui.Layouts;
using Tessera.Css.Properties;
using Tessera.Engine;
using Tessera.Gui.Theme;
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
    private readonly PageRenderer _pageRenderer = new();

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

    public WebviewPanel(ThemeManager tm, Action<string> onLinkActivated, Action<string, bool> onStatus)
    {
        _tm = tm;
        _onLinkActivated = onLinkActivated;
        _onStatus = onStatus;
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
        _currentPage?.Dispose();
        _currentPage = page;

        _hoverAnchor = null;
        _selectAnchor = null;
        ClearHighlights();

        using var bitmap = _pageRenderer.Render(page.Root);
        _pageImage.Source = PageRenderer.ToImageSource(bitmap);

        var docWidth = Math.Max(1, page.Root.Frame.Width);
        var docHeight = Math.Max(1, page.Root.Frame.Height);
        AbsoluteLayout.SetLayoutBounds(_pageImage, new Rect(0, 0, docWidth, docHeight));
        _pageCanvas.WidthRequest = docWidth;
        _pageCanvas.HeightRequest = docHeight;
        _pageScroll.Content = _pageCanvas;

        _fragments = BoxHitTester.CollectFragments(page.Root);
        RebuildFindIndex();
    }

    // --- Interaction re-derived from the laid-out box tree ------------------

    private void OnPagePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_currentPage is null) return;
        var pos = e.GetPosition(_pageImage);
        if (pos is not { } p) return;

        _lastPointerDoc = (p.X, p.Y);
        var hit = BoxHitTester.HitTest(_currentPage.Root, p.X, p.Y);
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
            AbsoluteLayout.SetLayoutBounds(overlay, rect);
            _pageCanvas.Children.Add(overlay);
            _highlightOverlays.Add(overlay);
        }
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

        var hit = BoxHitTester.HitTest(_currentPage.Root, p.X, p.Y);
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
                var targetY = Math.Max(0, y - _pageScroll.Height / 3);
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
        if (Uri.TryCreate(href, UriKind.Absolute, out var abs)) return abs.ToString();
        if (baseUrl is not null && Uri.TryCreate(new Uri(baseUrl), href, out var combined))
            return combined.ToString();
        return null;
    }
}
