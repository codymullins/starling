using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Starling.Gui.Chrome;
using Starling.Gui.Imaging;
using Starling.Gui.Theme;
using Starling.Common.Diagnostics;
using Starling.Common.Image;
using Starling.Css.Cascade;
using Starling.Css.Properties;
using Starling.Css.Selectors;
using Starling.Engine;
using Starling.Gui; // linked BoxHitTester.cs lives in this namespace
using AvColor = Avalonia.Media.Color;
using DomElement = Starling.Dom.Element;
using DomNode = Starling.Dom.Node;
using LayoutBox = Starling.Layout.Box.Box;

namespace Starling.Gui.Controls;

/// <summary>
/// The page surface — a ScrollViewer wrapping a Canvas that hosts the page
/// bitmap plus absolute-positioned overlays for hover highlight, drag-select,
/// and find-match flash. Interaction (hover, link activation, drag-select,
/// Cmd-F) is re-derived from the laid-out box tree via <see cref="BoxHitTester"/>,
/// mirroring the M2-era MAUI WebviewPanel's model.
/// </summary>
internal sealed class WebviewPanel : UserControl, IDisposable
{
    private readonly ThemeManager _tm;
    private readonly IDiagnostics _diag;
    private readonly Action<string> _onLinkActivated;
    private readonly Action<string, bool> _onStatus;
    private readonly PageRendererHost _renderer;

    private readonly ScrollViewer _scroll;
    private readonly Image _pageImage;
    private readonly Canvas _pageCanvas;
    private readonly TextBlock _placeholder;
    private readonly Border _placeholderHost;

    // Find bar — hidden until invoked via Cmd-F or the URL-bar find button.
    private readonly Border _findBar;
    private readonly TextBox _findEntry;

    private readonly List<Control> _selectionOverlays = new();
    private readonly List<Control> _findOverlays = new();
    private readonly List<(double Y, string Text)> _findIndex = new();
    private int _findCursor;

    private LaidOutPage? _currentPage;
    private List<BoxHitTester.PlacedFragment> _fragments = new();
    private double _currentScale = 1.0;
    private DomElement? _hoverAnchor;
    private Dictionary<DomElement, ComputedStyle>? _hoverOverrides;

    private (double X, double Y)? _selectAnchor;
    private bool _selecting;
    private string _selectionText = string.Empty;

