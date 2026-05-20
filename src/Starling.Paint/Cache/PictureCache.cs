using Starling.Common.Diagnostics;
using Starling.Common.Image;
using LayoutRect = Starling.Layout.Rect;

namespace Starling.Paint.Cache;

/// <summary>
/// A WebRender-style single-bitmap picture cache. Holds the most recently
/// rasterized viewport as a tightly-packed RGBA8888 buffer plus the device-pixel
/// rectangle it covers, keyed by <c>(pageVersion, scale)</c>. A scroll whose new
/// viewport falls entirely inside the cached rectangle is served by blitting the
/// cached pixels at an offset (no backend call). A scroll that exposes a fresh
/// edge paints only the newly-visible strips and stitches them back in, growing
/// the cache until it would exceed a max-area budget — at which point the cache
/// is evicted and reseeded from the new content.
/// </summary>
/// <remarks>
/// Coordinate spaces: the cache works entirely in <em>device pixels</em>
/// (page CSS px × <c>scale</c>, rounded to whole integers). Page-coord viewports
/// are converted on the boundary via <see cref="ToDeviceRect"/>. Keeping the
/// internal math integer-only is what guarantees a HIT/PARTIAL serve is
/// byte-identical to a from-scratch render: the backend lands a page-coord item
/// at device <c>item.X*scale - viewport.X*scale</c>, so as long as every viewport
/// origin is at a whole device pixel, two aligned viewports differ only by an
/// integer device offset and row copies reproduce pixels exactly. This is also
/// why strips are rounded to whole device pixels (WP note: "rounds to whole
/// device pixels to avoid aliasing").
/// <para>
/// This is intentionally a single bitmap; an LRU tile grid is
/// <c>wp:M12-05-tile-grid</c>. Any display-list change is signalled by a bumped
/// <c>pageVersion</c> and invalidates the whole cache — smarter partial
/// invalidation is <c>wp:M12-06-invalidation</c>.
/// </para>
/// </remarks>
internal sealed class PictureCache
{
    private readonly IDiagnostics _diag;
    private readonly long _maxAreaPx;

    // Backing pixels for the cached region, RGBA8888, row stride _bounds.Width*4.
    private byte[]? _pixels;
    private DeviceRect _bounds;
    private float _scale;
    private int _pageVersion;

    /// <summary>Creates an empty cache with the given strip-painting diagnostics sink and area budget.</summary>
    /// <param name="diagnostics">Counter sink for hit/miss/partial/evict + strip area.</param>
    /// <param name="maxAreaPx">
    /// Maximum device-pixel area the cache buffer may occupy before a stitch that
    /// would grow past it evicts instead. Defaults to ~4M px (≈ a 2560×1600
    /// window). Sized as a small multiple of a typical viewport so a long scroll
    /// can't grow the single bitmap without bound — that unbounded growth is what
    /// the tile grid (M12-05) exists to solve.
    /// </param>
    public PictureCache(IDiagnostics? diagnostics = null, long maxAreaPx = 4_096_000)
    {
        _diag = diagnostics ?? NoopDiagnostics.Instance;
        _maxAreaPx = maxAreaPx;
    }

    /// <summary>True once a frame has seeded the cache for the current key.</summary>
    public bool HasContent => _pixels is not null;

    /// <summary>
    /// Attempts to serve <paramref name="viewport"/> entirely from cache. Returns
    /// <c>true</c> (HIT) when <paramref name="pageVersion"/> and
    /// <paramref name="scale"/> match the cached key and the requested device rect
    /// is fully contained in the cached rect; <paramref name="blit"/> then
    /// describes the sub-rect of the cache buffer to copy and where it lands in
    /// the (0,0)-origin output. Bumps <c>paint.cache.hit</c> or
    /// <c>paint.cache.miss</c>.
    /// </summary>
    public bool TryServe(LayoutRect viewport, float scale, int pageVersion, out CacheBlit blit)
    {
        blit = default;
        var want = ToDeviceRect(viewport, scale);

        if (_pixels is null || pageVersion != _pageVersion || scale != _scale || !_bounds.Contains(want))
        {
            _diag.Counter("paint.cache.miss", 1);
            return false;
        }

        blit = new CacheBlit(
            SourcePixels: _pixels,
            SourceStride: _bounds.Width * 4,
            SourceX: want.X - _bounds.X,
            SourceY: want.Y - _bounds.Y,
            DestX: 0,
            DestY: 0,
            Width: want.Width,
            Height: want.Height);
        _diag.Counter("paint.cache.hit", 1);
        return true;
    }

