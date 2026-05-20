using Starling.Common.Diagnostics;
using Starling.Common.Image;
using Starling.Css.Cascade;
using Starling.Layout.Box;
using Starling.Layout.Text;
using Starling.Layout.Tree;
using Starling.Paint;
using Starling.Paint.Backend;
using Starling.Paint.Cache;
using Starling.Paint.DisplayList;
using LayoutRect = Starling.Layout.Rect;
using LayoutSize = Starling.Layout.Size;
using PaintList = Starling.Paint.DisplayList.DisplayList;

namespace Starling.Gui;

/// <summary>
/// Drives the same DisplayListBuilder + paint-backend pipeline that
/// src/Starling.Gui/PageRenderer.cs uses on the MAUI side, returning a raw
/// <see cref="RenderedBitmap"/> that <c>BitmapBridge</c> can hand to Avalonia.
/// The backend (ImageSharp CPU / ImageSharp WebGPU) is picked by
/// <see cref="PaintBackendSelector"/> from the <c>STARLING_PAINT_BACKEND</c>
/// env var. Avalonia takes the RGBA buffer directly — the MAUI-specific
/// <c>ToImageSource</c> tail isn't needed.
/// </summary>
internal sealed class PageRendererHost : IDisposable
{
    private readonly IDiagnostics _diag;
    private readonly IPaintBackend _backend;
    private readonly CachedPageRenderer _cached;
    private bool _disposed;

    public PageRendererHost(IDiagnostics? diagnostics = null)
    {
        _diag = diagnostics ?? NoopDiagnostics.Instance;
        _backend = PaintBackendSelector.Create(FontResolver.Default, webFonts: null, _diag);
        _cached = new CachedPageRenderer(_backend, _diag);
    }

    /// <summary>
    /// Drops the picture cache. The shell calls this when the laid-out page
    /// changes (navigation / re-layout) so the next viewport render is a full
    /// repaint rather than a stale-pixel blit. Scroll-only repaints leave it
    /// intact so the cache can serve them.
    /// </summary>
    public void InvalidateCache() => _cached.Invalidate();

    /// <summary>
    /// Renders <paramref name="root"/>. When <paramref name="viewport"/> is
    /// supplied (a page-coordinate <see cref="LayoutRect"/>: X/Y = scroll
    /// offset, Width/Height = visible size), the display list is culled to it
    /// and the output bitmap is sized to the viewport — the scroll-driven path.
    /// When omitted, the full page is rendered into a bitmap sized to
    /// <c>root.Frame</c> (the legacy full-page behavior; the CPU rasterizer
    /// handles arbitrarily large surfaces).
    /// </summary>
    public RenderedBitmap Render(BlockBox root, float scale = 1.0f, Func<Box, ComputedStyle?>? styleOverride = null, IImageResolver? images = null, LayoutRect? viewport = null, int pageVersion = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(root);

        try
        {
            // Viewport-clipped (scroll) path goes through the picture cache so
            // scrolling reuses prior pixels. The no-viewport full-page path
            // (headless screenshots, very tall pages) bypasses the cache and
            // paints everything, preserving byte-identical legacy output.
            if (viewport is { } v)
                return _cached.Render(root, v, scale, pageVersion, styleOverride, images);

            PaintList displayList = new DisplayListBuilder().Build(root, viewport: null, styleOverride, images);
            var surfaceSize = new LayoutSize(
                Math.Max(1, root.Frame.Width),
                Math.Max(1, root.Frame.Height));
            return _backend.Render(displayList, surfaceSize, scale);
        }
        catch (Exception ex)
        {
            // Surface backend failures (WebGPU init, native shim missing, etc.)
            // through diagnostics so Aspire's trace view shows the full
            // exception on the GUI span instead of just a failed activity.
            _diag.LogException("gui", ex, $"page render via '{_backend.Name}' failed");
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _backend.Dispose();
    }
}
