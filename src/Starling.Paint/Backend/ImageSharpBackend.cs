using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Text;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Tessera.Common.Diagnostics;
using Tessera.Paint.Interop;
using Tessera.Common.Image;
using Tessera.Css.Values;
using Tessera.Layout.Text;
using LayoutRect = Tessera.Layout.Rect;
using LayoutSize = Tessera.Layout.Size;
using PaintList = Tessera.Paint.DisplayList.DisplayList;

namespace Tessera.Paint.Backend;

/// <summary>
/// Cross-platform paint backend that replays a <see cref="DisplayList"/> through
/// ImageSharp.Drawing 3.0's canvas API. This is the sole paint backend after the
/// Skia/Graphite native shim was removed — the engine is once again pure-managed
/// end-to-end. Supports two destinations: the default parallel-SIMD CPU
/// rasterizer (<c>Image&lt;Rgba32&gt;</c>) and the GPU compute-shader
/// Vello-style pipeline in <see cref="WebGPURenderTarget"/>, selected via
/// <c>TESSERA_PAINT_BACKEND=imagesharp-webgpu</c>. The per-item dispatch is
/// identical because both paths drive the same <see cref="DrawingCanvas"/> API.
/// </summary>
/// <remarks>
/// <para>
/// The starting canvas is filled opaque white so a fresh render lands on a
/// predictable background regardless of viewport size.
/// </para>
/// <para>
/// Text path: ImageSharp.Drawing 3.0 does not expose a public way to paint a
/// caller-supplied (pre-shaped) glyph run — all text APIs route through the
/// internal text renderer, which re-shapes via SixLabors.Fonts.
/// So the backend ignores <c>DrawText.Shaped</c> and re-shapes via
/// <see cref="RichTextOptions"/>. Goldens between Skia and ImageSharp will
/// diverge in glyph positioning at sub-pixel resolution; cross-backend tests
/// must compare via SSIM rather than pixel identity.
/// </para>
/// <para>
/// Fonts: <see cref="FontResolver"/> only exposes Skia handles, not raw bytes.
/// To avoid changing the resolver, the backend loads every <c>*.ttf</c>/<c>*.otf</c>
/// embedded resource on <c>Starling.Paint.dll</c> into a private
/// <see cref="FontCollection"/> once at construction. Family lookup walks the
/// <see cref="FontSpec.Families"/> list against that collection and falls back
/// to the first registered family (the bundled OpenSans) when nothing matches.
/// </para>
/// </remarks>
internal sealed class ImageSharpBackend : IPaintBackend
{
    private readonly FontResolver _fonts;
    private readonly FontFaceRegistry? _webFonts;
    private readonly IDiagnostics _diag;
    private readonly bool _useWebGpu;
    private readonly FontCollection _fontCollection;
    private readonly ConcurrentDictionary<FontCacheKey, Font> _fontCache = new();

