using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Starling.Common.Diagnostics;
using Starling.Common.Image;
using Starling.Css.Values;
using Starling.Layout.Text;
using Starling.Paint.DisplayList;
using Starling.Paint.Interop;
using LayoutRect = Starling.Layout.Rect;
using LayoutSize = Starling.Layout.Size;
using PaintList = Starling.Paint.DisplayList.DisplayList;

namespace Starling.Paint.Backend;

/// <summary>
/// Cross-platform paint backend that replays a <see cref="DisplayList"/> through
/// ImageSharp.Drawing's canvas API. Supports two destinations: the CPU
/// rasterizer (<c>Image&lt;Rgba32&gt;</c>) and the WebGPU render target in
/// <see cref="WebGPURenderTarget"/>, selected via
/// <c>STARLING_PAINT_BACKEND=imagesharp-webgpu</c>. Both destinations consume
/// the same display-list replay because each exposes a <see cref="DrawingCanvas"/>.
/// </summary>
/// <remarks>
/// <para>
/// The starting canvas is filled opaque white so a fresh render lands on a
/// predictable background regardless of viewport size.
/// </para>
/// <para>
/// Text renders through <see cref="TextBlock"/> so SixLabors owns glyph
/// painting, including layered and color glyphs. When layout supplied an
/// ImageSharp-shaped run, the backend reuses its prepared block; otherwise it
/// creates a block from the resolved font at paint time.
/// </para>
/// <para>
/// Fonts: the backend snapshots a private <see cref="FontCollection"/> at
/// construction time. It contains the bundled fonts, any registered web fonts,
/// and system fonts when the host exposes them. Family lookup walks
/// <see cref="FontSpec.Families"/> against that collection and falls back to
/// the first registered family when nothing matches.
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

        if (_useWebGpu && (width > MaxWebGpuTextureDimension || height > MaxWebGpuTextureDimension))
        {
            // wgpu's default uncaptured-error handler is a Rust panic that
            // calls abort(), so a CreateTexture call exceeding
            // maxTextureDimension2D crashes the entire process before any
            // C# try/catch can intercept it. Real pages (e.g. tall scrolling
            // articles like netclaw.dev) routinely exceed the WebGPU spec
            // default of 8192 px in the long axis, so guard the GPU path and
            // fall back to the pure-managed CPU rasterizer for this frame
            // rather than aborting. The GPU path remains available for the
            // next frame if the page reflows back under the limit.
            _diag.Counter("paint.webgpu.fallback_cpu.oversize", 1);
            Activity.Current?.SetTag("raster.webgpu.fallback_reason", "exceeds_max_texture_dimension");
            return RenderCpu(list, width, height, scale, webGpuFallbackReason: "exceeds_max_texture_dimension");
        }

        return _useWebGpu
            ? RenderWebGpu(list, width, height, scale)
            : RenderCpu(list, width, height, scale);
    }

    // 8192 is WebGPU's guaranteed minimum maxTextureDimension2D. Larger
    // surfaces can fail texture creation on constrained adapters, and wgpu's
    // default uncaptured-error handler aborts the process before C# can catch
    // anything, so detect the overflow before allocating a texture.
    private const int MaxWebGpuTextureDimension = 8192;

    private RenderedBitmap RenderCpu(PaintList list, int width, int height, float scale, string? webGpuFallbackReason = null)
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
                Activity.Current?.SetTag("raster.items", list.Items.Count);
                if (webGpuFallbackReason is not null)
                    Activity.Current?.SetTag("raster.webgpu.fallback_reason", webGpuFallbackReason);
                image.Mutate(x => x.Paint(canvas =>
                {
                    var transforms = new Stack<Matrix2D>();
                    transforms.Push(Matrix2D.Identity);

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

    // GPU path: WebGPURenderTarget allocates an offscreen surface and exposes
    // the same DrawingCanvas API as the CPU path, so display-list replay stays
    // shared. The target starts transparent, so clear it to opaque white to
    // match the CPU image allocation.
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
                $"WebGPU paint backend requested via STARLING_PAINT_BACKEND=imagesharp-webgpu, " +
                $"but WebGPUEnvironment.ProbeAvailability returned {probe}. " +
                Environment.NewLine + Environment.NewLine +
                "Native loader trail (Starling.Paint.Interop.WgpuNativeLoader):" + Environment.NewLine +
                "  " + WgpuNativeLoader.Diagnose() +
                Environment.NewLine + Environment.NewLine +
                "Common causes: the wgpu-native dylib was not copied into the runtime layout " +
                "(check runtimes/<rid>/native/libwgpu_native.{dylib,so} in the app bundle), " +
                "no compatible GPU adapter is visible to the process, or a sandbox blocks " +
                "Metal/Vulkan/D3D12 access. Fall back to STARLING_PAINT_BACKEND=imagesharp " +
                "to use the pure-CPU rasterizer.");

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
                    Activity.Current?.SetTag("raster.items", list.Items.Count);
                    canvas.Clear(Brushes.Solid(Color.White), new Rectangle(0, 0, width, height));
                    var transforms = new Stack<Matrix2D>();
                    transforms.Push(Matrix2D.Identity);

                    foreach (var item in list.Items)
                        Apply(canvas, item, scale, pendingImageSources, transforms);
                    // Flush seals queued commands into the canvas timeline so
                    // the GPU pipeline executes before ReadbackImage samples
                    // the texture. Without it, readback races the (un)submitted
                    // command buffer and returns the initial clear color.
                    using (_diag.Span("paint", "raster.flush"))
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

    private void Apply(DrawingCanvas canvas, DisplayList.DisplayItem item, float scale, List<IDisposable> pendingImageSources, Stack<Matrix2D> transforms)
    {
        switch (item)
        {
            case DisplayList.PushTransform push:
                var matrix = transforms.Peek().Multiply(push.Matrix);
                transforms.Push(matrix);
                canvas.Save(new DrawingOptions
                {
                    Transform = ToDeviceMatrix(matrix, scale),
                }, []);

                _diag.Counter("paint.push_transform", 1);
                break;
            case DisplayList.PopTransform:
                canvas.Restore();
                transforms.Pop();
                _diag.Counter("paint.pop_transform", 1);
                break;
            case DisplayList.FillRect fill:
                if (fill.Bounds.Width <= 0 || fill.Bounds.Height <= 0) return;
                _diag.Counter("paint.fill_rect", 1);
                var rectPath = fill.PixelAlignment == FillRectPixelAlignment.SnapToDevicePixels
                    ? ToSnappedDeviceRectPath(fill.Bounds, scale)
                    : ToDeviceRectPath(fill.Bounds, scale);

                canvas.Fill(Brushes.Solid(ToColor(fill.Color)), rectPath);
                break;
            case DisplayList.StrokeRect stroke:
                _diag.Counter("paint.stroke_rect", 1);
                {
                    var penWidth = (float)stroke.Width * scale;
                    var pen = Pens.Solid(ToColor(stroke.Color), penWidth);
                    var r = ToDeviceRectPath(stroke.Bounds, scale);
                    canvas.Draw(pen, r);
                }
                break;
            case DisplayList.DrawText text:
                _diag.Counter("paint.draw_text", 1);
                DrawText(canvas, text, scale);
                break;
            case DisplayList.DrawImage img:
                _diag.Counter("paint.draw_image", 1);
                DrawImage(canvas, img, scale, pendingImageSources);
                break;
        }
    }

    private void DrawText(DrawingCanvas canvas, DisplayList.DrawText text, float scale)
    {
        if (string.IsNullOrEmpty(text.Text)) return;

        var spec = FontSpecFromDrawText(text);
        var size = (float)text.FontSize * scale;
        var probe = FirstCodepoint(text.Text);
        var font = ResolveFont(spec, probe, size);
        var color = ToColor(text.Color);

        var originX = (float)text.X * scale;
        var originY = (float)text.Y * scale;
        var brush = Brushes.Solid(color);

        var textBlock = text.Shaped is ImageSharpShapedRun shaped && shaped.Font.Size == size
            ? shaped.TextBlock
            : new TextBlock(text.Text, new TextOptions(font));

        canvas.DrawText(textBlock, new PointF(originX, originY), -1, brush, null);
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
        // DrawingCanvas.DrawImage signature is (image, sourceRect, destinationRect, resampler):
        // sourceRect is the integer crop inside the source image and
        // destinationRect is the float-precision target on the canvas.
        Rectangle sourceRect;
        if (item.SourceRect is { } sr)
        {
            // Clamp to the source dimensions so a slice that overruns the
            // image (e.g. a sprite-sheet edge with sub-pixel rounding) still
            // produces a valid integer rect.
            var x = Math.Clamp((int)Math.Round(sr.X), 0, decoded.Width);
            var y = Math.Clamp((int)Math.Round(sr.Y), 0, decoded.Height);
            var w = Math.Clamp((int)Math.Round(sr.Width), 0, decoded.Width - x);
            var h = Math.Clamp((int)Math.Round(sr.Height), 0, decoded.Height - y);
            if (w <= 0 || h <= 0) return;
            sourceRect = new Rectangle(x, y, w, h);
        }
        else
        {
            sourceRect = new Rectangle(0, 0, decoded.Width, decoded.Height);
        }
        var destRect = ToDeviceRectF(item.Bounds, scale);
        canvas.DrawImage(src, sourceRect, destRect, KnownResamplers.Bicubic);
    }

    private readonly record struct FontCacheKey(FontSpec Spec, float Size, int Probe);

    private static FontSpec FontSpecFromDrawText(DisplayList.DrawText text)
        => new(text.FontFamilies, text.Bold, text.Italic);

    private static int FirstCodepoint(string text)
    {
        foreach (var codePoint in text.EnumerateCodePoints())
            return codePoint.Value;

        return 0;
    }

    private Font ResolveFont(FontSpec spec, int probeCodepoint, float size)
        => _fontCache.GetOrAdd(new FontCacheKey(spec, size, probeCodepoint), k => CreateFont(k.Spec, k.Size));

    private Font CreateFont(FontSpec spec, float size)
        => ImageSharpFontLookup.CreateFont(_fontCollection, spec, size);

    private static Matrix4x4 ToDeviceMatrix(Matrix2D m, float scale)
    {
        var s = scale > 0 ? scale : 1f;
        // Draw commands are converted to device-pixel coordinates before they
        // reach the canvas. The transform's linear terms still apply to those
        // coordinates directly; only CSS-pixel translation has to be scaled.
        return new Matrix4x4(
            (float)m.A, (float)m.B, 0f, 0f,
            (float)m.C, (float)m.D, 0f, 0f,
            0f, 0f, 1f, 0f,
            (float)(m.E * s), (float)(m.F * s), 0f, 1f);
    }

    private static RectangleF ToDeviceRectF(LayoutRect r, float scale)
    {
        var s = scale > 0 ? scale : 1f;
        return new RectangleF(
            (float)(r.X * s),
            (float)(r.Y * s),
            (float)(r.Width * s),
            (float)(r.Height * s));
    }

    private static RectangleF ToSnappedDeviceRectF(LayoutRect r, float scale)
    {
        var s = scale > 0 ? scale : 1f;
        var x0 = (float)Math.Round(r.X * s);
        var y0 = (float)Math.Round(r.Y * s);
        var x1 = (float)Math.Round((r.X + r.Width) * s);
        var y1 = (float)Math.Round((r.Y + r.Height) * s);
        return new RectangleF(x0, y0, x1 - x0, y1 - y0);
    }

    private static RectanglePolygon ToDeviceRectPath(LayoutRect r, float scale)
        => new(ToDeviceRectF(r, scale));

    private static RectanglePolygon ToSnappedDeviceRectPath(LayoutRect r, float scale)
        => new(ToSnappedDeviceRectF(r, scale));

    private static Color ToColor(CssColor c)
        => Color.FromPixel(new Rgba32(c.R, c.G, c.B, c.A));

    public void Dispose()
    {
        // FontCollection has no Dispose. Clear cached Font entries so a
        // long-lived host releases per-spec lookup results.
        _fontCache.Clear();
        _ = _fonts;
        _ = _webFonts;
    }
}
