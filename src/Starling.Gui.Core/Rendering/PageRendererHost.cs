using Starling.Common.Diagnostics;
using Starling.Common.Image;
using Starling.Css.Cascade;
using Starling.Layout.Box;
using Starling.Layout.Tree;
using Starling.Paint;
using Starling.Paint.Backend;
using Starling.Paint.Cache;
using Starling.Paint.Compositor;
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
    private readonly LayerCacheStore _layerCaches;
    private bool _disposed;

    public PageRendererHost(IDiagnostics? diagnostics = null)
    {
        _diag = diagnostics ?? NoopDiagnostics.Instance;
        _backend = PaintBackendSelector.Create(FontResolver.Default, webFonts: null, _diag);
        _cached = new CachedPageRenderer(_backend, _diag);
        _layerCaches = new LayerCacheStore(_diag);
    }

    /// <summary>
    /// Drops the flat scroll picture cache only. The shell calls this on an
    /// in-place relayout / hover-override change so the next flat render is a
    /// clean repaint. The per-layer compositor caches are NOT dropped: they are
    /// keyed by each layer's slice content hash (LTF-02), so an unchanged layer
    /// stays valid across a relayout and only a real content change re-rasters it
    /// (LTF-03). Scroll-only repaints leave the flat cache intact so it can serve.
    /// </summary>
    public void InvalidateCache()
    {
        _cached.Invalidate();
    }

    /// <summary>
    /// Navigation reset: drops the flat cache AND every persistent per-layer
    /// compositor cache. Called when the laid-out page belongs to a different
    /// Document, so no pixels from the previous page survive (LTF-03).
    /// </summary>
    public void ResetForNavigation()
    {
        _cached.Invalidate();
        _layerCaches.Clear();
    }

    /// <summary>
    /// Renders <paramref name="root"/>. When <paramref name="viewport"/> is
    /// supplied (a page-coordinate <see cref="LayoutRect"/>: X/Y = scroll
    /// offset, Width/Height = visible size), the display list is culled to it
    /// and the output bitmap is sized to the viewport — the scroll-driven path.
    /// When omitted, the full page is rendered into a bitmap sized to
    /// <c>root.Frame</c> (the legacy full-page behavior; the CPU rasterizer
    /// handles arbitrarily large surfaces).
    /// </summary>
    public RenderedBitmap Render(BlockBox root, float scale = 1.0f, Func<Box, ComputedStyle?>? styleOverride = null, IImageResolver? images = null, LayoutRect? viewport = null, int pageVersion = 0, Func<Starling.Dom.Element, (double X, double Y)>? scrollOffsets = null)
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
                return _cached.Render(root, v, scale, pageVersion, styleOverride, images, scrollOffsets);

            PaintList displayList = new DisplayListBuilder().Build(root, viewport: null, styleOverride, images, scrollOffsets);
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

    /// <summary>
    /// Renders <paramref name="root"/> through the compositor layer tree
    /// (M12-04): the box tree is split into per-layer display-list slices, each
    /// slice is rasterized into its own cached bitmap, and the bitmaps are
    /// composited top-down with each layer's transform / opacity / clip applied.
    /// This is the additive layer-compositing path; the flat
    /// <see cref="Render"/> path above is preserved for the legacy/screenshot
    /// callers and existing golden tests. <paramref name="viewport"/> is the
    /// page-coord visible region; when omitted the full page frame is used.
    /// </summary>
    public RenderedBitmap RenderViaLayerTree(BlockBox root, float scale = 1.0f, Func<Box, ComputedStyle?>? styleOverride = null, IImageResolver? images = null, LayoutRect? viewport = null, Func<Box, bool>? isAnimatingLayerRoot = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(root);

        try
        {
            // Persistent per-layer caches (keyed by element) let a transform/
            // opacity-only frame re-blit each layer from cache instead of
            // re-rasterizing (Phase 5); the transform/opacity are re-sampled into
            // the rebuilt tree and applied at composite time. Each layer is keyed
            // by its slice content hash (LTF-02), so a relayout that bumped the
            // page version no longer busts a layer whose content is unchanged.
            var tree = new LayerTreeBuilder(styleOverride, images, _diag, _layerCaches.CacheFor, isAnimatingLayerRoot).Build(root);
            var region = viewport ?? new LayoutRect(0, 0,
                Math.Max(1, root.Frame.Width),
                Math.Max(1, root.Frame.Height));
            var compositor = new Compositor(_backend, _diag);
            return compositor.Render(tree, region, scale);
        }
        catch (Exception ex)
        {
            _diag.LogException("gui", ex, $"layer-tree render via '{_backend.Name}' failed");
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
