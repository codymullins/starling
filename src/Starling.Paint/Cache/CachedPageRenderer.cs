using Starling.Common.Diagnostics;
using Starling.Common.Image;
using Starling.Css.Cascade;
using Starling.Layout.Box;
using Starling.Layout.Tree;
using Starling.Paint.Backend;
using Starling.Paint.DisplayList;
using LayoutRect = Starling.Layout.Rect;
using PaintList = Starling.Paint.DisplayList.DisplayList;

namespace Starling.Paint.Cache;

/// <summary>
/// Wraps an <see cref="IPaintBackend"/> with a <see cref="PictureCache"/> so
/// scrolling re-uses the previous frame's pixels instead of re-painting the whole
/// viewport. On a cache HIT the cached pixels are blitted into the output with no
/// backend call; on a PARTIAL the newly-exposed strips are painted (each via the
/// viewport-clipped <see cref="DisplayListBuilder"/> + backend path) and stitched
/// in; on a MISS the full viewport is painted and seeds the cache. The full-page
/// (no-viewport) path bypasses the cache entirely so headless screenshots stay
/// byte-for-byte identical.
/// </summary>
internal sealed class CachedPageRenderer
{
    private readonly IPaintBackend _backend;
    private readonly IDiagnostics _diag;
    private readonly PictureCache _cache;

    public CachedPageRenderer(IPaintBackend backend, IDiagnostics? diagnostics = null, PictureCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
        _diag = diagnostics ?? NoopDiagnostics.Instance;
        _cache = cache ?? new PictureCache(_diag);
    }

    /// <summary>Drops cached pixels — call on navigation / tab switch / re-layout.</summary>
    public void Invalidate() => _cache.Invalidate();

    /// <summary>
    /// Renders <paramref name="root"/> for <paramref name="viewport"/> at
    /// <paramref name="scale"/>, consulting the cache keyed by
    /// <paramref name="pageVersion"/>. The output is a <see cref="RenderedBitmap"/>
    /// for the requested viewport, identical to a from-scratch render of the same
    /// region.
    /// </summary>
    public RenderedBitmap Render(
        BlockBox root,
        LayoutRect viewport,
        float scale,
        int pageVersion,
        Func<Box, ComputedStyle?>? styleOverride = null,
        IImageResolver? images = null)
    {
        ArgumentNullException.ThrowIfNull(root);

        var device = PictureCache.ToDeviceRect(viewport, scale);

        if (_cache.TryServe(viewport, scale, pageVersion, out var hit))
            return Compose(hit, device.Width, device.Height);

        var strips = _cache.ComputeUncachedStrips(viewport, scale, pageVersion);

        // Full miss: one strip equal to the whole device rect. Paint it, seed,
        // and serve from the now-complete cache.
        if (strips.Count == 1 && strips[0].Equals(device))
        {
            using var full = PaintStrip(root, strips[0], scale, styleOverride, images);
            // Seed against the raster's real device rect (origin + actual size);
            // a fractional scale's ceil can differ from the request by a pixel.
            var seedRect = new DeviceRect(device.X, device.Y, full.Width, full.Height);
            _cache.Reset(seedRect, scale, pageVersion, full);
            return ServeAfterFill(viewport, scale, pageVersion, device);
        }

        // Partial: paint each newly-exposed strip and stitch it in.
        foreach (var strip in strips)
        {
            using var painted = PaintStrip(root, strip, scale, styleOverride, images);
            var actual = new DeviceRect(strip.X, strip.Y, painted.Width, painted.Height);
            _cache.Stitch(actual, painted, scale, pageVersion);
        }
        return ServeAfterFill(viewport, scale, pageVersion, device);
    }

    private RenderedBitmap ServeAfterFill(LayoutRect viewport, float scale, int pageVersion, DeviceRect device)
    {
        if (!_cache.TryServe(viewport, scale, pageVersion, out var blit))
            throw new InvalidOperationException("Cache should fully cover the viewport after seed/stitch.");
        return Compose(blit, device.Width, device.Height);
    }

    /// <summary>
    /// Paints a single device-pixel strip through the viewport-clipped builder +
    /// backend. The strip's device rect is converted back to a page-coord viewport
    /// (device / scale) so the backend translates content to the strip's top-left;
    /// the result is therefore directly stitchable at the strip's cache position.
    /// </summary>
    private RenderedBitmap PaintStrip(
        BlockBox root,
        DeviceRect strip,
        float scale,
        Func<Box, ComputedStyle?>? styleOverride,
        IImageResolver? images)
    {
        var pageViewport = new LayoutRect(strip.X / scale, strip.Y / scale, strip.Width / scale, strip.Height / scale);
        PaintList list = new DisplayListBuilder().Build(root, pageViewport, styleOverride, images);
        return _backend.Render(list, pageViewport, scale);
    }

    private static RenderedBitmap Compose(CacheBlit blit, int outWidth, int outHeight)
    {
        var outBuf = new byte[checked(outWidth * outHeight * 4)];
        var destStride = outWidth * 4;
        var rowBytes = blit.Width * 4;
        for (var row = 0; row < blit.Height; row++)
        {
            var srcOffset = ((blit.SourceY + row) * blit.SourceStride) + (blit.SourceX * 4);
            var destOffset = ((blit.DestY + row) * destStride) + (blit.DestX * 4);
            Array.Copy(blit.SourcePixels, srcOffset, outBuf, destOffset, rowBytes);
        }
        return new RenderedBitmap(outWidth, outHeight, outBuf);
    }
}