    /// <summary>
    /// Returns the list of device-pixel strip rectangles of <paramref name="viewport"/>
    /// that are NOT already covered by the cache for the matching key — the regions
    /// the host must paint and then <see cref="Stitch"/> back. Returns the whole
    /// requested rect (and bumps <c>paint.cache.miss</c>) when the key differs;
    /// returns the uncovered edges (and bumps <c>paint.cache.partial</c>) when the
    /// key matches but the rect spills past the cached bounds; returns empty when
    /// fully covered (a HIT — caller should have used <see cref="TryServe"/>).
    /// </summary>
    public IReadOnlyList<DeviceRect> ComputeUncachedStrips(LayoutRect viewport, float scale, int pageVersion)
    {
        var want = ToDeviceRect(viewport, scale);

        if (_pixels is null || pageVersion != _pageVersion || scale != _scale || !_bounds.Intersects(want))
        {
            _diag.Counter("paint.cache.miss", 1);
            return new[] { want };
        }

        if (_bounds.Contains(want))
            return Array.Empty<DeviceRect>();

        _diag.Counter("paint.cache.partial", 1);
        return Subtract(want, _bounds);
    }

    /// <summary>
    /// Stitches a freshly-painted edge strip into the cache, growing the cache
    /// buffer to the union of its current bounds and the strip. If growth would
    /// exceed the max-area budget the cache is evicted (<c>paint.cache.evict</c>)
    /// and reset to just the strip's content. Emits the painted area as
    /// <c>paint.cache.strip_area</c>.
    /// </summary>
    public void Stitch(DeviceRect stripBounds, RenderedBitmap stripPixels, float scale, int pageVersion)
    {
        ArgumentNullException.ThrowIfNull(stripPixels);
        _diag.Counter("paint.cache.strip_area", (double)stripBounds.Width * stripBounds.Height);

        // A version/scale mismatch slipping through here means the caller is
        // stitching against a stale key — reseed wholesale rather than corrupt.
        if (_pixels is null || pageVersion != _pageVersion || scale != _scale)
        {
            Reset(stripBounds, scale, pageVersion, stripPixels);
            return;
        }

        var grown = _bounds.Union(stripBounds);
        if ((long)grown.Width * grown.Height > _maxAreaPx)
        {
            _diag.Counter("paint.cache.evict", 1);
            Reset(stripBounds, scale, pageVersion, stripPixels);
            return;
        }

        var dest = new byte[checked(grown.Width * grown.Height * 4)];
        // Copy the existing cache into its place within the grown buffer, then
        // overlay the new strip. Strip and cache never overlap (strips are the
        // set-difference of the request and the cache), so order is irrelevant.
        BlitInto(dest, grown.Width, _pixels, _bounds, grown);
        BlitInto(dest, grown.Width, stripPixels.Rgba, stripBounds, grown);

        _pixels = dest;
        _bounds = grown;
    }

    /// <summary>Drops all cached content. The next render is a full MISS.</summary>
    public void Invalidate()
    {
        _pixels = null;
        _bounds = default;
        _scale = 0f;
        _pageVersion = 0;
    }

    /// <summary>Device-pixel rectangle of the cached region (for diagnostics/tests).</summary>
    public DeviceRect Bounds => _bounds;

    /// <summary>
    /// Replaces all cached content with <paramref name="pixels"/> placed at the
    /// device rect <paramref name="rect"/>. Used to seed on a MISS and to reseed
    /// on eviction; the caller owns matching <paramref name="rect"/> to the
    /// raster's real dimensions.
    /// </summary>
    public void Reset(DeviceRect rect, float scale, int pageVersion, RenderedBitmap pixels)
    {
        var expected = checked(rect.Width * rect.Height * 4);
        if (pixels.Rgba.Length != expected || pixels.Width != rect.Width || pixels.Height != rect.Height)
            throw new ArgumentException(
                $"Seed/strip pixels {pixels.Width}x{pixels.Height} do not match device rect {rect.Width}x{rect.Height}.");
        // Copy so the cache owns an independent buffer (callers may reuse/dispose).
        var owned = new byte[expected];
        Array.Copy(pixels.Rgba, owned, expected);
        _pixels = owned;
        _bounds = rect;
        _scale = scale;
        _pageVersion = pageVersion;
    }