    public WebviewPanel(ThemeManager tm, IDiagnostics diag, Action<string> onLinkActivated, Action<string, bool> onStatus)
    {
        _tm = tm;
        _diag = diag;
        _renderer = new PageRendererHost(diag);
        _onLinkActivated = onLinkActivated;
        _onStatus = onStatus;

        _pageImage = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
            IsVisible = false,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(_pageImage, 0);
        Canvas.SetTop(_pageImage, 0);

        _placeholder = new TextBlock
        {
            Text = "Type a URL above and press Enter to render a page.",
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        _placeholderHost = new Border
        {
            Child = _placeholder,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch,
            Padding = new Thickness(24),
        };

        _pageCanvas = new Canvas();
        _pageCanvas.Children.Add(_pageImage);
        _pageCanvas.PointerMoved += OnPointerMoved;
        _pageCanvas.PointerExited += OnPointerExited;
        _pageCanvas.PointerPressed += OnPointerPressed;
        _pageCanvas.PointerReleased += OnPointerReleased;
        _pageCanvas.PointerCaptureLost += (_, _) => EndSelectionDrag(cancel: true);

        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _placeholderHost,
        };

        _findEntry = new TextBox
        {
            PlaceholderText = "Find in page",
            FontSize = _tm.Metrics.FsSm,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
        };
        _findEntry.KeyDown += OnFindEntryKeyDown;
        _findEntry.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) { _findCursor = 0; FindNext(); }
        };

        var findNext = new IconButton(_tm, Icons.Enter, "Find next");
        findNext.Clicked += (_, _) => FindNext();
        var findClose = new IconButton(_tm, Icons.Close, "Close find");
        findClose.Clicked += (_, _) => HideFindBar();

        var findGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 6,
            Margin = new Thickness(12, 6),
        };
        findGrid.Children.Add(_findEntry); Grid.SetColumn(_findEntry, 0);
        findGrid.Children.Add(findNext); Grid.SetColumn(findNext, 1);
        findGrid.Children.Add(findClose); Grid.SetColumn(findClose, 2);

        _findBar = new Border
        {
            Child = findGrid,
            BorderThickness = new Thickness(0, 0, 0, 1),
            IsVisible = false,
        };

        var rootGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        rootGrid.Children.Add(_findBar); Grid.SetRow(_findBar, 0);
        rootGrid.Children.Add(_scroll); Grid.SetRow(_scroll, 1);

        Content = rootGrid;
        ApplyTheme();
        _tm.Changed += (_, _) => ApplyTheme();
    }

    private void ApplyTheme()
    {
        var t = _tm.Tokens;
        _scroll.Background = new SolidColorBrush(t.WebBg);
        _pageCanvas.Background = new SolidColorBrush(t.WebBg);
        _placeholderHost.Background = new SolidColorBrush(t.WebBg);
        _placeholder.Foreground = new SolidColorBrush(t.Muted);
        _placeholder.FontSize = _tm.Metrics.FsLg;
        _findBar.Background = new SolidColorBrush(t.Panel);
        _findBar.BorderBrush = new SolidColorBrush(t.Border);
        _findEntry.Background = new SolidColorBrush(t.Surface);
        _findEntry.Foreground = new SolidColorBrush(t.Text);
    }

    /// <summary>
    /// Renders <paramref name="page"/> through the paint pipeline and shows
    /// the resulting bitmap. The image's pixel dimensions equal the document size;
    /// the ScrollViewer scrolls anything taller than the viewport.
    /// </summary>
    public void ShowPage(LaidOutPage page)
    {
        _currentPage?.Dispose();
        _currentPage = page;

        // Reset interaction state.
        ClearSelection();
        ClearFindHighlight();
        _hoverAnchor = null;
        _hoverOverrides = null;

        _currentScale = GetRenderScale();
        RenderPageBitmap();

        _pageImage.Width = page.Root.Frame.Width;
        _pageImage.Height = page.Root.Frame.Height;
        _pageImage.IsVisible = true;

        _pageCanvas.Width = page.Root.Frame.Width;
        _pageCanvas.Height = page.Root.Frame.Height;
        _scroll.Content = _pageCanvas;

        using (_diag.Span("gui", "show_page.hit_index"))
            _fragments = BoxHitTester.CollectFragments(page.Root);
        RebuildFindIndex();
    }

    private double GetRenderScale()
    {
        var tl = TopLevel.GetTopLevel(this);
        return tl?.RenderScaling ?? 1.0;
    }

    // ---- Pointer event handlers ----------------------------------------

    private (double X, double Y)? PointerToDocSpace(PointerEventArgs e)
    {
        if (_currentPage is null) return null;
        var p = e.GetPosition(_pageCanvas);
        if (p.X < 0 || p.Y < 0) return null;
        if (p.X >= _pageCanvas.Bounds.Width || p.Y >= _pageCanvas.Bounds.Height) return null;
        return (p.X, p.Y);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_currentPage is null) return;
        var doc = PointerToDocSpace(e);
        if (doc is null) { ResetCursor(); return; }

        if (_selecting && _selectAnchor is { } anchor)
        {
            UpdateSelection(anchor, doc.Value);
            return;
        }

        var hit = BoxHitTester.HitTest(_currentPage.Root, doc.Value.X, doc.Value.Y);
        ApplyCursor(hit);

        var anchorEl = hit.LinkAnchor;
        if (!ReferenceEquals(anchorEl, _hoverAnchor))
        {
            _hoverAnchor = anchorEl;
            ApplyHoverState();
        }

        if (anchorEl is not null)
        {
            var href = anchorEl.GetAttribute("href");
            if (!string.IsNullOrEmpty(href))
                _onStatus(href!, false);
        }
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        ResetCursor();
        if (_hoverAnchor is not null)
        {
            _hoverAnchor = null;
            ApplyHoverState();
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_currentPage is null) return;
        if (!e.GetCurrentPoint(_pageCanvas).Properties.IsLeftButtonPressed) return;

        var doc = PointerToDocSpace(e);
        if (doc is null) return;

        // Always collapse any prior selection on primary press; the new gesture
        // is either a single click (link / blank) or the start of a fresh drag.
        ClearSelection();

        _selectAnchor = doc.Value;
        _selecting = true;
        e.Pointer.Capture(_pageCanvas);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_currentPage is null) { EndSelectionDrag(cancel: true); return; }
        if (e.InitialPressMouseButton != MouseButton.Left) return;

        var wasSelecting = _selecting && _selectAnchor is not null;
        var dragged = wasSelecting && _selectionOverlays.Count > 0;

        EndSelectionDrag(cancel: false);

        if (dragged) return; // drag-select: don't navigate on release

        var doc = PointerToDocSpace(e);
        if (doc is null) return;

        var hit = BoxHitTester.HitTest(_currentPage.Root, doc.Value.X, doc.Value.Y);
        if (hit.LinkAnchor is null) return;

        var href = hit.LinkAnchor.GetAttribute("href");
        if (string.IsNullOrEmpty(href)) return;

        var resolved = ResolveLink(href!);
        if (!string.IsNullOrEmpty(resolved))
        {
            e.Handled = true;
            _onLinkActivated(resolved);
        }
    }

    private void EndSelectionDrag(bool cancel)
    {
        _selecting = false;
        if (cancel) _selectAnchor = null;
    }

    // ---- Cursor --------------------------------------------------------

    private void ApplyCursor(BoxHitTester.HitResult hit)
    {
        var keyword = BoxHitTester.ResolveCursor(hit);
        Cursor = MapCursor(keyword);
    }

    private void ResetCursor() => Cursor = new Cursor(StandardCursorType.Arrow);

    private static Cursor MapCursor(string keyword) => keyword switch
    {
        "pointer" => new Cursor(StandardCursorType.Hand),
        "text" => new Cursor(StandardCursorType.Ibeam),
        "wait" or "progress" => new Cursor(StandardCursorType.Wait),
        "help" => new Cursor(StandardCursorType.Help),
        "crosshair" => new Cursor(StandardCursorType.Cross),
        "move" => new Cursor(StandardCursorType.SizeAll),
        "not-allowed" or "no-drop" => new Cursor(StandardCursorType.No),
        "ew-resize" or "col-resize" => new Cursor(StandardCursorType.SizeWestEast),
        "ns-resize" or "row-resize" => new Cursor(StandardCursorType.SizeNorthSouth),
        "grab" => new Cursor(StandardCursorType.Hand),
        "grabbing" => new Cursor(StandardCursorType.DragMove),
        _ => new Cursor(StandardCursorType.Arrow),
    };

    // ---- Hover re-render ----------------------------------------------

    /// <summary>
    /// Re-cascades the hovered anchor + its DOM descendants with a
    /// <c>:hover</c>-active context and re-rasterizes the page through the
    /// paint pipeline with those styles substituted in via the
    /// DisplayListBuilder's per-box override hook. Layout is reused —
    /// hover changes that affect geometry (rare in real CSS) won't reflow.
    /// </summary>
    private void ApplyHoverState()
    {
        if (_currentPage is null) return;

        var newOverrides = _hoverAnchor is null
            ? null
            : BuildHoverOverrides(_hoverAnchor, _currentPage.Style);

        // Skip re-render when nothing the cascade emitted actually changed —
        // pages without :hover rules then pay only the cascade-probe cost on
        // pointer move, not the paint cost.
        if (!OverridesDifferFromBaseline(newOverrides))
        {
            _hoverOverrides = null;
            return;
        }

        _hoverOverrides = newOverrides;
        RenderPageBitmap();
    }

    private static Dictionary<DomElement, ComputedStyle> BuildHoverOverrides(DomElement hovered, StyleEngine style)
    {
        var ctx = new SelectorMatchContext { HoveredElement = hovered };
        var result = new Dictionary<DomElement, ComputedStyle>();
        Recurse(hovered);
        return result;

        void Recurse(DomElement el)
        {
            result[el] = style.Compute(el, ctx);
            for (var child = el.FirstChild; child is not null; child = child.NextSibling)
                if (child is DomElement c) Recurse(c);
        }
    }

    /// <summary>
    /// Returns true if any computed style in <paramref name="overrides"/>
    /// differs from the element's layout-time style for the properties the
    /// paint pipeline actually emits. Comparing the full ComputedStyle would
    /// trigger re-renders on identity changes (CssValue boxing), so we sample
    /// the handful of paint-affecting properties.
    /// </summary>
    private bool OverridesDifferFromBaseline(Dictionary<DomElement, ComputedStyle>? overrides)
    {
        if (_currentPage is null || overrides is null || overrides.Count == 0) return false;
        var baseline = _currentPage.Style;
        foreach (var (element, hovered) in overrides)
        {
            var original = baseline.Compute(element);
            if (!SamePaintProperties(original, hovered)) return true;
        }
        return false;
    }

    private static bool SamePaintProperties(ComputedStyle a, ComputedStyle b)
    {
        if (a.GetColor(PropertyId.Color) != b.GetColor(PropertyId.Color)) return false;
        if (a.GetColor(PropertyId.BackgroundColor) != b.GetColor(PropertyId.BackgroundColor)) return false;
        if (a.GetColor(PropertyId.BorderTopColor) != b.GetColor(PropertyId.BorderTopColor)) return false;
        if (a.GetColor(PropertyId.BorderRightColor) != b.GetColor(PropertyId.BorderRightColor)) return false;
        if (a.GetColor(PropertyId.BorderBottomColor) != b.GetColor(PropertyId.BorderBottomColor)) return false;
        if (a.GetColor(PropertyId.BorderLeftColor) != b.GetColor(PropertyId.BorderLeftColor)) return false;
        // Reference-compare the decoration value — different cascade outputs
        // for the same `underline` keyword still produce reference-equal
        // CssKeyword.Underline singletons in the engine.
        if (!ReferenceEquals(a.Get(PropertyId.TextDecoration), b.Get(PropertyId.TextDecoration))) return false;
        return true;
    }

    /// <summary>
    /// Renders <c>_currentPage.Root</c> through the paint pipeline, threading
    /// the current hover overrides into <see cref="DisplayListBuilder"/> so
    /// <c>a:hover</c> rules repaint without re-layout. Called once on each
    /// page load and on every hover transition that changes paint-affecting
    /// styles.
    /// </summary>
    private void RenderPageBitmap()
    {
        if (_currentPage is null) return;

        var overrides = _hoverOverrides;
        Func<LayoutBox, ComputedStyle?>? styleOverride = overrides is null
            ? null
            : box => ResolveOverride(box, overrides);

        RenderedBitmap rendered;
        using (_diag.Span("gui", "render"))
            rendered = _renderer.Render(_currentPage.Root, (float)_currentScale, styleOverride, _currentPage.ImageResolver);
        WriteableBitmap bmp;
        using (rendered)
            bmp = BitmapBridge.ToWriteableBitmap(rendered, _currentScale);

        (_pageImage.Source as IDisposable)?.Dispose();
        _pageImage.Source = bmp;
    }

    /// <summary>
    /// Maps a paint-time box back to its hover override. Boxes with their own
    /// <see cref="LayoutBox.Element"/> match directly; anonymous text boxes
    /// inherit by walking up the box-parent chain to find the nearest box
    /// whose element has an override (text inherits color from its element
    /// ancestor's cascade output).
    /// </summary>
    private static ComputedStyle? ResolveOverride(LayoutBox box, Dictionary<DomElement, ComputedStyle> overrides)
    {
        if (box.Element is { } el && overrides.TryGetValue(el, out var direct))
            return direct;
        // TextBox / anonymous block: walk up box parents looking for a
        // box whose element appears in the override map.
        for (var p = box.Parent; p is not null; p = p.Parent)
        {
            if (p.Element is { } pel && overrides.TryGetValue(pel, out var inherited))
                return inherited;
        }
        return null;
    }

    // ---- Selection -----------------------------------------------------

    private void UpdateSelection((double X, double Y) anchor, (double X, double Y) cursor)
    {
        ClearSelectionOverlays();
        if (_fragments.Count == 0) return;

        var anchorCaret = SelectionModel.CaretFromPoint(_fragments, anchor.X, anchor.Y);
        var cursorCaret = SelectionModel.CaretFromPoint(_fragments, cursor.X, cursor.Y);
        var range = SelectionModel.Order(anchorCaret, cursorCaret);
        if (range.IsEmpty)
        {
            _selectionText = string.Empty;
            return;
        }

        var brush = new SolidColorBrush(AvColor.FromArgb(96, 80, 140, 255));
        foreach (var r in SelectionModel.RectsFor(_fragments, range))
        {
            var overlay = MakeOverlay(brush, r.X, r.Y, r.Width, r.Height);
            _pageCanvas.Children.Add(overlay);
            _selectionOverlays.Add(overlay);
        }

        _selectionText = SelectionModel.TextFor(_fragments, range);
        _onStatus($"Selected {_selectionText.Length} chars — ⌘C to copy", false);
    }

    private void ClearSelection()
    {
        ClearSelectionOverlays();
        _selectionText = string.Empty;
        _selectAnchor = null;
    }

    private void ClearSelectionOverlays()
    {
        foreach (var o in _selectionOverlays)
            _pageCanvas.Children.Remove(o);
        _selectionOverlays.Clear();
    }

    /// <summary>Copies the current text selection to the clipboard.</summary>
    public async Task CopySelectionAsync()
    {
        if (string.IsNullOrEmpty(_selectionText)) return;
        var tl = TopLevel.GetTopLevel(this);
        var clip = tl?.Clipboard;
        if (clip is null) return;
        await clip.SetTextAsync(_selectionText);
        _onStatus($"Copied {_selectionText.Length} chars to clipboard", false);
    }

    // ---- Find in page --------------------------------------------------

    /// <summary>Reveals the find bar and focuses its entry.</summary>
    public void FocusFind()
    {
        _findBar.IsVisible = true;
        _findEntry.Focus();
        _findEntry.SelectAll();
    }

    /// <summary>Hides the find bar and clears any match-highlight overlay.</summary>
    public void HideFindBar()
    {
        _findBar.IsVisible = false;
        ClearFindHighlight();
    }

    private void OnFindEntryKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                FindNext();
                break;
            case Key.Escape:
                e.Handled = true;
                HideFindBar();
                break;
        }
    }

    private void RebuildFindIndex()
    {
        _findIndex.Clear();
        _findCursor = 0;
        foreach (var f in _fragments)
            _findIndex.Add((f.Y, f.Text));
    }

    private void FindNext()
    {
        ClearFindHighlight();
        var query = (_findEntry.Text ?? string.Empty).Trim();
        if (query.Length == 0 || _findIndex.Count == 0) return;

        for (var i = 0; i < _findIndex.Count; i++)
        {
            var idx = (_findCursor + i) % _findIndex.Count;
            var (_, text) = _findIndex[idx];
            if (text.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                _findCursor = idx + 1;
                FlashFindMatch(idx);
                _onStatus($"Find: '{query}' — match {idx + 1}/{_findIndex.Count}", false);
                return;
            }
        }
        _onStatus($"Find: '{query}' — no matches", true);
    }

    private void FlashFindMatch(int idx)
    {
        var f = _fragments[idx];
        var brush = new SolidColorBrush(AvColor.FromArgb(140, 255, 220, 70));
        var overlay = MakeOverlay(brush, f.X, f.Y, f.Width, f.Height);
        _pageCanvas.Children.Add(overlay);
        _findOverlays.Add(overlay);

        var viewport = _scroll.Viewport.Height;
        var targetY = Math.Max(0, f.Y - (viewport / 3));
        _scroll.Offset = new Vector(_scroll.Offset.X, targetY);

        // Auto-fade after a short delay so repeated find-next stays readable.
        DispatcherTimer.RunOnce(() =>
        {
            _pageCanvas.Children.Remove(overlay);
            _findOverlays.Remove(overlay);
        }, TimeSpan.FromMilliseconds(1200));
    }

    private void ClearFindHighlight()
    {
        foreach (var o in _findOverlays)
            _pageCanvas.Children.Remove(o);
        _findOverlays.Clear();
    }

    // ---- Helpers -------------------------------------------------------

    private static Border MakeOverlay(IBrush brush, double x, double y, double w, double h)
    {
        var b = new Border
        {
            Background = brush,
            Width = w,
            Height = h,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(b, x);
        Canvas.SetTop(b, y);
        return b;
    }

    private string? ResolveLink(string href)
        => _currentPage is null ? null : LinkResolver.Resolve(href, _currentPage.Url);

    public void Dispose()
    {
        _renderer.Dispose();
        _currentPage?.Dispose();
        (_pageImage.Source as IDisposable)?.Dispose();
    }
}
