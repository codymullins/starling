using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Starling.Gui.Avalonia.Imaging;
using Starling.Gui.Avalonia.Theme;
using Tessera.Common.Diagnostics;
using Tessera.Common.Image;
using Tessera.Engine;
using Tessera.Gui; // linked BoxHitTester.cs lives in this namespace
using DomElement = Tessera.Dom.Element;

namespace Starling.Gui.Avalonia.Controls;

/// <summary>
/// The page surface — a ScrollViewer wrapping an Image bound to a
/// WriteableBitmap rendered from a <see cref="LaidOutPage"/>. Re-derives
/// hover / link from the box tree via <see cref="BoxHitTester"/>, mirroring
/// the MAUI WebviewPanel's interaction model for the smoke-test subset.
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
    private readonly TextBlock _placeholder;
    private readonly Panel _surface;

    private LaidOutPage? _currentPage;
    private DomElement? _hoverAnchor;

    public WebviewPanel(ThemeManager tm, IDiagnostics diag, Action<string> onLinkActivated, Action<string, bool> onStatus)
    {
        _tm = tm;
        _diag = diag;
        _renderer = new PageRendererHost(diag);
        _onLinkActivated = onLinkActivated;
        _onStatus = onStatus;

        _pageImage = new Image
        {
            // Uniform (not None) so explicit Width/Height in DIPs drive layout
            // rather than the bitmap's pixel size. On Retina, the 2× bitmap
            // maps 1:1 to physical pixels under the DIP-sized control.
            Stretch = Stretch.Uniform,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
            IsVisible = false,
        };
        _pageImage.PointerMoved += OnPointerMoved;
        _pageImage.PointerExited += OnPointerExited;
        _pageImage.PointerPressed += OnPointerPressed;

        _placeholder = new TextBlock
        {
            Text = "Type a URL above and press Enter to render a page.",
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };

        _surface = new Panel
        {
            Children = { _placeholder, _pageImage },
        };

        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _surface,
        };

        Content = _scroll;
        ApplyTheme();
        _tm.Changed += (_, _) => ApplyTheme();
    }

    private void ApplyTheme()
    {
        var t = _tm.Tokens;
        _scroll.Background = new SolidColorBrush(t.WebBg);
        _surface.Background = new SolidColorBrush(t.WebBg);
        _placeholder.Foreground = new SolidColorBrush(t.Muted);
        _placeholder.FontSize = _tm.Metrics.FsLg;
    }

    /// <summary>
    /// Renders <paramref name="page"/> through the Skia pipeline and shows the
    /// resulting bitmap. The image's pixel dimensions equal the document size;
    /// the ScrollViewer scrolls anything taller than the viewport.
    /// </summary>
    public void ShowPage(LaidOutPage page)
    {
        _currentPage?.Dispose();
        _currentPage = page;

        var scale = GetRenderScale();
        RenderedBitmap rendered;
        using (_diag.Span("gui", "render"))
            rendered = _renderer.Render(page.Root, (float)scale);
        WriteableBitmap bmp;
        using (rendered)
            bmp = BitmapBridge.ToWriteableBitmap(rendered, scale);

        // Dispose old bitmap if any.
        (_pageImage.Source as IDisposable)?.Dispose();
        _pageImage.Source = bmp;
        _pageImage.Width = page.Root.Frame.Width;
        _pageImage.Height = page.Root.Frame.Height;
        _pageImage.IsVisible = true;
        _placeholder.IsVisible = false;
        _hoverAnchor = null;
    }

    private double GetRenderScale()
    {
        // VisualRoot exposes TopLevel.RenderScaling once the window is open;
        // before then, default to 1.0 — the first post-Opened render replaces
        // the surface at the correct density.
        var tl = TopLevel.GetTopLevel(this);
        return tl?.RenderScaling ?? 1.0;
    }

    private (double X, double Y)? PointerToDocSpace(PointerEventArgs e)
    {
        if (_currentPage is null) return null;
        var p = e.GetPosition(_pageImage);
        if (p.X < 0 || p.Y < 0) return null;
        if (p.X >= _pageImage.Bounds.Width || p.Y >= _pageImage.Bounds.Height) return null;
        return (p.X, p.Y);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_currentPage is null) return;
        var doc = PointerToDocSpace(e);
        if (doc is null) { ResetCursor(); return; }

        var hit = BoxHitTester.HitTest(_currentPage.Root, doc.Value.X, doc.Value.Y);
        if (hit.LinkAnchor is not null)
        {
            Cursor = new Cursor(StandardCursorType.Hand);
            _hoverAnchor = hit.LinkAnchor;
            var href = hit.LinkAnchor.GetAttribute("href");
            if (!string.IsNullOrEmpty(href))
                _onStatus(href!, false);
        }
        else
        {
            ResetCursor();
        }
    }

    private void OnPointerExited(object? sender, PointerEventArgs e) => ResetCursor();

    private void ResetCursor()
    {
        if (_hoverAnchor is null) return;
        Cursor = new Cursor(StandardCursorType.Arrow);
        _hoverAnchor = null;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_currentPage is null) return;
        if (!e.GetCurrentPoint(_pageImage).Properties.IsLeftButtonPressed) return;
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

    private string? ResolveLink(string href)
    {
        if (_currentPage is null) return null;
        // The engine stores the page's effective base URL on LaidOutPage.Url —
        // resolve relative hrefs against it. Absolute hrefs (http/https/file)
        // pass through unchanged when Url.TryParse succeeds standalone.
        if (Uri.TryCreate(href, UriKind.Absolute, out var abs))
            return abs.ToString();

        if (Uri.TryCreate(_currentPage.Url, UriKind.Absolute, out var baseUri) &&
            Uri.TryCreate(baseUri, href, out var combined))
        {
            return combined.ToString();
        }
        return href;
    }

    public void Dispose()
    {
        _renderer.Dispose();
        _currentPage?.Dispose();
        (_pageImage.Source as IDisposable)?.Dispose();
    }
}
