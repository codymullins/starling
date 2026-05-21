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
    private static readonly Lazy<WebGPUEnvironmentError> _webGpuAvailability = new(WebGPUEnvironment.ProbeAvailability);

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
        => Render(list, new LayoutRect(0, 0, viewport.Width, viewport.Height), scale);

    public RenderedBitmap Render(PaintList list, LayoutRect viewport, float scale = 1.0f)
        => Render(list, viewport, scale, opaqueBackground: true);

    /// <summary>
    /// Renders <paramref name="list"/> over a transparent canvas when
    /// <paramref name="opaqueBackground"/> is false — used by the compositor to
    /// rasterize a layer's slice into its own bitmap so unpainted regions stay
    /// see-through for alpha-over compositing. The default (true) clears to
    /// opaque white, the behavior every other caller relies on.
    /// </summary>
    public RenderedBitmap Render(PaintList list, LayoutRect viewport, float scale, bool opaqueBackground)
    {
        ArgumentNullException.ThrowIfNull(list);
        if (viewport.Width <= 0 || viewport.Height <= 0)
            throw new ArgumentException("Viewport must have positive dimensions.", nameof(viewport));
        if (!(scale > 0.0f))
            throw new ArgumentException("Scale must be positive.", nameof(scale));

        var width = (int)Math.Ceiling(viewport.Width * scale);
        var height = (int)Math.Ceiling(viewport.Height * scale);

        // viewport.X/Y is the page-coordinate scroll offset of the visible
        // region: a page-coord item at (viewport.X, viewport.Y) must land at
        // device (0,0). Express it as the outermost (ancestor) CSS-space
        // transform so it composes with per-item transforms and is folded into
        // the canvas matrix alongside the device scale by ToCanvasMatrix.
        var viewportTransform = Matrix2D.Translate(-viewport.X, -viewport.Y);

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
            return RenderCpu(list, width, height, scale, viewportTransform, opaqueBackground, webGpuFallbackReason: "exceeds_max_texture_dimension");
        }

        return _useWebGpu
            ? RenderWebGpu(list, width, height, scale, viewportTransform, opaqueBackground)
            : RenderCpu(list, width, height, scale, viewportTransform, opaqueBackground);
    }

    // 8192 is WebGPU's guaranteed minimum maxTextureDimension2D. Larger
    // surfaces can fail texture creation on constrained adapters, and wgpu's
    // default uncaptured-error handler aborts the process before C# can catch
    // anything, so detect the overflow before allocating a texture.
    private const int MaxWebGpuTextureDimension = 8192;

    private RenderedBitmap RenderCpu(PaintList list, int width, int height, float scale, Matrix2D viewportTransform, bool opaqueBackground = true, string? webGpuFallbackReason = null)
    {
        using (_diag.Span("paint", "raster.context_init"))
        {
            // ImageSharp has no persistent CPU context; the span is kept so
            // the diagnostics trace shape stays stable for tooling.
        }

        Image<Rgba32> image;
        using (_diag.Span("paint", "raster.surface_alloc"))
            image = new Image<Rgba32>(width, height, opaqueBackground ? new Rgba32(255, 255, 255, 255) : new Rgba32(0, 0, 0, 0));

        using (image)
        {
            // Image sources used by DrawImage outlive the canvas closure: the
            // DrawingCanvas records commands and rasterizes them when the
            // Mutate/Paint scope unwinds, so disposing image sources inside
            // the closure throws ObjectDisposedException from ImageBrushRenderer.
            // Stage them in a bag that is disposed after rasterization/readback.
            using var pendingImageSources = new DisposableBag();

            using (_diag.Span("paint", "raster.command_record"))
            {
                Activity.Current?.SetTag("raster.items", list.Items.Count);
                if (webGpuFallbackReason is not null)
                    Activity.Current?.SetTag("raster.webgpu.fallback_reason", webGpuFallbackReason);
                image.Mutate(x => x.Paint(canvas =>
                {
                    var transforms = new Stack<Matrix2D>();
                    transforms.Push(viewportTransform);
                    canvas.Save(new DrawingOptions
                    {
                        Transform = ToCanvasMatrix(viewportTransform, scale),
                    });

                    foreach (var item in list.Items)
                        Apply(canvas, item, scale, pendingImageSources, transforms);

                    canvas.Restore();
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
    }

    // GPU path: WebGPURenderTarget allocates an offscreen surface and exposes
    // the same DrawingCanvas API as the CPU path, so display-list replay stays
    // shared. The target starts transparent, so clear it to opaque white to
    // match the CPU image allocation (skipped when opaqueBackground is false so
    // compositor layer slices keep a see-through background).
    private RenderedBitmap RenderWebGpu(PaintList list, int width, int height, float scale, Matrix2D viewportTransform, bool opaqueBackground = true)
    {
        // Probe the WebGPU environment before constructing a render target so a
        // missing/broken wgpu-native binary, no compatible GPU adapter, or a
        // sandboxed Catalyst process surfaces as a clear actionable error
        // rather than the generic "failed to initialize webgpu runtime" the
        // constructor would otherwise throw.
        var probe = _webGpuAvailability.Value;
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

        using (target)
        {
            using var pendingImageSources = new DisposableBag();

            DrawingCanvas canvas;
            using (_diag.Span("paint", "raster.surface_alloc"))
                canvas = target.CreateCanvas();

            using (canvas)
            {
                using (_diag.Span("paint", "raster.command_record"))
                {
                    Activity.Current?.SetTag("raster.items", list.Items.Count);
                    if (opaqueBackground)
                        canvas.Clear(Brushes.Solid(Color.White), new Rectangle(0, 0, width, height));
                    var transforms = new Stack<Matrix2D>();
                    transforms.Push(viewportTransform);
                    canvas.Save(new DrawingOptions
                    {
                        Transform = ToCanvasMatrix(viewportTransform, scale),
                    });


                    foreach (var item in list.Items)
                        Apply(canvas, item, scale, pendingImageSources, transforms);

                    canvas.Restore();

                    // Flush seals queued commands into the canvas timeline so
                    // the GPU pipeline executes before ReadbackImage samples
                    // the texture. Without it, readback races the (un)submitted
                    // command buffer and returns the initial clear color.
                    using (_diag.Span("paint", "raster.flush"))
                        canvas.Flush();
                }
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
    }

    private void Apply(DrawingCanvas canvas, DisplayItem item, float scale, DisposableBag pendingImageSources, Stack<Matrix2D> transforms)
    {
        switch (item)
        {
            case PushTransform push:
                var matrix = transforms.Peek().Multiply(push.Matrix);
                transforms.Push(matrix);
                canvas.Save(new DrawingOptions
                {
                    Transform = ToCanvasMatrix(matrix, scale),
                });

                _diag.Counter("paint.push_transform", 1);
                break;
            case DisplayList.PopTransform:
                canvas.Restore();
                transforms.Pop();
                _diag.Counter("paint.pop_transform", 1);
                break;
            case FillRect fill:
                if (fill.Bounds.Width <= 0 || fill.Bounds.Height <= 0) return;
                _diag.Counter("paint.fill_rect", 1);
                var rectPath = fill.PixelAlignment == FillRectPixelAlignment.SnapToDevicePixels
                    ? ToSnappedLayoutRectPath(fill.Bounds, scale)
                    : ToRectPath(fill.Bounds);

                canvas.Fill(Brushes.Solid(ToColor(fill.Color)), rectPath);
                break;
            case StrokeRect stroke:
                _diag.Counter("paint.stroke_rect", 1);
                {
                    var penWidth = (float)stroke.Width;
                    var pen = Pens.Solid(ToColor(stroke.Color), penWidth);
                    var r = ToRectPath(stroke.Bounds);
                    canvas.Draw(pen, r);
                }
                break;
            case FillRoundedRect roundFill:
                if (roundFill.Bounds.Width <= 0 || roundFill.Bounds.Height <= 0) return;
                _diag.Counter("paint.fill_rounded_rect", 1);
                {
                    var path = BuildRoundedRectPath(roundFill.Bounds, roundFill.Radii);
                    canvas.Fill(Brushes.Solid(ToColor(roundFill.Color)), path);
                }
                break;
            case StrokeRoundedRect roundStroke:
                if (roundStroke.Bounds.Width <= 0 || roundStroke.Bounds.Height <= 0 || roundStroke.Width <= 0) return;
                _diag.Counter("paint.stroke_rounded_rect", 1);
                {
                    var path = BuildRoundedRectPath(roundStroke.Bounds, roundStroke.Radii);
                    canvas.Draw(Pens.Solid(ToColor(roundStroke.Color), (float)roundStroke.Width), path);
                }
                break;
            case DrawBoxShadow shadow:
                _diag.Counter("paint.box_shadow", 1);
                DrawBoxShadow(canvas, shadow, scale, pendingImageSources);
                break;
            case DrawText text:
                _diag.Counter("paint.draw_text", 1);
                DrawText(canvas, text);
                break;
            case DrawImage img:
                _diag.Counter("paint.draw_image", 1);
                DrawImage(canvas, img, pendingImageSources);
                break;
            case FillGradient grad:
                _diag.Counter("paint.fill_gradient", 1);
                FillGradient(canvas, grad);
                break;
        }
    }

    /// <summary>
    /// Rasterize a CSS Images 3 gradient by mapping its color stops onto an
    /// ImageSharp.Drawing gradient brush sized to the fill box. Linear and
    /// radial gradients (and their <c>repeating-</c> variants) are supported;
    /// conic gradients have no ImageSharp brush and are filtered out before this
    /// point, so they never reach the backend.
    /// </summary>
    private void FillGradient(DrawingCanvas canvas, FillGradient item)
    {
        var bounds = item.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var gradient = item.Gradient;
        if (gradient.Stops.Count < 2) return;

        var stops = ResolveColorStops(gradient, Math.Max(bounds.Width, bounds.Height));
        if (stops.Length < 2) return;

        var repetition = gradient.Repeating
            ? GradientRepetitionMode.Repeat
            : GradientRepetitionMode.None;

        var x = (float)bounds.X;
        var y = (float)bounds.Y;
        var w = (float)bounds.Width;
        var h = (float)bounds.Height;
        var path = ToRectPath(bounds);

        Brush brush;
        if (gradient.Kind == CssGradientKind.Radial)
        {
            var pos = gradient.Position ?? CssGradientPosition.Center;
            var cx = x + (float)(pos.FractionX * w);
            var cy = y + (float)(pos.FractionY * h);
            var radius = RadialRadius(gradient, pos, w, h);
            if (radius <= 0) return;
            brush = new RadialGradientBrush(new PointF(cx, cy), radius, repetition, stops);
        }
        else
        {
            // Linear: map the CSS gradient angle to two endpoints on the box.
            // CSS measures angle clockwise from "to top" (0deg = up). The
            // gradient line passes through the box center; endpoints sit where
            // it meets the box edge, with the start offset so the full color
            // range covers the projected box extent (CSS Images 3 §3.1).
            var angleDeg = gradient.Line?.ToDegrees(w, h) ?? 180.0;
            var rad = angleDeg * Math.PI / 180.0;
            // Direction the gradient progresses (toward the end color).
            var dx = Math.Sin(rad);
            var dy = -Math.Cos(rad);
            // Length of the projected gradient line: |w·sin| + |h·cos| keeps the
            // 0%/100% stops anchored to the box corners for the cardinal cases.
            var lineLen = Math.Abs(w * dx) + Math.Abs(h * dy);
            var ccx = x + w / 2.0;
            var ccy = y + h / 2.0;
            var p0 = new PointF((float)(ccx - dx * lineLen / 2.0), (float)(ccy - dy * lineLen / 2.0));
            var p1 = new PointF((float)(ccx + dx * lineLen / 2.0), (float)(ccy + dy * lineLen / 2.0));
            brush = new LinearGradientBrush(p0, p1, repetition, stops);
        }

        canvas.Fill(brush, path);
    }

    /// <summary>
    /// Resolve a gradient's color stops into ImageSharp <see cref="ColorStop"/>s
    /// with monotonically increasing ratios in 0..1. Stops without an explicit
    /// position are evenly distributed between their neighbors per CSS Images 3
    /// §3.4.3.
    /// </summary>
    private static ColorStop[] ResolveColorStops(CssGradient gradient, double lineLengthPx)
    {
        var src = gradient.Stops;
        var ratios = new double?[src.Count];
        for (var i = 0; i < src.Count; i++)
        {
            if (src[i].Position is { } p)
                ratios[i] = Math.Clamp(p.ResolveFraction(lineLengthPx), 0.0, 1.0);
        }

        // First/last default to 0 / 1.
        ratios[0] ??= 0.0;
        ratios[^1] ??= 1.0;

        // Enforce non-decreasing positions (CSS Images 3: a stop's position is
        // clamped to be >= the previous stop's).
        var lastKnown = 0.0;
        for (var i = 0; i < ratios.Length; i++)
        {
            if (ratios[i] is { } r)
            {
                if (r < lastKnown) r = lastKnown;
                ratios[i] = r;
                lastKnown = r;
            }
        }

        // Interpolate runs of null positions evenly between their bracketing
        // known stops.
        var idx = 0;
        while (idx < ratios.Length)
        {
            if (ratios[idx] is not null) { idx++; continue; }
            var startKnown = idx - 1;            // always >= 0 because [0] is set
            var end = idx;
            while (end < ratios.Length && ratios[end] is null) end++;
            var endKnown = end;                  // ratios[end] is set (last is set)
            var startVal = ratios[startKnown]!.Value;
            var endVal = ratios[endKnown]!.Value;
            var gap = endKnown - startKnown;
            for (var k = idx; k < end; k++)
                ratios[k] = startVal + (endVal - startVal) * (k - startKnown) / gap;
            idx = end;
        }

        var result = new ColorStop[src.Count];
        for (var i = 0; i < src.Count; i++)
        {
            var c = src[i].Color.ToSrgb();
            result[i] = new ColorStop((float)ratios[i]!.Value, Color.FromPixel(new Rgba32(c.R, c.G, c.B, c.A)));
        }
        return result;
    }

    private static float RadialRadius(CssGradient gradient, CssGradientPosition pos, float w, float h)
    {
        // Distances from the gradient center to each box edge.
        var left = pos.FractionX * w;
        var right = w - left;
        var top = pos.FractionY * h;
        var bottom = h - top;

        float closestSide = (float)Math.Min(Math.Min(left, right), Math.Min(top, bottom));
        float farthestSide = (float)Math.Max(Math.Max(left, right), Math.Max(top, bottom));

        double Corner(Func<double, double, double> pick)
        {
            var hx = pick(left, right);
            var hy = pick(top, bottom);
            return Math.Sqrt(hx * hx + hy * hy);
        }
        float closestCorner = (float)Corner(Math.Min);
        float farthestCorner = (float)Corner(Math.Max);

        return gradient.Size switch
        {
            CssRadialSize.ClosestSide => closestSide,
            CssRadialSize.ClosestCorner => closestCorner,
            CssRadialSize.FarthestSide => farthestSide,
            CssRadialSize.FarthestCorner => farthestCorner,
            _ => farthestCorner,
        };
    }

    private void DrawText(DrawingCanvas canvas, DrawText text)
    {
        if (string.IsNullOrEmpty(text.Text)) return;

        var spec = FontSpecFromDrawText(text);
        var size = (float)text.FontSize;
        var probe = FirstCodepoint(text.Text);
        var font = ResolveFont(spec, probe, size);
        var color = ToColor(text.Color);

        var originX = (float)text.X;
        var originY = (float)text.Y;
        var brush = Brushes.Solid(color);

        var textBlock = text.Shaped is ImageSharpShapedRun shaped && shaped.Font.Size == size
            ? shaped.TextBlock
            : new TextBlock(text.Text, new TextOptions(font));

        canvas.DrawText(textBlock, new PointF(originX, originY), -1, brush, null);
    }

    private static void DrawImage(DrawingCanvas canvas, DrawImage item, DisposableBag pendingImageSources)
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
        var destRect = ToRectF(item.Bounds);
        canvas.DrawImage(src, sourceRect, destRect, KnownResamplers.Bicubic);
    }

    private readonly record struct FontCacheKey(FontSpec Spec, float Size, int Probe);

    private static FontSpec FontSpecFromDrawText(DrawText text)
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

    private static Matrix4x4 ToCanvasMatrix(Matrix2D m, float scale)
    {
        var s = scale > 0 ? scale : 1f;
        // Display-list commands stay in CSS pixels. The canvas transform maps
        // the composed CSS transform into device pixels for the output surface.
        return new Matrix4x4(
            (float)(m.A * s), (float)(m.B * s), 0f, 0f,
            (float)(m.C * s), (float)(m.D * s), 0f, 0f,
            0f, 0f, 1f, 0f,
            (float)(m.E * s), (float)(m.F * s), 0f, 1f);
    }

    private static RectangleF ToRectF(LayoutRect r)
        => new((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height);

    private static RectangleF ToSnappedLayoutRectF(LayoutRect r, float scale)
    {
        var s = scale > 0 ? scale : 1f;
        var x0 = (float)(Math.Round(r.X * s) / s);
        var y0 = (float)(Math.Round(r.Y * s) / s);
        var x1 = (float)(Math.Round((r.X + r.Width) * s) / s);
        var y1 = (float)(Math.Round((r.Y + r.Height) * s) / s);
        return new RectangleF(x0, y0, x1 - x0, y1 - y0);
    }

    private static RectanglePolygon ToRectPath(LayoutRect r)
        => new(ToRectF(r));

    private static RectanglePolygon ToSnappedLayoutRectPath(LayoutRect r, float scale)
        => new(ToSnappedLayoutRectF(r, scale));

    private static Color ToColor(CssColor c)
        => Color.FromPixel(new Rgba32(c.R, c.G, c.B, c.A));

    // --- wp:M5-css-14 — rounded rects + box-shadow ---------------------------

    // Magic constant for approximating a 90° elliptical arc with a cubic Bézier.
    private const float ArcBezierK = 0.5522847498f;

    /// <summary>
    /// Builds a closed rounded-rectangle path for <paramref name="bounds"/> with
    /// independent per-corner elliptical radii. Each corner's radii are clamped
    /// to half the box extent so they never cross. A zero-radius corner degrades
    /// to a sharp right angle. Traced clockwise from the top edge.
    /// </summary>
    private static IPath BuildRoundedRectPath(LayoutRect bounds, CornerRadii r)
    {
        var x = (float)bounds.X;
        var y = (float)bounds.Y;
        var w = (float)bounds.Width;
        var h = (float)bounds.Height;
        var right = x + w;
        var bottom = y + h;

        // Clamp each radius to the half-extents of the box.
        var maxX = w / 2f;
        var maxY = h / 2f;
        float Clamp(double v, float max) => Math.Clamp((float)v, 0f, max);
        var tlx = Clamp(r.TopLeftX, maxX); var tly = Clamp(r.TopLeftY, maxY);
        var trx = Clamp(r.TopRightX, maxX); var tryy = Clamp(r.TopRightY, maxY);
        var brx = Clamp(r.BottomRightX, maxX); var bry = Clamp(r.BottomRightY, maxY);
        var blx = Clamp(r.BottomLeftX, maxX); var bly = Clamp(r.BottomLeftY, maxY);

        var pb = new PathBuilder();
        // Top edge, left→right, starting just after the top-left corner.
        pb.MoveTo(new PointF(x + tlx, y));
        pb.LineTo(new PointF(right - trx, y));
        // Top-right corner.
        if (trx > 0 || tryy > 0)
            pb.AddCubicBezier(
                new PointF(right - trx, y),
                new PointF(right - trx + trx * ArcBezierK, y),
                new PointF(right, y + tryy - tryy * ArcBezierK),
                new PointF(right, y + tryy));
        // Right edge.
        pb.LineTo(new PointF(right, bottom - bry));
        // Bottom-right corner.
        if (brx > 0 || bry > 0)
            pb.AddCubicBezier(
                new PointF(right, bottom - bry),
                new PointF(right, bottom - bry + bry * ArcBezierK),
                new PointF(right - brx + brx * ArcBezierK, bottom),
                new PointF(right - brx, bottom));
        // Bottom edge.
        pb.LineTo(new PointF(x + blx, bottom));
        // Bottom-left corner.
        if (blx > 0 || bly > 0)
            pb.AddCubicBezier(
                new PointF(x + blx, bottom),
                new PointF(x + blx - blx * ArcBezierK, bottom),
                new PointF(x, bottom - bly + bly * ArcBezierK),
                new PointF(x, bottom - bly));
        // Left edge.
        pb.LineTo(new PointF(x, y + tly));
        // Top-left corner.
        if (tlx > 0 || tly > 0)
            pb.AddCubicBezier(
                new PointF(x, y + tly),
                new PointF(x, y + tly - tly * ArcBezierK),
                new PointF(x + tlx - tlx * ArcBezierK, y),
                new PointF(x + tlx, y));
        pb.CloseFigure();
        return pb.Build();
    }

    /// <summary>
    /// Paints a single outer drop shadow (CSS Backgrounds 3 §6). The shadow
    /// silhouette — the box grown by <c>Spread</c>, with its corner radii grown
    /// to match — is rasterized into a transparent offscreen image at CSS-pixel
    /// resolution, blurred by a Gaussian of σ = blur/2, then blitted at the
    /// shadow offset through the same canvas transform every other primitive
    /// uses (so the device <paramref name="scale"/> composes correctly). Inset
    /// shadows are not painted (parsed only); the builder filters them out.
    /// </summary>
    private static void DrawBoxShadow(DrawingCanvas canvas, DrawBoxShadow shadow, float scale, DisposableBag pendingImageSources)
    {
        if (shadow.Inset) return; // inner shadows deferred
        if (shadow.Color.A == 0) return;

        // The spread-expanded silhouette in CSS px (still at the box origin).
        var spread = shadow.Spread;
        var silhouetteW = shadow.Bounds.Width + 2 * spread;
        var silhouetteH = shadow.Bounds.Height + 2 * spread;
        if (silhouetteW <= 0 || silhouetteH <= 0) return;

        var blur = Math.Max(0, shadow.Blur);
        // Padding around the silhouette so the Gaussian tail isn't clipped. A
        // Gaussian is effectively zero past 3σ; σ = blur/2, so 3σ = 1.5·blur.
        var margin = (int)Math.Ceiling(blur * 1.5) + 2;

        var imgW = (int)Math.Ceiling(silhouetteW) + 2 * margin;
        var imgH = (int)Math.Ceiling(silhouetteH) + 2 * margin;
        if (imgW <= 0 || imgH <= 0 || (long)imgW * imgH > 64_000_000L) return; // guard pathological sizes

        // Grow each corner radius by the spread so the silhouette stays a
        // rounded rect that hugs the box (a negative spread shrinks them).
        var grown = GrowRadii(shadow.Radii, spread);
        var silhouette = new LayoutRect(margin, margin, silhouetteW, silhouetteH);
        var shape = BuildRoundedRectPath(silhouette, grown);

        var shadowImage = new Image<Rgba32>(imgW, imgH, new Rgba32(0, 0, 0, 0));
        // Drawing 3 paints paths through a DrawingCanvas (the same model the main
        // render path uses), so fill the silhouette inside a Paint scope, then
        // soften it with a separate Gaussian-blur Mutate.
        shadowImage.Mutate(ctx => ctx.Paint(canvas => canvas.Fill(Brushes.Solid(ToColor(shadow.Color)), shape)));
        if (blur > 0)
            shadowImage.Mutate(ctx => ctx.GaussianBlur((float)(blur / 2d)));
        pendingImageSources.Add(shadowImage);

        // Destination in CSS px: the silhouette's top-left is
        // (Bounds - spread + offset); the image extends `margin` px further out.
        var destX = shadow.Bounds.X - spread + shadow.OffsetX - margin;
        var destY = shadow.Bounds.Y - spread + shadow.OffsetY - margin;
        var dest = new RectangleF((float)destX, (float)destY, imgW, imgH);
        canvas.DrawImage(shadowImage, new Rectangle(0, 0, imgW, imgH), dest, KnownResamplers.Bicubic);
    }

    private static CornerRadii GrowRadii(CornerRadii r, double by)
    {
        static double G(double v, double by) => v <= 0 ? 0 : Math.Max(0, v + by);
        return new CornerRadii(
            G(r.TopLeftX, by), G(r.TopLeftY, by),
            G(r.TopRightX, by), G(r.TopRightY, by),
            G(r.BottomRightX, by), G(r.BottomRightY, by),
            G(r.BottomLeftX, by), G(r.BottomLeftY, by));
    }

    private sealed class DisposableBag : IDisposable
    {
        private readonly List<IDisposable> _items = [];

        public void Add(IDisposable item)
            => _items.Add(item);

        public void Dispose()
        {
            foreach (var item in _items)
                item.Dispose();
        }
    }

    public void Dispose()
    {
        // FontCollection has no Dispose. Clear cached Font entries so a
        // long-lived host releases per-spec lookup results.
        _fontCache.Clear();
        _ = _fonts;
        _ = _webFonts;
    }
}
