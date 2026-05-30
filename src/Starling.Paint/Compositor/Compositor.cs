using Starling.Common.Diagnostics;
using Starling.Common.Image;
using Starling.Css.Values;
using Starling.Layout;
using Starling.Paint.Backend;
using Starling.Paint.Cache;
using LayoutRect = Starling.Layout.Rect;
using PaintList = Starling.Paint.DisplayList.DisplayList;

namespace Starling.Paint.Compositor;

/// <summary>
/// Paints a <see cref="CompositorLayer"/> tree into a single viewport bitmap.
/// Each layer's display-list slice is rasterized into its own layer-local bitmap
/// (served from the layer's <see cref="PictureCache"/> on a hit), then the layer
/// bitmaps are composited top-down with each layer's effective transform /
/// opacity / clip applied — composed with its ancestors'. Compositing is pure
/// managed: a layer bitmap is alpha-over blended into the output via an
/// inverse-mapped bilinear sample, so an upright layer raster lands rotated /
/// scaled exactly where its transform places it.
/// </summary>
internal sealed class Compositor
{
    private readonly IPaintBackend _backend;
    private readonly IDiagnostics _diag;

    public Compositor(IPaintBackend backend, IDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
        _diag = diagnostics ?? NoopDiagnostics.Instance;
    }

    /// <summary>
    /// Test-only switch (LTF-05): when set, every layer takes the general
    /// inverse-mapped bilinear composite path even when it would qualify for the
    /// fast integer-aligned blit. Used to prove the two paths are byte-identical.
    /// </summary>
    internal bool DisableFastBlit { get; init; }

    /// <summary>
    /// Forces the managed CPU blend even when a GPU is available
    /// (wp:M12-13-gpu-composite-blend). The composite path blends cached layers
    /// on the GPU by default — this switch pins the CPU path for the golden
    /// parity test and for hosts that opt out.
    /// </summary>
    internal bool DisableGpuBlend { get; init; }

    private GpuLayerCompositor? Gpu => DisableGpuBlend ? null : GpuLayerCompositor.Shared;

    /// <summary>
    /// Renders <paramref name="root"/> (the layer tree's root) into a bitmap
    /// sized to <paramref name="viewport"/> at <paramref name="scale"/>. Each
    /// layer's picture cache is keyed by its own slice content hash
    /// (<see cref="CompositorLayer.ContentHash"/>, LTF-02), so a layer whose
    /// content is unchanged serves from cache — even across a relayout that bumped
    /// the global page version — while a layer whose slice actually changed
    /// re-rasters alone, leaving its siblings untouched.
    /// </summary>
    public RenderedBitmap Render(CompositorLayer root, LayoutRect viewport, float scale)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (viewport.Width <= 0 || viewport.Height <= 0)
            throw new ArgumentException("Viewport must have positive dimensions.", nameof(viewport));
        if (!(scale > 0f))
            throw new ArgumentException("Scale must be positive.", nameof(scale));

        var width = (int)Math.Ceiling(viewport.Width * scale);
        var height = (int)Math.Ceiling(viewport.Height * scale);

        // The output base is opaque white — the page background the flat path
        // also establishes. Every layer paints over a transparent canvas, so
        // unpainted regions of a layer leave the base (or lower layers) showing.
        var output = new byte[checked(width * height * 4)];
        FillWhite(output);

        // Walk the tree once into a flat, paint-ordered list of blend ops, each
        // carrying the layer bitmap plus the geometry (localToDevice, clipDev,
        // opacity) computed in one place. The CPU and GPU paths consume the same
        // ops, so they blend identical geometry.
        var ops = new List<LayerBlend>();
        var keepAlive = new List<RenderedBitmap>();
        CollectOps(root, ops, keepAlive, viewport, scale,
            ancestorTransform: Matrix2D.Identity, ancestorOpacity: 1f, ancestorClip: null);

        // GPU blend (wp:M12-13): cached layer textures stay resident across
        // frames and blend in one pass. Falls back to the managed CPU blend when
        // no GPU adapter is present or a frame fails on the GPU.
        var blended = false;
        var gpu = Gpu;
        if (gpu is not null && ops.Count > 0)
            blended = gpu.Composite(output, width, height, ops);