    public ImageSharpBackend(FontResolver fonts, FontFaceRegistry? webFonts, IDiagnostics? diagnostics = null, bool useWebGpu = false)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        _fonts = fonts;
        _webFonts = webFonts;
        _diag = diagnostics ?? NoopDiagnostics.Instance;
        _useWebGpu = useWebGpu;
        _fontCollection = ImageSharpFontLookup.LoadCollection(webFonts);
    }

    public string Name => _useWebGpu ? "imagesharp-webgpu" : "imagesharp";

    public RenderedBitmap Render(PaintList list, LayoutSize viewport, float scale = 1.0f)
    {
        ArgumentNullException.ThrowIfNull(list);
        if (viewport.Width <= 0 || viewport.Height <= 0)
            throw new ArgumentException("Viewport must have positive dimensions.", nameof(viewport));
        if (!(scale > 0.0f))
            throw new ArgumentException("Scale must be positive.", nameof(scale));

        var width = (int)Math.Ceiling(viewport.Width * scale);
        var height = (int)Math.Ceiling(viewport.Height * scale);

        Activity.Current?.SetTag("raster.width", width);
        Activity.Current?.SetTag("raster.height", height);
        Activity.Current?.SetTag("raster.scale", scale);
        Activity.Current?.SetTag("raster.items", list.Items.Count);

        return _useWebGpu
            ? RenderWebGpu(list, width, height, scale)
            : RenderCpu(list, width, height, scale);
    }

    private RenderedBitmap RenderCpu(PaintList list, int width, int height, float scale)
    {
        using (_diag.Span("paint", "raster.context_init"))
        {
            // ImageSharp has no persistent CPU context; the span is kept so
            // the diagnostics trace shape stays stable for tooling.
        }

        Image<Rgba32> image;
        using (_diag.Span("paint", "raster.surface_alloc"))
            image = new Image<Rgba32>(width, height, new Rgba32(255, 255, 255, 255));

        // Image sources used by DrawImage outlive the canvas closure: the
        // DrawingCanvas records commands and rasterizes them when the
        // Mutate/Paint scope unwinds, so disposing image sources inside the
        // closure throws ObjectDisposedException from ImageBrushRenderer.
        // Stage them in a list and dispose after the mutation completes.
        var pendingImageSources = new List<IDisposable>();

        try
        {
            using (_diag.Span("paint", "raster.command_record"))
            {
                image.Mutate(x => x.Paint(canvas =>
                {
                    var transforms = new TransformStack();
                    foreach (var item in list.Items)
                        Apply(canvas, item, scale, pendingImageSources, transforms);
                }));
            }

            byte[] pixels;
            using (_diag.Span("paint", "raster.readback"))
            {
                pixels = new byte[checked(width * height * 4)];
                image.CopyPixelDataTo(pixels);
            }

            return new RenderedBitmap(width, height, pixels);
        }
        finally
        {
            foreach (var src in pendingImageSources) src.Dispose();
            image.Dispose();
        }
    }

    // GPU path: WebGPU compute-shader (Vello-style) rasterizer in
    // SixLabors.ImageSharp.Drawing.WebGPU. WebGPURenderTarget allocates an
    // offscreen surface on the shared process device and exposes the same
    // DrawingCanvas the CPU path uses, so Apply(canvas, item, scale) below is
    // shared verbatim. The starting canvas is cleared to opaque white via a
    // full-bounds Fill so the white-background invariant matches Skia.
    private RenderedBitmap RenderWebGpu(PaintList list, int width, int height, float scale)
    {
        // Probe the WebGPU environment before constructing a render target so a
        // missing/broken wgpu-native binary, no compatible GPU adapter, or a
        // sandboxed Catalyst process surfaces as a clear actionable error
        // rather than the generic "failed to initialize webgpu runtime" the
        // constructor would otherwise throw.
        var probe = WebGPUEnvironment.ProbeAvailability();
        if (probe != WebGPUEnvironmentError.Success)
            throw new InvalidOperationException(
                $"WebGPU paint backend requested via TESSERA_PAINT_BACKEND=imagesharp-webgpu, " +
                $"but WebGPUEnvironment.ProbeAvailability returned {probe}. " +
                Environment.NewLine + Environment.NewLine +
                "Native loader trail (Starling.Paint.Interop.WgpuNativeLoader):" + Environment.NewLine +
                "  " + WgpuNativeLoader.Diagnose() +
                Environment.NewLine + Environment.NewLine +
                "Common causes: the wgpu-native dylib was not copied into the runtime layout " +
                "(check runtimes/<rid>/native/libwgpu_native.{dylib,so} in the app bundle), " +
                "no compatible GPU adapter is visible to the process, or a sandbox blocks " +
                "Metal/Vulkan/D3D12 access. Fall back to TESSERA_PAINT_BACKEND=imagesharp " +
                "(CPU) or =skia.");

        WebGPURenderTarget target;
        using (_diag.Span("paint", "raster.context_init"))
            target = new WebGPURenderTarget(width, height);

        try
        {
            DrawingCanvas canvas;
            using (_diag.Span("paint", "raster.surface_alloc"))
                canvas = target.CreateCanvas();

            var pendingImageSources = new List<IDisposable>();
            try
            {
                using (_diag.Span("paint", "raster.command_record"))
                {
                    canvas.Fill(Brushes.Solid(Color.White), new Rectangle(0, 0, width, height));
                    var transforms = new TransformStack();
                    foreach (var item in list.Items)
                        Apply(canvas, item, scale, pendingImageSources, transforms);
                    // Flush seals queued commands into the canvas timeline so
                    // the GPU pipeline executes before ReadbackImage samples
                    // the texture. Without it, readback races the (un)submitted
                    // command buffer and returns the initial clear color.
                    canvas.Flush();
                }
            }
            finally
            {
                canvas.Dispose();
                foreach (var src in pendingImageSources) src.Dispose();
            }

            byte[] pixels;
            using (_diag.Span("paint", "raster.readback"))
            {
                using var image = target.ReadbackImage<Rgba32>();
                pixels = new byte[checked(width * height * 4)];
                image.CopyPixelDataTo(pixels);
            }

            return new RenderedBitmap(width, height, pixels);
        }
        finally
        {
            target.Dispose();
        }
    }

    private void Apply(DrawingCanvas canvas, DisplayList.DisplayItem item, float scale, List<IDisposable> pendingImageSources, TransformStack transforms)
    {
        switch (item)
        {
            case DisplayList.PushTransform push:
                transforms.Push(push.Matrix);
                _diag.Counter("paint.push_transform", 1);
                break;
            case DisplayList.PopTransform:
                transforms.Pop();
                _diag.Counter("paint.pop_transform", 1);
                break;
            case DisplayList.FillRect fill:
                if (fill.Bounds.Width <= 0 || fill.Bounds.Height <= 0) return;
                _diag.Counter("paint.fill_rect", 1);
                if (transforms.IsIdentity)
                    canvas.Fill(Brushes.Solid(ToColor(fill.Color)), ToDeviceRect(fill.Bounds, scale));
                else
                    canvas.Fill(Brushes.Solid(ToColor(fill.Color)), BuildTransformedRectPath(fill.Bounds, scale, transforms.Current));
                break;
            case DisplayList.StrokeRect stroke:
                _diag.Counter("paint.stroke_rect", 1);
                {
                    var penWidth = (float)stroke.Width * scale;
                    var pen = Pens.Solid(ToColor(stroke.Color), penWidth);
                    if (transforms.IsIdentity)
                    {
                        var r = ToDeviceRectF(stroke.Bounds, scale);
                        canvas.Draw(pen, new RectanglePolygon(r));
                    }
                    else
                    {
                        canvas.Draw(pen, BuildTransformedRectPath(stroke.Bounds, scale, transforms.Current));
                    }
                }
                break;
            case DisplayList.DrawText text:
                _diag.Counter("paint.draw_text", 1);
                DrawText(canvas, text, scale, transforms);
                break;
            case DisplayList.DrawImage img:
                _diag.Counter("paint.draw_image", 1);
                // Transformed <img> elements paint at their untransformed
                // bounds — DrawingCanvas.DrawImage takes no DrawingOptions and
                // ImageSharp's AffineTransformBuilder path would require
                // re-rasterising the source per frame. Logged so a real
                // dependency on transformed images surfaces in metrics.
                if (!transforms.IsIdentity) _diag.Counter("paint.draw_image.transform_skipped", 1);
                DrawImage(canvas, img, scale, pendingImageSources);
                break;
        }
    }

    private void DrawText(DrawingCanvas canvas, DisplayList.DrawText text, float scale, TransformStack transforms)
    {
        if (string.IsNullOrEmpty(text.Text)) return;

        var spec = FontSpecFromDrawText(text);
        var size = (float)text.FontSize * scale;
        var probe = FirstCodepoint(text.Text);
        var font = ResolveFont(spec, probe, size);
        var color = ToColor(text.Color);

        var originX = (float)text.X * scale;
        var baselineY = SnapToPixel((float)text.Y, scale) * scale;

        // No pre-shaped path: ImageSharp.Drawing 3.0 does not accept
        // caller-shaped glyph runs through its public canvas surface, so the
        // text.Shaped fast path is intentionally bypassed. See the class
        // doc-comment for why and what this means for cross-backend diffs.
        if (text.Shaped is { Glyphs.Length: > 0 })
            _diag.Counter("paint.draw_text.reshaped", 1);
        else
            _diag.Counter("paint.draw_text.unshaped", 1);

        if (transforms.IsIdentity)
        {
            var richOptions = new RichTextOptions(font)
            {
                Origin = new PointF(originX, baselineY),
                TextAlignment = TextAlignment.Start,
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            canvas.DrawText(richOptions, text.Text, Brushes.Solid(color), pen: null);
            return;
        }

        // Transformed text: generate per-glyph outlines at the un-transformed
        // origin, then apply the (CSS-space) transform composed with the
        // device-scale matrix to each glyph's path. DrawGlyphs replays the
        // transformed outlines without re-running the text shaper.
        var textOptions = new TextOptions(font)
        {
            Origin = new PointF(originX, baselineY),
            TextAlignment = TextAlignment.Start,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        var glyphs = TextBuilder.GenerateGlyphs(text.Text, textOptions);
        // The display-list transform is in CSS px space; primitives are
        // emitted in CSS px and scaled to device px at the call site. Glyphs
        // here are already in device px (Origin was scaled), so we apply just
        // the transform — but the transform's translate components are in
        // CSS px and need to be scaled too. Build a "device-space" version
        // of the current matrix: scale only the (e,f) translation by `scale`,
        // leaving (a,b,c,d) intact since they are dimensionless.
        var deviceMatrix = transforms.CurrentInDeviceSpace(scale);
        var brush = Brushes.Solid(color);
        foreach (var glyph in glyphs)
        {
            var transformed = glyph.Transform(deviceMatrix);
            foreach (var path in transformed.PathList)
                canvas.Fill(brush, path);
        }
    }

    /// <summary>
    /// Builds a 4-point polygon for the axis-aligned <paramref name="rect"/>
    /// in CSS px, transforms each corner through <paramref name="cssMatrix"/>
    /// (still in CSS px space), then scales the result into device-pixel
    /// space for submission to the canvas. Used by the transform-aware Fill
    /// / Draw paths when the current transform stack is non-identity.
    /// </summary>
    private static Polygon BuildTransformedRectPath(LayoutRect rect, float scale, Matrix2D cssMatrix)
    {
        var s = scale > 0 ? scale : 1f;
        var (x0, y0) = cssMatrix.Transform(rect.X, rect.Y);
        var (x1, y1) = cssMatrix.Transform(rect.X + rect.Width, rect.Y);
        var (x2, y2) = cssMatrix.Transform(rect.X + rect.Width, rect.Y + rect.Height);
        var (x3, y3) = cssMatrix.Transform(rect.X, rect.Y + rect.Height);
        return new Polygon(new LinearLineSegment(
            new PointF((float)(x0 * s), (float)(y0 * s)),
            new PointF((float)(x1 * s), (float)(y1 * s)),
            new PointF((float)(x2 * s), (float)(y2 * s)),
            new PointF((float)(x3 * s), (float)(y3 * s))));
    }

    private static void DrawImage(DrawingCanvas canvas, DisplayList.DrawImage item, float scale, List<IDisposable> pendingImageSources)
    {
        if (item.Bounds.Width <= 0 || item.Bounds.Height <= 0) return;
        var decoded = item.Source;
        if (decoded is null || decoded.Width <= 0 || decoded.Height <= 0) return;

        // ImageSharp's LoadPixelData<TPixel> wants a ReadOnlySpan<byte> of the
        // exact byte length; DecodedImage's buffer is straight-alpha RGBA8888
        // which matches Rgba32's layout precisely.
        var src = Image.LoadPixelData<Rgba32>(decoded.Pixels.Span, decoded.Width, decoded.Height);
        pendingImageSources.Add(src);
        // DrawingCanvas.DrawImage signature is (image, sourceRect, destinationRect, resampler) —
        // sourceRect is the integer crop inside the source image and
        // destinationRect is the float-precision target on the canvas.
        var sourceRect = new SixLabors.ImageSharp.Rectangle(0, 0, decoded.Width, decoded.Height);
        var destRect = ToDeviceRectF(item.Bounds, scale);
        canvas.DrawImage(src, sourceRect, destRect, KnownResamplers.Bicubic);
    }

    private readonly record struct FontCacheKey(FontSpec Spec, float Size, int Probe);

    private static FontSpec FontSpecFromDrawText(DisplayList.DrawText text)
        => new(text.FontFamilies, text.Bold, text.Italic);

    private static int FirstCodepoint(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        if (char.IsHighSurrogate(text[0]) && text.Length > 1 && char.IsLowSurrogate(text[1]))
            return char.ConvertToUtf32(text[0], text[1]);
        return text[0];
    }

    private Font ResolveFont(FontSpec spec, int probeCodepoint, float size)
        => _fontCache.GetOrAdd(new FontCacheKey(spec, size, probeCodepoint), k => CreateFont(k.Spec, k.Size));

    private Font CreateFont(FontSpec spec, float size)
        => ImageSharpFontLookup.CreateFont(_fontCollection, spec, size);

    // ImageSharp.Drawing 3.0 dropped canvas-level transforms (no Scale/Translate),
    // so layout-space coordinates are pre-multiplied by `scale` at the call site.
    // SnapRect rounds in the post-scale (device-pixel) space and returns the
    // device-pixel rect directly.
    private static SixLabors.ImageSharp.RectangleF ToDeviceRectF(LayoutRect r, float scale)
    {
        var s = scale > 0 ? scale : 1f;
        var x0 = (float)Math.Round(r.X * s);
        var y0 = (float)Math.Round(r.Y * s);
        var x1 = (float)Math.Round((r.X + r.Width) * s);
        var y1 = (float)Math.Round((r.Y + r.Height) * s);
        return new SixLabors.ImageSharp.RectangleF(x0, y0, x1 - x0, y1 - y0);
    }

    private static SixLabors.ImageSharp.Rectangle ToDeviceRect(LayoutRect r, float scale)
    {
        var s = scale > 0 ? scale : 1f;
        var x0 = (int)Math.Round(r.X * s);
        var y0 = (int)Math.Round(r.Y * s);
        var x1 = (int)Math.Round((r.X + r.Width) * s);
        var y1 = (int)Math.Round((r.Y + r.Height) * s);
        return new SixLabors.ImageSharp.Rectangle(x0, y0, x1 - x0, y1 - y0);
    }

    private static float SnapToPixel(float value, float scale)
    {
        var s = scale > 0 ? scale : 1f;
        return MathF.Round(value * s) / s;
    }

    private static Color ToColor(CssColor c)
        => Color.FromPixel(new Rgba32(c.R, c.G, c.B, c.A));

    public void Dispose()
    {
        // Font is a value-ish handle; FontCollection has no Dispose. Clear
        // the cache so a long-lived host releases the per-spec entries.
        _fontCache.Clear();
        _ = _fonts;
        _ = _webFonts;
    }

    /// <summary>
    /// Per-render transform stack for the CSS <c>transform</c> property.
    /// ImageSharp.Drawing 3's <see cref="DrawingCanvas"/> has no built-in
    /// transform ops (no Save(matrix)/Scale/Translate/Concat), so the backend
    /// keeps its own stack of <see cref="Matrix2D"/> in CSS px and threads
    /// the composed current matrix into every transformed primitive. The
    /// matrix on the bottom of the stack is identity; <see cref="Push"/>
    /// post-multiplies so nested transforms compose left-to-right
    /// (CSS Transforms 1 §6.1: <c>transform: A B</c> applies B then A,
    /// equivalently the outer ancestor's transform "wraps" descendants).
    /// </summary>
    private sealed class TransformStack
    {
        private readonly Stack<Matrix2D> _stack = new();
        public TransformStack() { _stack.Push(Matrix2D.Identity); }
        public Matrix2D Current => _stack.Peek();
        public bool IsIdentity => Current.IsIdentity;
        public void Push(Matrix2D m) => _stack.Push(Current.Multiply(m));
        public void Pop()
        {
            // Defensive: a stray Pop without a matching Push would underflow
            // and corrupt subsequent primitive coordinates. Builder always
            // emits balanced pairs; treat extras as no-ops.
            if (_stack.Count > 1) _stack.Pop();
        }

        /// <summary>
        /// Returns the current CSS-space matrix lifted to <see cref="Matrix4x4"/>
        /// with the translate components scaled by <paramref name="scale"/>
        /// so it composes with primitives whose origin is already in device
        /// pixels (the DrawText path, where the glyph layout is generated at
        /// the device-pixel baseline).
        /// </summary>
        public Matrix4x4 CurrentInDeviceSpace(float scale)
        {
            var m = Current;
            var s = scale > 0 ? scale : 1f;
            // Row 0: (a, b, 0, 0); Row 1: (c, d, 0, 0); Row 2: identity;
            // Row 3: (e*s, f*s, 0, 1) — translate scaled to device px.
            return new Matrix4x4(
                (float)m.A, (float)m.B, 0f, 0f,
                (float)m.C, (float)m.D, 0f, 0f,
                0f,         0f,         1f, 0f,
                (float)(m.E * s), (float)(m.F * s), 0f, 1f);
        }
    }
}
