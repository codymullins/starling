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
using System.Diagnostics;
using Starling.Common.Diagnostics;
using Starling.Common.Image;
using Starling.Css.Cascade;
using Starling.Css.Properties;
using Starling.Css.Selectors;
using Starling.Dom.Events;
using Starling.Engine;
using AvColor = Avalonia.Media.Color;
using DomElement = Starling.Dom.Element;
using DomNode = Starling.Dom.Node;
using EngineSize = SixLabors.ImageSharp.Size;
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

    // Given the current page and a new viewport size, reflows the page (reusing
    // its document/resources, no network) and returns the successor — or null
    // to skip (e.g. a navigation is in flight). Supplied by the host so the
    // panel can react to its own resize without owning navigation policy.
    private readonly Func<LaidOutPage, EngineSize, LaidOutPage?>? _relayout;
    // Coalesces the burst of viewport changes during a drag-resize into a
    // single re-layout once the size settles.
    private readonly DispatcherTimer _relayoutTimer;

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

    // Text-input editing state. _focusedInput is the <input>/<textarea> element
    // currently receiving keystrokes; _caretIndex is the insertion point within
    // its value; _caretOverlay is the blinking caret drawn over the page bitmap.
    private DomElement? _focusedInput;
    private int _caretIndex;
    private Border? _caretOverlay;
    private bool _caretOn;
    private string _valueAtFocus = string.Empty;
    private readonly DispatcherTimer _caretBlinkTimer;

    // The element a synthetic mouse-move (MoveTo) last hovered, so the next move
    // can fire mouseout on it / mouseover on the new target. Only the programmatic
    // MoveTo path tracks this — real pointer moves drive CSS :hover via _hoverAnchor.
    private DomElement? _mouseTarget;

    // Live-page event loop: while the current page carries a live JS context
    // (Starling.Engine.PageScripting), this timer pumps its microtasks / timers /
    // rAF / fetch completions against wall-clock time and re-renders any DOM the
    // page mutates. _boundScripting tracks which context the stopwatch's clock
    // belongs to so a relayout (same context) doesn't reset it.
    private readonly DispatcherTimer _liveTimer;
    private readonly Stopwatch _liveStopwatch = new();
    private PageScripting? _boundScripting;

    public WebviewPanel(
        ThemeManager tm,
        IDiagnostics diag,
        Action<string> onLinkActivated,
        Action<string, bool> onStatus,
        Func<LaidOutPage, EngineSize, LaidOutPage?>? relayout = null)
    {
        _tm = tm;
        _diag = diag;
        _renderer = new PageRendererHost(diag);
        _onLinkActivated = onLinkActivated;
        _onStatus = onStatus;
        _relayout = relayout;
        _relayoutTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _relayoutTimer.Tick += (_, _) => RelayoutToViewport();

        _pageImage = new Image
        {
            // The backend renders the visible viewport region at device px and
            // BitmapBridge leaves the WriteableBitmap at 96 DPI, so its logical
            // size equals its device-pixel size. The image is given the CSS
            // (DIP) viewport Width/Height and Stretch.Uniform downscales the
            // device-pixel bitmap to fit — a crisp 1:1 device mapping on Retina.
            // (Stretch.None would draw the device-pixel bitmap at its full DIP
            // size, i.e. scale× too big on any non-1.0 RenderScaling display.)
            // It is repositioned to the scroll offset on every scroll.
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

        _pageCanvas = new Canvas
        {
            // Focusable so the page surface can receive keyboard/text input once
            // an editable field is focused (links/selection only need pointers).
            Focusable = true,
        };
        _pageCanvas.Children.Add(_pageImage);
        _pageCanvas.PointerMoved += OnPointerMoved;
        _pageCanvas.PointerExited += OnPointerExited;
        _pageCanvas.PointerPressed += OnPointerPressed;
        _pageCanvas.PointerReleased += OnPointerReleased;
        _pageCanvas.PointerCaptureLost += (_, _) => EndSelectionDrag(cancel: true);
        _pageCanvas.KeyDown += OnPageKeyDown;
        _pageCanvas.KeyUp += OnPageKeyUp;
        _pageCanvas.TextInput += OnPageTextInput;

        // ~60 Hz live-page pump (started only while a page has a JS context).
        _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _liveTimer.Tick += (_, _) => LiveTick();

        // Caret blink — toggles the overlay's visibility on a fixed cadence
        // while a field is focused; stopped (and the overlay removed) on blur.
        _caretBlinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        _caretBlinkTimer.Tick += (_, _) =>
        {
            if (_caretOverlay is null) return;
            _caretOn = !_caretOn;
            _caretOverlay.IsVisible = _caretOn;
        };

        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _placeholderHost,
        };
        // Re-render the visible viewport region whenever the user scrolls or
        // the viewport size changes (M12 viewport-clipped paint: the page is no
        // longer rasterized whole, only the on-screen rect each frame). A
        // non-zero ViewportDelta means the visible area itself resized (window
        // resize, sidebar/DevTools toggle) — schedule a debounced re-layout so
        // the page reflows to the new width instead of staying at its old one.
        _scroll.ScrollChanged += OnScrollChanged;

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
    /// the resulting bitmap. The page-sized <see cref="_pageCanvas"/> provides
    /// the scroll extent (virtual content), while <see cref="_pageImage"/> is
    /// sized to the visible viewport and re-rendered for the current scroll
    /// offset — only the on-screen region is rasterized each frame (M12
    /// viewport-clipped paint).
    /// </summary>
    public void ShowPage(LaidOutPage page, bool preserveScroll = false)
    {
        // Keep the scroll position across a reflow (resize re-layout) so the
        // user doesn't jump to the top; a fresh navigation resets to the top.
        var prevOffset = _scroll.Offset;

        // A relayout of the same document (resize / edit) keeps the focused
        // field; a fresh navigation produces a new document whose
        // FocusedElement is null, so the caret is dropped below.
        var keepFocus = _focusedInput is not null
            && ReferenceEquals(page.Document.FocusedElement, _focusedInput);

        // Pause the live-page pump across the swap; rebound at the end against the
        // incoming page's JS context. (All on the UI thread, so the timer can't
        // tick mid-swap, but stopping keeps _boundScripting from briefly naming a
        // page we're about to dispose.)
        _liveTimer.Stop();

        _currentPage?.Dispose();
        _currentPage = page;

        // New laid-out page = new display-list version; drop any cached pixels
        // from the previous page so the first render is a clean full repaint.
        _renderer.InvalidateCache();

        // Reset interaction state.
        ClearSelection();
        ClearFindHighlight();
        _hoverAnchor = null;
        _hoverOverrides = null;

        _currentScale = GetRenderScale();

        // Page-sized canvas = scroll extent. The bitmap overlaid on it is only
        // viewport-sized; ShowPage resets to the top, then renders that region.
        _pageImage.IsVisible = true;
        _pageCanvas.Width = page.Root.Frame.Width;
        _pageCanvas.Height = page.Root.Frame.Height;
        _scroll.Content = _pageCanvas;

        if (preserveScroll)
        {
            var maxX = Math.Max(0, _pageCanvas.Width - _scroll.Viewport.Width);
            var maxY = Math.Max(0, _pageCanvas.Height - _scroll.Viewport.Height);
            _scroll.Offset = new Vector(
                Math.Clamp(prevOffset.X, 0, maxX),
                Math.Clamp(prevOffset.Y, 0, maxY));
        }
        else
        {
            _scroll.Offset = new Vector(0, 0);
        }
        RenderViewportRegion();

        using (_diag.Span("gui", "show_page.hit_index"))
            _fragments = BoxHitTester.CollectFragments(page.Root);
        RebuildFindIndex();

        // Re-establish (or drop) the text caret against the new box tree. The
        // caret overlay belongs to the prior layout's positions, so it is always
        // rebuilt here.
        CaretLog($"ShowPage: keepFocus={keepFocus} idx={_caretIndex} foc={_focusedInput?.LocalName} " +
            $"docFoc={page.Document.FocusedElement?.LocalName} vp={page.Viewport.Width}x{page.Viewport.Height} preserveScroll={preserveScroll}");
        RemoveCaretOverlay();
        if (keepFocus)
        {
            _pageCanvas.Focus();
            RenderCaret();
        }
        else
        {
            _focusedInput = null;
            _caretIndex = 0;
            _caretBlinkTimer.Stop();
        }

        BindLiveScripting();
    }

    /// <summary>
    /// The currently-visible page-coordinate rectangle: the scroll offset plus
    /// the ScrollViewer viewport size. Falls back to the page width / a nominal
    /// height before the control has been measured.
    /// </summary>
    private Starling.Layout.Rect CurrentViewportRect()
    {
        var pageW = _currentPage?.Root.Frame.Width ?? 0;
        var pageH = _currentPage?.Root.Frame.Height ?? 0;

        var vpW = _scroll.Viewport.Width > 0 ? _scroll.Viewport.Width : Math.Max(1, pageW);
        var vpH = _scroll.Viewport.Height > 0 ? _scroll.Viewport.Height : Math.Max(1, Math.Min(pageH, 1024));

        var offX = _scroll.Offset.X;
        var offY = _scroll.Offset.Y;

        // Clamp so we never request a region past the page extent.
        vpW = Math.Max(1, Math.Min(vpW, Math.Max(1, pageW - offX)));
        vpH = Math.Max(1, Math.Min(vpH, Math.Max(1, pageH - offY)));

        return new Starling.Layout.Rect(offX, offY, vpW, vpH);
    }

    /// <summary>
    /// Renders the current viewport region and positions the bitmap at the
    /// scroll offset so it stays aligned with the page-coordinate overlays.
    /// </summary>
    private void RenderViewportRegion()
    {
        if (_currentPage is null) return;
        var rect = CurrentViewportRect();
        RenderPageBitmap(rect);

        _pageImage.Width = rect.Width;
        _pageImage.Height = rect.Height;
        Canvas.SetLeft(_pageImage, rect.X);
        Canvas.SetTop(_pageImage, rect.Y);
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        RenderViewportRegion();
        if (e.ViewportDelta.X != 0 || e.ViewportDelta.Y != 0)
            ScheduleRelayout();
    }

    /// <summary>
    /// The live CSS layout viewport — the ScrollViewer's visible area (which
    /// already excludes any scrollbar gutter). Falls back to the control bounds,
    /// then a nominal size, before the panel has been measured. This is the
    /// width/height pages are laid out against, so it tracks the window size.
    /// </summary>
    public EngineSize CurrentViewportSize()
    {
        var w = _scroll.Viewport.Width;
        var h = _scroll.Viewport.Height;
        if (w < 1 || h < 1)
        {
            w = Bounds.Width > 1 ? Bounds.Width : 1200;
            h = Bounds.Height > 1 ? Bounds.Height : 900;
        }
        return new EngineSize((int)Math.Round(w), (int)Math.Round(h));
    }

    private void ScheduleRelayout()
    {
        if (_relayout is null) return;
        // Restart the timer so a continuous drag-resize only reflows once it
        // pauses, not on every intermediate size.
        _relayoutTimer.Stop();
        _relayoutTimer.Start();
    }

    private void RelayoutToViewport()
    {
        _relayoutTimer.Stop();
        var page = _currentPage;
        if (page is null || _relayout is null) return;

        var size = CurrentViewportSize();
        // No-op if the layout viewport already matches what produced this page —
        // guards against scrollbar-toggle flicker and the post-load delta.
        if (size.Width == (int)Math.Round(page.Viewport.Width) &&
            size.Height == (int)Math.Round(page.Viewport.Height))
            return;

        CaretLog($"RelayoutToViewport: {(int)page.Viewport.Width}x{(int)page.Viewport.Height} -> {size.Width}x{size.Height}");
        var relaid = _relayout(page, size);
        if (relaid is not null)
            ShowPage(relaid, preserveScroll: true);
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

        UpdateHover(BoxHitTester.HitTest(_currentPage.Root, doc.Value.X, doc.Value.Y));
    }

    /// <summary>Applies the cursor and CSS <c>:hover</c> state for a hit, and
    /// surfaces a hovered link's href in the status bar. Shared by the real
    /// pointer-move handler and the synthetic <see cref="MoveTo"/> entry point.</summary>
    private void UpdateHover(BoxHitTester.HitResult hit)
    {
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

        if (PerformClick(doc.Value).Handled)
            e.Handled = true;
    }

    /// <summary>
    /// Runs the click decision tree at a document-space point, mirroring a real
    /// left-click release: a hit on an editable text field focuses it (caret at
    /// the clicked character); otherwise a DOM <c>click</c> is dispatched into the
    /// page (JS handlers, checkbox toggle, form submit) and, if the page consumed
    /// it, field focus is kept; otherwise the current field is blurred and a hit
    /// link is followed. <paramref name="Handled"/> reports whether anything
    /// consumed the click (so the pointer handler can mark the event handled);
    /// <paramref name="Detail"/> is a short human-readable outcome for tooling.
    /// </summary>
    private (bool Handled, string Detail) PerformClick((double X, double Y) doc)
    {
        var hit = BoxHitTester.HitTest(_currentPage!.Root, doc.X, doc.Y);

        if (FindFocusableInput(hit.Box) is { } field)
        {
            FocusInput(field, doc);
            return (true, $"focused <{field.LocalName}>");
        }

        if (DispatchClick(hit, doc))
            return (true, $"clicked <{DescribeHit(hit)}> (page handled)");

        // Not consumed by the page: a click elsewhere blurs the current field and
        // then follows a link if one was hit.
        BlurInput();

        if (hit.LinkAnchor is { } anchorEl)
        {
            var href = anchorEl.GetAttribute("href");
            if (!string.IsNullOrEmpty(href) && ResolveLink(href!) is { Length: > 0 } resolved)
            {
                _onLinkActivated(resolved);
                return (true, $"followed link to {resolved}");
            }
        }

        return (false, hit.Box is null ? "clicked empty area" : $"clicked <{DescribeHit(hit)}> (no action)");
    }

    private static string DescribeHit(BoxHitTester.HitResult hit)
        => FindClickTarget(hit.Box)?.LocalName ?? "unknown";

    private void EndSelectionDrag(bool cancel)
    {
        _selecting = false;
        if (cancel) _selectAnchor = null;
    }

    // ---- Text input editing -------------------------------------------
    //
    // Click-to-focus + type for <input>/<textarea>. The value lives on the DOM
    // element (Element.InputValue); each edit mutates it, re-lays-out the page
    // (so the synthesized label text refreshes), and repositions a blinking
    // caret drawn as a page overlay. document.FocusedElement is kept in sync so
    // :focus CSS and the JS activeElement/.value accessors agree. Firing DOM
    // input/keydown events into page scripts is a deliberate follow-up.

    private const double CaretWidth = 1.0;

    /// <summary>
    /// The editable <c>&lt;input&gt;</c>/<c>&lt;textarea&gt;</c> enclosing
    /// <paramref name="box"/> in the box/DOM tree, or null when the hit isn't a
    /// text-entry control. Non-text inputs (button, checkbox, …) are excluded.
    /// </summary>
    private static DomElement? FindFocusableInput(LayoutBox? box)
    {
        for (var b = box; b is not null; b = b.Parent)
        {
            if (b.Element is not DomElement el) continue;
            switch (el.LocalName)
            {
                case "textarea":
                    return el;
                case "input":
                    var type = (el.GetAttribute("type") ?? "text").Trim().ToLowerInvariant();
                    return type is "text" or "search" or "email" or "url" or "tel"
                            or "password" or "number" or ""
                        ? el : null;
            }
        }
        return null;
    }

    private string CurrentValue()
        => _focusedInput is null ? string.Empty
            : _focusedInput.InputValue ?? _focusedInput.GetAttribute("value") ?? string.Empty;

    /// <summary>
    /// Focuses <paramref name="input"/>: records document focus (so <c>:focus</c>
    /// and <c>activeElement</c> see it), re-lays-out so a focused empty field
    /// drops its placeholder, then places the caret at the clicked character.
    /// </summary>
    private void FocusInput(DomElement input, (double X, double Y) clickDoc)
    {
        if (_currentPage is null) return;

        _focusedInput = input;
        _currentPage.Document.FocusedElement = input;
        _valueAtFocus = CurrentValue();
        _caretIndex = CurrentValue().Length;
        _pageCanvas.Focus();

        // Notify page JS (HTML focus event order: focus, then focusin/bubbles).
        DispatchDom(input, new FocusEvent("focus"));
        DispatchDom(input, new FocusEvent("focusin", new EventInit(Bubbles: true)));

        RefreshFocusedLayout();
        _caretIndex = CaretIndexFromClick(clickDoc);
        RenderCaret();
    }

    /// <summary>Clears the current field focus (caret removed, placeholder
    /// restored), if any. A no-op when nothing is focused.</summary>
    private void BlurInput()
    {
        if (_focusedInput is null) return;
        var was = _focusedInput;
        var finalValue = was.InputValue ?? was.GetAttribute("value") ?? string.Empty;
        var changed = !string.Equals(finalValue, _valueAtFocus, StringComparison.Ordinal);
        _focusedInput = null;
        _caretBlinkTimer.Stop();
        RemoveCaretOverlay();
        if (_currentPage is not null && ReferenceEquals(_currentPage.Document.FocusedElement, was))
            _currentPage.Document.FocusedElement = null;

        // HTML order: a value edited since focus fires `change`, then blur/focusout.
        if (changed) DispatchDom(was, new Event("change", new EventInit(Bubbles: true)));
        DispatchDom(was, new FocusEvent("blur"));
        DispatchDom(was, new FocusEvent("focusout", new EventInit(Bubbles: true)));

        RefreshFocusedLayout(); // placeholder returns on the now-blurred field
    }

    private void OnPageTextInput(object? sender, TextInputEventArgs e)
    {
        if (_focusedInput is null || string.IsNullOrEmpty(e.Text)) return;
        // Drop control characters — Enter/Tab arrive via OnPageKeyDown, and some
        // platforms also surface them here.
        var text = new string(e.Text.Where(c => !char.IsControl(c)).ToArray());
        if (text.Length == 0) return;

        e.Handled = true;
        var val = CurrentValue();
        var idx = Math.Clamp(_caretIndex, 0, val.Length);
        CaretLog($"TextInput: '{text}' at idx={idx} (caretIdx={_caretIndex}) val='{val}'");
        CommitValue(val.Insert(idx, text), idx + text.Length);
    }

    private void OnPageKeyUp(object? sender, KeyEventArgs e)
    {
        if (_focusedInput is null) return;
        DispatchDom(_focusedInput, MakeKeyboardEvent("keyup", e));
    }

    private void OnPageKeyDown(object? sender, KeyEventArgs e)
    {
        if (_focusedInput is null) return;
        DispatchDom(_focusedInput, MakeKeyboardEvent("keydown", e));
        var val = CurrentValue();
        var idx = Math.Clamp(_caretIndex, 0, val.Length);
        switch (e.Key)
        {
            case Key.Back:
                if (idx > 0) CommitValue(val.Remove(idx - 1, 1), idx - 1);
                e.Handled = true;
                break;
            case Key.Delete:
                if (idx < val.Length) CommitValue(val.Remove(idx, 1), idx);
                e.Handled = true;
                break;
            case Key.Left: MoveCaret(idx - 1); e.Handled = true; break;
            case Key.Right: MoveCaret(idx + 1); e.Handled = true; break;
            case Key.Home: MoveCaret(0); e.Handled = true; break;
            case Key.End: MoveCaret(val.Length); e.Handled = true; break;
            case Key.Escape: BlurInput(); e.Handled = true; break;
            case Key.Enter:
                // Implicit form submission: Enter in a text field submits its
                // owning form (the keydown was already dispatched above).
                e.Handled = true;
                if (_focusedInput.LocalName == "input" && NearestForm(_focusedInput) is { } form
                    && DispatchDom(form, new Event("submit", new EventInit(Bubbles: true, Cancelable: true))))
                    RefreshLiveLayout();
                break;
        }
    }

    /// <summary>Writes the field's new value + caret position, then re-lays-out
    /// so the rendered label text matches.</summary>
    private void CommitValue(string next, int newCaret)
    {
        if (_focusedInput is null) return;
        _focusedInput.InputValue = next;
        _caretIndex = Math.Clamp(newCaret, 0, next.Length);
        // Fire the DOM `input` event so search-as-you-type / form handlers run;
        // RefreshFocusedLayout then reflects both the new value text and any DOM
        // the handler mutated synchronously.
        DispatchDom(_focusedInput, new InputEvent("input", new EventInit(Bubbles: true)) { InputType = "insertText" });
        RefreshFocusedLayout();
    }

    private void MoveCaret(int index)
    {
        _caretIndex = Math.Clamp(index, 0, CurrentValue().Length);
        RenderCaret(); // value unchanged — no re-layout needed
    }

    /// <summary>Re-lays-out the current page (reusing its document/resources) so
    /// the focused field's text and placeholder reflect the latest state, then
    /// reshows it preserving scroll. Falls back to repositioning the caret in
    /// place when no relayout hook is wired.</summary>
    private void RefreshFocusedLayout()
    {
        if (_currentPage is null || _relayout is null) { RenderCaret(); return; }
        var relaid = _relayout(_currentPage, CurrentViewportSize());
        if (relaid is not null)
            ShowPage(relaid, preserveScroll: true); // re-renders caret if still focused
        else
            RenderCaret();
    }

    // TEMP caret diagnostics — gated behind STARLING_CARET_DEBUG=1.
    private static readonly bool _caretDebug =
        Environment.GetEnvironmentVariable("STARLING_CARET_DEBUG") == "1";
    private static void CaretLog(string msg)
    {
        if (!_caretDebug) return;
        try { System.IO.File.AppendAllText("/tmp/starling-caret.log", $"{DateTime.Now:HH:mm:ss.fff} {msg}\n"); }
        catch { }
    }

    /// <summary>(Re)draws the caret overlay at the current insertion point and
    /// (re)starts the blink. Removes the caret when nothing is focused.</summary>
    private void RenderCaret()
    {
        RemoveCaretOverlay();
        if (_focusedInput is null || _currentPage is null) { CaretLog("RenderCaret: no focus/page"); return; }
        if (ComputeCaretRect(_currentPage.Root, _focusedInput, _caretIndex) is not { } c)
        {
            CaretLog($"RenderCaret: ComputeCaretRect NULL idx={_caretIndex} val='{CurrentValue()}'");
            return;
        }

        CaretLog($"RenderCaret: idx={_caretIndex} X={c.X:F1} val='{CurrentValue()}'");
        _caretOverlay = MakeOverlay(new SolidColorBrush(c.Color), c.X, c.Y, CaretWidth, c.H);
        _pageCanvas.Children.Add(_caretOverlay);
        _caretOn = true;
        _caretBlinkTimer.Stop();
        _caretBlinkTimer.Start(); // solid immediately after a change, then blinks
    }

    private void RemoveCaretOverlay()
    {
        if (_caretOverlay is null) return;
        _pageCanvas.Children.Remove(_caretOverlay);
        _caretOverlay = null;
    }

    /// <summary>
    /// Document-space caret rectangle (and colour) for <paramref name="caretIndex"/>
    /// inside <paramref name="input"/>'s value, or null if the field's box can't
    /// be located. Derives X from the shaped glyph pen positions of the value
    /// text fragment; an empty field gets a caret spanning the content box.
    /// </summary>
    private static (double X, double Y, double H, AvColor Color)? ComputeCaretRect(
        Starling.Layout.Box.BlockBox root, DomElement input, int caretIndex)
    {
        if (FindBoxAbs(root, 0, 0, b => ReferenceEquals(b.Element, input)) is not { } found)
            return null;

        var inputBox = found.Box;
        var color = ResolveCaretColor(inputBox);
        var cx = found.X + inputBox.Border.Left + inputBox.Padding.Left;
        var cy = found.Y + inputBox.Border.Top + inputBox.Padding.Top;

        var tb = FindValueTextBox(inputBox, cx, cy);
        if (tb is { } th)
        {
            // The value is laid out as one fragment per whitespace-delimited token
            // (words and the spaces between them), all in this text box. Walk them
            // in order, mapping the global caret index onto the fragment that holds
            // it — using only Fragments[0] froze the caret at the first space.
            var t = (Starling.Layout.Box.TextBox)th.Box;
            var remaining = Math.Max(0, caretIndex);
            foreach (var frag in t.Fragments)
            {
                if (remaining <= frag.Text.Length)
                    return (th.X + frag.X + XOffsetInFragment(frag, remaining),
                            th.Y + frag.Y, frag.Height, color);
                remaining -= frag.Text.Length;
            }
            // Past the last character → caret at the end of the final fragment.
            var last = t.Fragments[^1];
            return (th.X + last.X + XOffsetInFragment(last, last.Text.Length),
                    th.Y + last.Y, last.Height, color);
        }

        // Empty field: caret at the content origin, spanning the content box.
        var contentH = inputBox.Frame.Height
            - inputBox.Border.Top - inputBox.Border.Bottom
            - inputBox.Padding.Top - inputBox.Padding.Bottom;
        return (cx, cy, contentH > 2 ? contentH : 14, color);
    }

    private int CaretIndexFromClick((double X, double Y) clickDoc)
    {
        var val = CurrentValue();
        if (_focusedInput is null || _currentPage is null || val.Length == 0) return val.Length;
        if (FindBoxAbs(_currentPage.Root, 0, 0, b => ReferenceEquals(b.Element, _focusedInput)) is not { } found)
            return val.Length;

        var inputBox = found.Box;
        var cx = found.X + inputBox.Border.Left + inputBox.Padding.Left;
        var cy = found.Y + inputBox.Border.Top + inputBox.Padding.Top;
        if (FindValueTextBox(inputBox, cx, cy) is not { } th) return val.Length;

        // Find the value fragment under the click (or the last one when the click
        // is past the text), then add the char offset within it to the count of
        // characters in the fragments before it.
        var t = (Starling.Layout.Box.TextBox)th.Box;
        var prior = 0;
        for (var fi = 0; fi < t.Fragments.Count; fi++)
        {
            var frag = t.Fragments[fi];
            var fragLeft = th.X + frag.X;
            var isLast = fi == t.Fragments.Count - 1;
            if (clickDoc.X < fragLeft + frag.Width || isLast)
                return prior + CharOffsetInFragment(frag, clickDoc.X - fragLeft);
            prior += frag.Text.Length;
        }
        return val.Length;
    }

    /// <summary>Character index (0..len) within <paramref name="frag"/> nearest to
    /// local x-offset <paramref name="localX"/> from the fragment's left edge,
    /// using shaped glyph midpoints when available and a proportional fallback.</summary>
    private static int CharOffsetInFragment(Starling.Layout.Box.TextFragment frag, double localX)
    {
        if (localX <= 0) return 0;
        if (frag.Shaped is { } sh && sh.Glyphs.Length == frag.Text.Length)
        {
            for (var i = 0; i < sh.Glyphs.Length; i++)
            {
                var left = sh.Glyphs[i].X;
                var right = i + 1 < sh.Glyphs.Length ? sh.Glyphs[i + 1].X : frag.Width;
                if (localX < (left + right) / 2) return i;
            }
            return frag.Text.Length;
        }
        var approx = (int)Math.Round(localX / Math.Max(1, frag.Width) * frag.Text.Length);
        return Math.Clamp(approx, 0, frag.Text.Length);
    }

    /// <summary>First non-empty text fragment box under <paramref name="inputBox"/>,
    /// with its absolute origin (the rendered value text).</summary>
    private static (LayoutBox Box, double X, double Y)? FindValueTextBox(
        LayoutBox inputBox, double contentX, double contentY)
    {
        foreach (var child in inputBox.Children)
        {
            if (FindBoxAbs(child, contentX, contentY,
                    b => b is Starling.Layout.Box.TextBox { Fragments.Count: > 0 }) is { } r)
                return r;
        }
        return null;
    }

    /// <summary>Finds the first box matching <paramref name="pred"/>, returning it
    /// with its absolute (document-space) border-box origin. Mirrors the
    /// origin accumulation used by <see cref="BoxHitTester"/>.</summary>
    private static (LayoutBox Box, double X, double Y)? FindBoxAbs(
        LayoutBox box, double originX, double originY, Func<LayoutBox, bool> pred)
    {
        var fx = originX + box.Frame.X;
        var fy = originY + box.Frame.Y;
        if (pred(box)) return (box, fx, fy);
        if (box is Starling.Layout.Box.TextBox) return null;

        var cx = fx + box.Border.Left + box.Padding.Left;
        var cy = fy + box.Border.Top + box.Padding.Top;
        foreach (var child in box.Children)
            if (FindBoxAbs(child, cx, cy, pred) is { } r) return r;
        return null;
    }

    private static double XOffsetInFragment(Starling.Layout.Box.TextFragment frag, int caretIndex)
    {
        var n = Math.Clamp(caretIndex, 0, frag.Text.Length);
        if (n == 0) return 0;
        if (frag.Shaped is { } sh && sh.Glyphs.Length == frag.Text.Length)
            return n < sh.Glyphs.Length ? sh.Glyphs[n].X : frag.Width;
        return frag.Text.Length == 0 ? 0 : frag.Width * n / frag.Text.Length;
    }

    private static AvColor ResolveCaretColor(LayoutBox box)
    {
        var c = box.Style?.GetColor(PropertyId.Color);
        if (c is null) return AvColor.FromArgb(255, 0, 0, 0);
        return AvColor.FromArgb(c.A == 0 ? (byte)255 : c.A, c.R, c.G, c.B);
    }

    // ---- Live page event loop + DOM event dispatch --------------------
    //
    // While the current page carries a live JS context (PageScripting, attached
    // by the engine's interactive load), a ~60Hz timer pumps its microtasks /
    // setTimeout / rAF / fetch completions against wall-clock time and re-renders
    // whatever the page mutates. DOM events from interaction (focus/input/key…)
    // are dispatched into the same context so page handlers run.

    /// <summary>
    /// (Re)bind the live-page pump to the current page's JS context. Called at the
    /// end of <see cref="ShowPage"/> and by the shell once a navigation settles
    /// (the no-DOM-mutation case returns the already-shown page object, so
    /// ShowPage isn't re-invoked but the context has since been attached). The
    /// wall-clock baseline resets only when the context object changes, so a
    /// relayout of the same page keeps timer due-times monotonic.
    /// </summary>
    /// <summary>Wall-clock ms the live-page pump has been driving the current JS
    /// context — the timestamp to render a screenshot at so any CSS animations
    /// sample the same frame that's on screen. Zero when no live context is bound.</summary>
    public long LiveElapsedMs => _boundScripting is null ? 0 : _liveStopwatch.ElapsedMilliseconds;

    public void BindLiveScripting()
    {
        var scripting = _currentPage?.Scripting;
        if (scripting is null)
        {
            _liveTimer.Stop();
            _boundScripting = null;
            return;
        }
        if (!ReferenceEquals(scripting, _boundScripting))
        {
            _boundScripting = scripting;
            _liveStopwatch.Restart();
        }
        if (!_liveTimer.IsEnabled) _liveTimer.Start();
    }

    private void LiveTick()
    {
        var scripting = _currentPage?.Scripting;
        if (scripting is null) { _liveTimer.Stop(); _boundScripting = null; return; }

        bool mutated;
        try { mutated = scripting.PumpFrame(_liveStopwatch.ElapsedMilliseconds); }
        catch (Exception ex)
        {
            _diag.Log(DiagLevel.Warn, "gui", $"live JS pump failed: {ex.Message}");
            return;
        }
        if (mutated) { CaretLog("LiveTick: pump mutated -> RefreshLiveLayout"); RefreshLiveLayout(); }
    }

    /// <summary>Re-lay-out + repaint after the live loop mutated the DOM,
    /// preserving scroll and the text caret. No-op without a relayout hook.</summary>
    private void RefreshLiveLayout()
    {
        if (_currentPage is null || _relayout is null) return;
        var relaid = _relayout(_currentPage, CurrentViewportSize());
        if (relaid is not null) ShowPage(relaid, preserveScroll: true);
    }

    /// <summary>Dispatch a DOM event into the page's JS listeners (a no-op for
    /// pages without a live JS context), then ensure the live pump is running so
    /// any async work the listener scheduled (timers, fetch) gets serviced.
    /// Returns true when the dispatch synchronously mutated the DOM.</summary>
    private bool DispatchDom(DomElement target, Event evt)
    {
        var scripting = _currentPage?.Scripting;
        if (scripting is null) return false;
        bool mutated = false;
        try { mutated = scripting.DispatchEvent(target, evt); }
        catch (Exception ex) { _diag.Log(DiagLevel.Warn, "gui", $"DOM event dispatch failed: {ex.Message}"); }
        if (!_liveTimer.IsEnabled) BindLiveScripting();
        return mutated;
    }

    /// <summary>
    /// Dispatches a DOM <c>click</c> at the hit element into the page's JS, then
    /// runs the click's default action for form controls — toggling a
    /// checkbox/radio (firing <c>input</c>+<c>change</c>) and submitting the
    /// owning form for a submit button (firing a cancelable <c>submit</c>). The
    /// page is re-laid-out if any of this mutated the DOM. Returns true when a
    /// live page consumed the click — the JS cancelled it, we activated a control,
    /// or the DOM changed — so the caller skips chrome handling (blur, link nav).
    /// </summary>
    private bool DispatchClick(BoxHitTester.HitResult hit, (double X, double Y) doc)
    {
        if (_currentPage?.Scripting is null) return false;
        if (FindClickTarget(hit.Box) is not { } target) return false;

        var click = new MouseEvent("click", new EventInit(Bubbles: true, Cancelable: true))
        {
            Button = 0,
            ClientX = doc.X - _scroll.Offset.X,
            ClientY = doc.Y - _scroll.Offset.Y,
        };
        var mutated = DispatchDom(target, click);

        var actioned = false;
        if (!click.DefaultPrevented)
            mutated |= RunClickDefault(target, out actioned);

        if (mutated) RefreshLiveLayout();
        return click.DefaultPrevented || actioned || mutated;
    }

    /// <summary>Runs the activation behaviour of a click on <paramref name="clicked"/>
    /// (or its nearest enclosing control): checkbox/radio toggle, or implicit form
    /// submission for a submit button. <paramref name="actioned"/> reports whether
    /// a control was activated; the return value reports whether the DOM mutated.</summary>
    private bool RunClickDefault(DomElement clicked, out bool actioned)
    {
        actioned = false;
        if (NearestControl(clicked) is not { } control) return false;

        switch (control.LocalName)
        {
            case "input":
                var type = (control.GetAttribute("type") ?? "text").Trim().ToLowerInvariant();
                if (type is "checkbox" or "radio")
                {
                    actioned = true;
                    var m = DispatchDom(control, new InputEvent("input", new EventInit(Bubbles: true)));
                    m |= DispatchDom(control, new Event("change", new EventInit(Bubbles: true)));
                    return m;
                }
                if (type is "submit" or "image")
                    return SubmitOwningForm(control, ref actioned);
                return false;

            case "button":
                var btnType = (control.GetAttribute("type") ?? "submit").Trim().ToLowerInvariant();
                if (btnType == "submit")
                    return SubmitOwningForm(control, ref actioned);
                return false;

            default:
                return false;
        }
    }

    /// <summary>Fires a cancelable <c>submit</c> at <paramref name="control"/>'s
    /// owning &lt;form&gt; (implicit form submission). Sets <paramref name="actioned"/>
    /// when a form was found; returns whether the submit handler mutated the DOM.
    /// Actual form navigation when the event isn't cancelled is not yet wired —
    /// the SPA/todo pattern preventDefaults and handles submission in JS.</summary>
    private bool SubmitOwningForm(DomElement control, ref bool actioned)
    {
        if (NearestForm(control) is not { } form) return false;
        actioned = true;
        return DispatchDom(form, new Event("submit", new EventInit(Bubbles: true, Cancelable: true)));
    }

    /// <summary>Nearest DOM element enclosing <paramref name="box"/> (the click's
    /// event target), or null for a hit with no element ancestor.</summary>
    private static DomElement? FindClickTarget(LayoutBox? box)
    {
        for (var b = box; b is not null; b = b.Parent)
            if (b.Element is DomElement el) return el;
        return null;
    }

    /// <summary>Nearest &lt;button&gt;/&lt;input&gt; control at or above
    /// <paramref name="el"/> (a click on a button's inner text activates the
    /// button), or null.</summary>
    private static DomElement? NearestControl(DomElement el)
    {
        for (DomNode? n = el; n is not null; n = n.ParentNode)
            if (n is DomElement { LocalName: "button" or "input" } c) return c;
        return null;
    }

    private static DomElement? NearestForm(DomElement el)
    {
        for (DomNode? n = el; n is not null; n = n.ParentNode)
            if (n is DomElement { LocalName: "form" } f) return f;
        return null;
    }

    // ---- Programmatic input (MCP automation) --------------------------
    //
    // ClickAt / MoveTo / TypeText let an out-of-process driver (the MCP server)
    // synthesize input without a real device. Coordinates are document-space CSS
    // px from the page's top-left — the same space browser_screenshot captures
    // (full scroll extent) — so a driver can click/move where it sees a target in
    // a screenshot. These reuse the very same paths the pointer/keyboard handlers
    // drive, so synthetic and real input behave identically.

    /// <summary>True once a page is laid out and ready to receive synthetic input.</summary>
    public bool HasPage => _currentPage is not null;

    /// <summary>Synthesizes a left click at a document-space point. See
    /// <see cref="PerformClick"/> for the decision tree. Fails only when no page
    /// is loaded; the returned detail describes what the click did.</summary>
    public InputResult ClickAt(double x, double y)
    {
        if (_currentPage is null) return new InputResult(false, "no page is loaded");
        return new InputResult(true, PerformClick((x, y)).Detail);
    }

    /// <summary>Moves the synthetic mouse to a document-space point: updates the
    /// cursor + CSS <c>:hover</c> exactly as a real move would, and dispatches DOM
    /// <c>mouseover</c>/<c>mousemove</c> (plus <c>mouseout</c> on the previously
    /// hovered element) so JS hover handlers run. Fails only when no page is
    /// loaded.</summary>
    public InputResult MoveTo(double x, double y)
    {
        if (_currentPage is null) return new InputResult(false, "no page is loaded");

        var hit = BoxHitTester.HitTest(_currentPage.Root, x, y);
        UpdateHover(hit);
        DispatchMouseMove((x, y), FindClickTarget(hit.Box));

        if (hit.Box is null) return new InputResult(true, "moved over empty area");
        var link = hit.LinkAnchor?.GetAttribute("href") is { Length: > 0 } href ? $" → {href}" : string.Empty;
        return new InputResult(true, $"moved over <{DescribeHit(hit)}>{link}");
    }

    /// <summary>Types literal text into the currently focused text field (focus one
    /// first via <see cref="ClickAt"/>). Control characters are dropped; the text
    /// is inserted at the caret and a DOM <c>input</c> event fires (so
    /// search-as-you-type / form handlers run). When <paramref name="submit"/> is
    /// set, Enter is pressed afterward to submit the owning form. Fails when no
    /// page is loaded or no text field is focused.</summary>
    public InputResult TypeText(string text, bool submit = false)
    {
        if (_currentPage is null) return new InputResult(false, "no page is loaded");
        if (_focusedInput is null)
            return new InputResult(false, "no focused text field — click an input first");

        var clean = new string((text ?? string.Empty).Where(c => !char.IsControl(c)).ToArray());
        if (clean.Length > 0)
        {
            var val = CurrentValue();
            var idx = Math.Clamp(_caretIndex, 0, val.Length);
            CommitValue(val.Insert(idx, clean), idx + clean.Length);
        }

        if (submit)
        {
            var fired = PressEnterOnFocused();
            return new InputResult(true,
                $"typed \"{clean}\"; submit {(fired ? "fired" : "no-op")}; value=\"{CurrentValue()}\"");
        }

        return new InputResult(true, clean.Length == 0
            ? "nothing to type (text was empty or control-only)"
            : $"typed \"{clean}\"; value=\"{CurrentValue()}\"");
    }

    /// <summary>Dispatches DOM mouse-move events for a synthetic move to
    /// <paramref name="target"/>: <c>mouseout</c>/<c>mouseover</c> across a target
    /// change (with the other element as relatedTarget), then <c>mousemove</c>.
    /// Client coordinates are viewport-relative (document minus scroll), matching
    /// <see cref="DispatchClick"/>. Tracks <see cref="_mouseTarget"/> and re-lays
    /// out if a handler mutated the DOM. A no-op without a live JS context.</summary>
    private void DispatchMouseMove((double X, double Y) doc, DomElement? target)
    {
        if (_currentPage?.Scripting is null) { _mouseTarget = target; return; }

        var clientX = doc.X - _scroll.Offset.X;
        var clientY = doc.Y - _scroll.Offset.Y;
        MouseEvent Make(string type, DomElement? related) =>
            new(type, new EventInit(Bubbles: true, Cancelable: true))
            {
                Button = 0,
                ClientX = clientX,
                ClientY = clientY,
                RelatedTarget = related,
            };

        var mutated = false;
        if (!ReferenceEquals(target, _mouseTarget))
        {
            if (_mouseTarget is { } prev) mutated |= DispatchDom(prev, Make("mouseout", target));
            if (target is { } cur) mutated |= DispatchDom(cur, Make("mouseover", _mouseTarget));
            _mouseTarget = target;
        }
        if (target is not null) mutated |= DispatchDom(target, Make("mousemove", null));
        if (mutated) RefreshLiveLayout();
    }

    /// <summary>Synthesizes an Enter keypress on the focused field: dispatches
    /// <c>keydown</c>, submits the owning form (for a text <c>&lt;input&gt;</c>,
    /// implicit submission), then <c>keyup</c> — mirroring the Enter branch of
    /// <see cref="OnPageKeyDown"/>. Returns whether any of it mutated the DOM.</summary>
    private bool PressEnterOnFocused()
    {
        if (_focusedInput is null) return false;
        var target = _focusedInput;
        var down = new KeyboardEvent("keydown", new EventInit(Bubbles: true, Cancelable: true)) { Key = "Enter", Code = "Enter" };
        var mutated = DispatchDom(target, down);
        if (target.LocalName == "input" && NearestForm(target) is { } form)
            mutated |= DispatchDom(form, new Event("submit", new EventInit(Bubbles: true, Cancelable: true)));
        var up = new KeyboardEvent("keyup", new EventInit(Bubbles: true, Cancelable: true)) { Key = "Enter", Code = "Enter" };
        mutated |= DispatchDom(target, up);
        if (mutated) RefreshLiveLayout();
        return mutated;
    }

    private static KeyboardEvent MakeKeyboardEvent(string type, KeyEventArgs e) =>
        new(type, new EventInit(Bubbles: true, Cancelable: true))
        {
            Key = DomKeyName(e),
            Code = e.PhysicalKey.ToString(),
            CtrlKey = e.KeyModifiers.HasFlag(KeyModifiers.Control),
            ShiftKey = e.KeyModifiers.HasFlag(KeyModifiers.Shift),
            AltKey = e.KeyModifiers.HasFlag(KeyModifiers.Alt),
            MetaKey = e.KeyModifiers.HasFlag(KeyModifiers.Meta),
        };

    private static string DomKeyName(KeyEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.KeySymbol)) return e.KeySymbol!;
        return e.Key switch
        {
            Key.Enter => "Enter",
            Key.Tab => "Tab",
            Key.Back => "Backspace",
            Key.Delete => "Delete",
            Key.Escape => "Escape",
            Key.Left => "ArrowLeft",
            Key.Right => "ArrowRight",
            Key.Up => "ArrowUp",
            Key.Down => "ArrowDown",
            Key.Home => "Home",
            Key.End => "End",
            Key.Space => " ",
            _ => e.Key.ToString(),
        };
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
            // If we were previously showing hover pixels, the display list just
            // reverted to the baseline: drop the hover-painted cache and repaint.
            var hadOverrides = _hoverOverrides is not null;
            _hoverOverrides = null;
            if (hadOverrides)
            {
                _renderer.InvalidateCache();
                RenderViewportRegion();
            }
            return;
        }

        // The override set changes the display list without changing the page
        // version, so the picture cache must be dropped or it would blit stale
        // (non-hover) pixels. Wholesale invalidation matches the WP: any
        // display-list change is a full miss (smarter is wp:M12-06).
        _hoverOverrides = newOverrides;
        _renderer.InvalidateCache();
        RenderViewportRegion();
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
    /// Renders the <paramref name="viewport"/> region of <c>_currentPage.Root</c>
    /// through the paint pipeline, threading the current hover overrides into
    /// <see cref="DisplayListBuilder"/> so <c>a:hover</c> rules repaint without
    /// re-layout. Called on page load, on scroll, and on every hover transition
    /// that changes paint-affecting styles.
    /// </summary>
    private void RenderPageBitmap(Starling.Layout.Rect viewport)
    {
        if (_currentPage is null) return;

        var overrides = _hoverOverrides;
        Func<LayoutBox, ComputedStyle?>? styleOverride = overrides is null
            ? null
            : box => ResolveOverride(box, overrides);

        RenderedBitmap rendered;
        // A finished navigation can leave its stopped Activity as the UI thread's
        // ambient Activity.Current (async span leak — see RunNavigation). A
        // standalone render (hover / resize / scroll) must not pile into that prior
        // navigation's trace, so detach from a leaked, already-stopped span. Renders
        // that run inside a live navigation keep their (non-stopped) parent and nest.
        if (Activity.Current is { IsStopped: true })
            Activity.Current = null;
        using (_diag.Span("gui", "render"))
            rendered = _renderer.Render(_currentPage.Root, (float)_currentScale, styleOverride, _currentPage.ImageResolver, viewport, _currentPage.DisplayListVersion);
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
        _relayoutTimer.Stop();
        _caretBlinkTimer.Stop();
        _liveTimer.Stop();
        _renderer.Dispose();
        _currentPage?.Dispose();
        (_pageImage.Source as IDisposable)?.Dispose();
    }
}

/// <summary>Outcome of a synthetic-input call (<see cref="WebviewPanel.ClickAt"/>,
/// <see cref="WebviewPanel.MoveTo"/>, <see cref="WebviewPanel.TypeText"/>):
/// <paramref name="Ok"/> is false only for precondition failures (no page / no
/// focused field); <paramref name="Detail"/> is a short human-readable summary.</summary>
internal readonly record struct InputResult(bool Ok, string Detail);