        if (!blended)
        {
            // A failed GPU frame may have left partial pixels in the output; reset
            // to the white base before the CPU blend re-composites from scratch.
            if (gpu is not null) FillWhite(output);
            foreach (var op in ops)
                BlendOp(op, output, width, height, fastBlit: !DisableFastBlit);
        }

        foreach (var bmp in keepAlive)
            bmp.Dispose();

        return new RenderedBitmap(width, height, output);
    }

    private void CollectOps(
        CompositorLayer layer,
        List<LayerBlend> ops,
        List<RenderedBitmap> keepAlive,
        LayoutRect viewport,
        float scale,
        Matrix2D ancestorTransform,
        float ancestorOpacity,
        Rect? ancestorClip)
    {
        // Effective transform = ancestor × this layer's transform (post-multiply
        // so the ancestor's frame wraps the descendant, matching CSS Transforms
        // 1 §6.1 and the flat builder's nesting).
        var effectiveTransform = ancestorTransform.Multiply(layer.Transform);
        var effectiveOpacity = ancestorOpacity * layer.Opacity;

        // Clip is intersected in page space. The layer's own clip rect is in
        // page coords; ancestor clips were transformed into the same space as
        // they were intersected, so combine by plain rect intersection.
        var effectiveClip = IntersectClip(ancestorClip, layer.Clip);

        if (layer.Bounds.Width > 0 && layer.Bounds.Height > 0 && effectiveOpacity > 0f)
        {
            var local = RenderLayerBitmap(layer, scale);
            keepAlive.Add(local);

            // local-bitmap pixel -> output device pixel (see BlendOp for the
            // composed mapping). pageToDevice is shared; clipDev is the device
            // AABB of the page-space clip.
            var s = scale;
            var localToPage = Matrix2D.Translate(layer.Bounds.X, layer.Bounds.Y).Multiply(Matrix2D.Scale(1d / s, 1d / s));
            var pageToDevice = Matrix2D.Translate(-viewport.X * s, -viewport.Y * s).Multiply(Matrix2D.Scale(s, s));
            var localToDevice = pageToDevice.Multiply(effectiveTransform).Multiply(localToPage);
            Rect? clipDev = effectiveClip is { } cp
                ? TransformedAabb(new Rect(cp.X, cp.Y, cp.Width, cp.Height), pageToDevice)
                : null;

            ops.Add(new LayerBlend(local, layer.ContentHash, localToDevice, effectiveOpacity, clipDev));
        }

        // Children already in paint order (z-index sorted at build time).
        foreach (var child in layer.Children)
            CollectOps(child, ops, keepAlive, viewport, scale,
                effectiveTransform, effectiveOpacity, effectiveClip);
    }

    /// <summary>
    /// Rasterizes (or serves from cache) the layer's slice into a layer-local
    /// bitmap covering <see cref="CompositorLayer.Bounds"/> at <paramref name="scale"/>,
    /// over a transparent canvas. A cache hit reuses the prior bitmap without a
    /// backend call — this is what keeps an untouched sibling layer's pixels
    /// valid while another layer is repainted (bump its pageVersion).
    /// </summary>
    private RenderedBitmap RenderLayerBitmap(CompositorLayer layer, float scale)
    {
        // Key the cache by the slice's content hash, not the page version: a
        // transform/opacity-only frame (or an unrelated relayout elsewhere) leaves
        // this layer's hash unchanged → HIT; only a real content change re-rasters.
        var key = layer.ContentHash;
        var device = PictureCache.ToDeviceRect(layer.Bounds, scale);
        var w = Math.Max(1, device.Width);
        var h = Math.Max(1, device.Height);

        if (layer.Cache.TryServe(layer.Bounds, scale, key, out var hit))
            return CopyOut(hit, w, h);

        PaintList list = layer.Items;
        var painted = _backend.Render(list, layer.Bounds, scale, opaqueBackground: false);
        // Seed the cache against the raster's real device rect so subsequent
        // unchanged frames serve this layer from cache (HIT).
        var seedRect = PictureCache.ToDeviceRect(layer.Bounds, scale);
        var actual = ToDeviceRect(seedRect, painted.Width, painted.Height);
        layer.Cache.Reset(actual, scale, key, painted);
        return painted;
    }

    private static RenderedBitmap CopyOut(CacheBlit blit, int outWidth, int outHeight)
    {
        var buf = new byte[checked(outWidth * outHeight * 4)];
        var destStride = outWidth * 4;
        var rowBytes = blit.Width * 4;
        for (var row = 0; row < blit.Height; row++)
        {
            var srcOffset = ((blit.SourceY + row) * blit.SourceStride) + (blit.SourceX * 4);
            var destOffset = ((blit.DestY + row) * destStride) + (blit.DestX * 4);
            Array.Copy(blit.SourcePixels, srcOffset, buf, destOffset, rowBytes);
        }
        return new RenderedBitmap(outWidth, outHeight, buf);
    }

    private static DeviceRect ToDeviceRect(DeviceRect anchor, int width, int height)
        => new(anchor.X, anchor.Y, width, height);

    /// <summary>
    /// Managed alpha-over blend of one <see cref="LayerBlend"/> into
    /// <paramref name="output"/> — the CPU fallback for the GPU composite path.
    /// Inverse-maps each output device pixel back to a source sample using the
    /// op's precomputed <see cref="LayerBlend.LocalToDevice"/> so rotation /
    /// scaling are exact regardless of the transform.
    /// </summary>
    private static void BlendOp(LayerBlend op, byte[] output, int outWidth, int outHeight, bool fastBlit)
    {
        var local = op.Local;
        var localToDevice = op.LocalToDevice;
        var opacity = op.Opacity;
        var clipDev = op.ClipDevice;

        if (!TryInvert(localToDevice, out var deviceToLocal))
            return; // Degenerate (scale 0 / collapsed) transform paints nothing.

        // Device-space AABB of the transformed local bitmap, clamped to output.
        var srcRect = new Rect(0, 0, local.Width, local.Height);
        var devAabb = TransformedAabb(srcRect, localToDevice);

        // Fast path (LTF-05): a layer that lands as a pure integer-pixel
        // translation at full opacity (no rotation/scale/skew) skips the matrix
        // inverse + bilinear sample and blits source rows directly. The 1/scale
        // and scale factors cancel for an upright layer, so localToDevice's linear
        // part is exactly identity here and an integer-translation check suffices.
        // Byte-identical to the general path: a bilinear sample at integer
        // alignment returns the exact source pixel, and the same AlphaOver runs.
        if (fastBlit && opacity >= 1f && IsIntegerTranslation(localToDevice, out var tx, out var ty))
        {
            BlitIntegerAligned(local, output, outWidth, outHeight, tx, ty, clipDev);
            return;
        }

        var minX = Math.Max(0, (int)Math.Floor(devAabb.X));
        var minY = Math.Max(0, (int)Math.Floor(devAabb.Y));
        var maxX = Math.Min(outWidth, (int)Math.Ceiling(devAabb.Right));
        var maxY = Math.Min(outHeight, (int)Math.Ceiling(devAabb.Bottom));
        if (clipDev is { } cd)
        {
            minX = Math.Max(minX, (int)Math.Floor(cd.X));
            minY = Math.Max(minY, (int)Math.Floor(cd.Y));
            maxX = Math.Min(maxX, (int)Math.Ceiling(cd.Right));
            maxY = Math.Min(maxY, (int)Math.Ceiling(cd.Bottom));
        }

        var srcStride = local.Width * 4;
        var src = local.Rgba;
        var dstStride = outWidth * 4;

        for (var y = minY; y < maxY; y++)
        {
            for (var x = minX; x < maxX; x++)
            {
                // Sample at the pixel centre for stable mapping.
                var (sx, sy) = deviceToLocal.Transform(x + 0.5, y + 0.5);
                if (sx < 0 || sy < 0 || sx >= local.Width || sy >= local.Height) continue;

                Sample(src, srcStride, local.Width, local.Height, sx, sy, out var sr, out var sg, out var sb, out var sa);
                if (sa == 0) continue;

                var a = sa * opacity;
                if (a <= 0f) continue;

                var di = (y * dstStride) + (x * 4);
                AlphaOver(output, di, sr, sg, sb, a);
            }
        }
    }

    /// <summary>Bilinear sample of straight-alpha RGBA at fractional (sx, sy).</summary>
    private static void Sample(byte[] src, int stride, int w, int h, double sx, double sy,
        out float r, out float g, out float b, out float a)
    {
        var x0 = (int)Math.Floor(sx - 0.5);
        var y0 = (int)Math.Floor(sy - 0.5);
        var fx = (float)(sx - 0.5 - x0);
        var fy = (float)(sy - 0.5 - y0);
        var x1 = x0 + 1;
        var y1 = y0 + 1;
        x0 = Math.Clamp(x0, 0, w - 1);
        x1 = Math.Clamp(x1, 0, w - 1);
        y0 = Math.Clamp(y0, 0, h - 1);
        y1 = Math.Clamp(y1, 0, h - 1);

        // Premultiply before interpolation so a transparent neighbour doesn't
        // leak colour into the edge.
        Premul(src, (y0 * stride) + x0 * 4, out var r00, out var g00, out var b00, out var a00);
        Premul(src, (y0 * stride) + x1 * 4, out var r10, out var g10, out var b10, out var a10);
        Premul(src, (y1 * stride) + x0 * 4, out var r01, out var g01, out var b01, out var a01);
        Premul(src, (y1 * stride) + x1 * 4, out var r11, out var g11, out var b11, out var a11);

        var w00 = (1 - fx) * (1 - fy);
        var w10 = fx * (1 - fy);
        var w01 = (1 - fx) * fy;
        var w11 = fx * fy;

        var pr = r00 * w00 + r10 * w10 + r01 * w01 + r11 * w11;
        var pg = g00 * w00 + g10 * w10 + g01 * w01 + g11 * w11;
        var pb = b00 * w00 + b10 * w10 + b01 * w01 + b11 * w11;
        var pa = a00 * w00 + a10 * w10 + a01 * w01 + a11 * w11;

        a = pa;
        if (pa > 0f)
        {
            r = pr / pa;
            g = pg / pa;
            b = pb / pa;
        }
        else
        {
            r = g = b = 0f;
        }
    }

    private static void Premul(byte[] src, int i, out float r, out float g, out float b, out float a)
    {
        a = src[i + 3] / 255f;
        r = src[i] / 255f * a;
        g = src[i + 1] / 255f * a;
        b = src[i + 2] / 255f * a;
    }

    /// <summary>Straight-alpha source over straight-alpha destination, in place.</summary>
    private static void AlphaOver(byte[] dst, int i, float sr, float sg, float sb, float sa)
    {
        var da = dst[i + 3] / 255f;
        var outA = sa + da * (1 - sa);
        if (outA <= 0f)
        {
            dst[i] = dst[i + 1] = dst[i + 2] = dst[i + 3] = 0;
            return;
        }
        var dr = dst[i] / 255f;
        var dg = dst[i + 1] / 255f;
        var db = dst[i + 2] / 255f;
        var or = (sr * sa + dr * da * (1 - sa)) / outA;
        var og = (sg * sa + dg * da * (1 - sa)) / outA;
        var ob = (sb * sa + db * da * (1 - sa)) / outA;
        dst[i] = ToByte(or);
        dst[i + 1] = ToByte(og);
        dst[i + 2] = ToByte(ob);
        dst[i + 3] = ToByte(outA);
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

    private static void FillWhite(byte[] buf)
    {
        for (var i = 0; i < buf.Length; i++)
            buf[i] = 255;
    }

    private static Rect? IntersectClip(Rect? a, Rect? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        var x = Math.Max(a.Value.X, b.Value.X);
        var y = Math.Max(a.Value.Y, b.Value.Y);
        var right = Math.Min(a.Value.Right, b.Value.Right);
        var bottom = Math.Min(a.Value.Bottom, b.Value.Bottom);
        return new Rect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }

    private static Rect TransformedAabb(Rect r, Matrix2D m)
    {
        var (x0, y0) = m.Transform(r.X, r.Y);
        var (x1, y1) = m.Transform(r.X + r.Width, r.Y);
        var (x2, y2) = m.Transform(r.X + r.Width, r.Y + r.Height);
        var (x3, y3) = m.Transform(r.X, r.Y + r.Height);
        var minX = Math.Min(Math.Min(x0, x1), Math.Min(x2, x3));
        var minY = Math.Min(Math.Min(y0, y1), Math.Min(y2, y3));
        var maxX = Math.Max(Math.Max(x0, x1), Math.Max(x2, x3));
        var maxY = Math.Max(Math.Max(y0, y1), Math.Max(y2, y3));
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// True when <paramref name="m"/> is a pure integer-pixel translation (identity
    /// linear part, integer offsets), the precondition for the LTF-05 fast blit.
    /// </summary>
    private static bool IsIntegerTranslation(Matrix2D m, out int dx, out int dy)
    {
        const double eps = 1e-6;
        dx = 0; dy = 0;
        if (Math.Abs(m.A - 1d) > eps || Math.Abs(m.D - 1d) > eps
            || Math.Abs(m.B) > eps || Math.Abs(m.C) > eps)
            return false;
        var rx = Math.Round(m.E);
        var ry = Math.Round(m.F);
        if (Math.Abs(m.E - rx) > eps || Math.Abs(m.F - ry) > eps)
            return false;
        dx = (int)rx;
        dy = (int)ry;
        return true;
    }

    /// <summary>
    /// Blits <paramref name="local"/> into <paramref name="output"/> offset by the
    /// integer device translation (<paramref name="dx"/>, <paramref name="dy"/>),
    /// clamped to the output and to <paramref name="clipDev"/>. Opaque source
    /// pixels copy straight through; partially-transparent pixels alpha-over —
    /// the same blend the general path applies, so the result is byte-identical.
    /// </summary>
    private static void BlitIntegerAligned(RenderedBitmap local, byte[] output, int outWidth, int outHeight, int dx, int dy, Rect? clipDev)
    {
        var minX = Math.Max(0, dx);
        var minY = Math.Max(0, dy);
        var maxX = Math.Min(outWidth, dx + local.Width);
        var maxY = Math.Min(outHeight, dy + local.Height);
        if (clipDev is { } cd)
        {
            minX = Math.Max(minX, (int)Math.Floor(cd.X));
            minY = Math.Max(minY, (int)Math.Floor(cd.Y));
            maxX = Math.Min(maxX, (int)Math.Ceiling(cd.Right));
            maxY = Math.Min(maxY, (int)Math.Ceiling(cd.Bottom));
        }
        if (maxX <= minX || maxY <= minY) return;

        var src = local.Rgba;
        var srcStride = local.Width * 4;
        var dstStride = outWidth * 4;
        for (var y = minY; y < maxY; y++)
        {
            var srcRow = (y - dy) * srcStride;
            var dstRow = y * dstStride;
            for (var x = minX; x < maxX; x++)
            {
                var si = srcRow + (x - dx) * 4;
                var sa = src[si + 3];
                if (sa == 0) continue;
                var di = dstRow + (x * 4);
                if (sa == 255)
                {
                    output[di] = src[si];
                    output[di + 1] = src[si + 1];
                    output[di + 2] = src[si + 2];
                    output[di + 3] = 255;
                }
                else
                {
                    AlphaOver(output, di, src[si] / 255f, src[si + 1] / 255f, src[si + 2] / 255f, sa / 255f);
                }
            }
        }
    }

    /// <summary>Inverts a 2D affine <see cref="Matrix2D"/>; false if singular.</summary>
    private static bool TryInvert(Matrix2D m, out Matrix2D inverse)
    {
        var det = m.A * m.D - m.B * m.C;
        if (Math.Abs(det) < 1e-12)
        {
            inverse = Matrix2D.Identity;
            return false;
        }
        var invDet = 1d / det;
        var a = m.D * invDet;
        var b = -m.B * invDet;
        var c = -m.C * invDet;
        var d = m.A * invDet;
        var e = -(a * m.E + c * m.F);
        var f = -(b * m.E + d * m.F);
        inverse = new Matrix2D(a, b, c, d, e, f);
        return true;
    }
}