    /// <summary>
    /// Copies <paramref name="src"/> (covering <paramref name="srcRect"/>) into
    /// <paramref name="dest"/> (covering <paramref name="destRect"/>), one row at a
    /// time respecting both strides. Both rects are in the same device-pixel space.
    /// </summary>
    private static void BlitInto(byte[] dest, int destWidth, byte[] src, DeviceRect srcRect, DeviceRect destRect)
    {
        var rowBytes = srcRect.Width * 4;
        var srcStride = srcRect.Width * 4;
        var destStride = destWidth * 4;
        var dx = srcRect.X - destRect.X;
        var dy = srcRect.Y - destRect.Y;
        for (var row = 0; row < srcRect.Height; row++)
        {
            var srcOffset = row * srcStride;
            var destOffset = ((dy + row) * destStride) + (dx * 4);
            Array.Copy(src, srcOffset, dest, destOffset, rowBytes);
        }
    }

    /// <summary>
    /// Page-coord rect → device-pixel rect. Origin rounds to the nearest whole
    /// device pixel; size uses <see cref="Math.Ceiling(double)"/> to match
    /// <c>ImageSharpBackend.Render</c>'s output dimensions exactly.
    /// </summary>
    public static DeviceRect ToDeviceRect(LayoutRect r, float scale)
    {
        var x = (int)Math.Round(r.X * scale);
        var y = (int)Math.Round(r.Y * scale);
        var w = (int)Math.Ceiling(r.Width * scale);
        var h = (int)Math.Ceiling(r.Height * scale);
        return new DeviceRect(x, y, w, h);
    }

    /// <summary>
    /// The parts of <paramref name="want"/> not covered by <paramref name="have"/>,
    /// as up to four axis-aligned strips (top, bottom, left, right). The split
    /// favors full-width top/bottom bands then the remaining left/right columns so
    /// adjacent scroll deltas produce one band, not a cross.
    /// </summary>
    private static List<DeviceRect> Subtract(DeviceRect want, DeviceRect have)
    {
        var strips = new List<DeviceRect>(4);

        // Top band: full width of `want` above `have`.
        if (want.Y < have.Y)
        {
            var h = Math.Min(have.Y, want.Bottom) - want.Y;
            strips.Add(new DeviceRect(want.X, want.Y, want.Width, h));
        }
        // Bottom band: full width of `want` below `have`.
        if (want.Bottom > have.Bottom)
        {
            var top = Math.Max(have.Bottom, want.Y);
            strips.Add(new DeviceRect(want.X, top, want.Width, want.Bottom - top));
        }

        // Middle vertical span shared with `have` — left/right columns there.
        var midTop = Math.Max(want.Y, have.Y);
        var midBottom = Math.Min(want.Bottom, have.Bottom);
        if (midBottom > midTop)
        {
            if (want.X < have.X)
            {
                var w = Math.Min(have.X, want.Right) - want.X;
                strips.Add(new DeviceRect(want.X, midTop, w, midBottom - midTop));
            }
            if (want.Right > have.Right)
            {
                var left = Math.Max(have.Right, want.X);
                strips.Add(new DeviceRect(left, midTop, want.Right - left, midBottom - midTop));
            }
        }

        return strips;
    }
}

/// <summary>
/// An integer device-pixel rectangle. Distinct from the page-coord
/// <see cref="Starling.Layout.Rect"/> (which is in CSS px, doubles); all
/// <see cref="PictureCache"/> pixel math is in this space.
/// </summary>
internal readonly record struct DeviceRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public bool Contains(DeviceRect o)
        => o.X >= X && o.Y >= Y && o.Right <= Right && o.Bottom <= Bottom;

    public bool Intersects(DeviceRect o)
        => X < o.Right && Right > o.X && Y < o.Bottom && Bottom > o.Y;

    public DeviceRect Union(DeviceRect o)
    {
        var x = Math.Min(X, o.X);
        var y = Math.Min(Y, o.Y);
        var right = Math.Max(Right, o.Right);
        var bottom = Math.Max(Bottom, o.Bottom);
        return new DeviceRect(x, y, right - x, bottom - y);
    }
}

/// <summary>
/// Describes a single row-by-row copy from a cached buffer into the output
/// bitmap: which sub-rect of the source to read and where in the destination it
/// lands. The destination is always the (0,0)-origin output for the requested
/// viewport.
/// </summary>
internal readonly record struct CacheBlit(
    byte[] SourcePixels,
    int SourceStride,
    int SourceX,
    int SourceY,
    int DestX,
    int DestY,
    int Width,
    int Height);
