using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Text;
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
    // Glyph-outline cache: maps a (text, font) run to its pre-tessellated glyph
    // outlines so a repeated word is filled instead of re-shaped+re-tessellated
    // on every DrawText. ImageSharp's DrawText(TextBlock,…) regenerates the full
    // glyph geometry on each call even when the TextBlock is reused — that
    // tessellation is the dominant cost in the raster.replay_items span on
    // text-heavy pages. Filling a cached, translated IPathCollection produces
    // byte-identical pixels for plain outline glyphs (verified by snapshot
    // tests) while skipping the re-tessellation. A null entry marks a run that
    // must keep going through DrawText: color/painted (COLR/CPAL emoji) glyphs,
    // whose layer fills GeneratePaths/GenerateGlyphs-outlines would drop.
    private readonly ConcurrentDictionary<GlyphOutlineKey, IPathCollection?> _glyphOutlineCache = new();
    // Cap the outline cache so a pathological page with a huge unique-token
    // count can't grow it without bound; past the cap, runs fall back to the
    // (still TextBlock-cached) DrawText path. Article-class pages have a few
    // thousand unique tokens, comfortably under this.
    private const int MaxGlyphOutlineCacheEntries = 20_000;
    // Test seam: lets the pixel-parity regression test render the same display
    // list with and without the outline fast-path and assert the two are
    // byte-identical. Production callers leave this at its default (enabled).
    private readonly bool _useGlyphOutlineCache;
    private static readonly Lazy<WebGPUEnvironmentError> _webGpuAvailability = new(WebGPUEnvironment.ProbeAvailability);

    public ImageSharpBackend(FontResolver fonts, FontFaceRegistry? webFonts, IDiagnostics? diagnostics = null, bool useWebGpu = false, bool useGlyphOutlineCache = true)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        _fonts = fonts;
        _webFonts = webFonts;
        _diag = diagnostics ?? NoopDiagnostics.Instance;
        _useWebGpu = useWebGpu;
        _useGlyphOutlineCache = useGlyphOutlineCache;
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
                var stats = new RasterStats();
                image.Mutate(x => x.Paint(canvas =>
                {
                    var transforms = new Stack<Matrix2D>();
                    transforms.Push(viewportTransform);
                    canvas.Save(new DrawingOptions
                    {
                        Transform = ToCanvasMatrix(viewportTransform, scale),
                    });

                    // raster.command_record wraps the whole Mutate, but ImageSharp
                    // rasterizes lazily when this Paint scope unwinds — so the
                    // pixel-fill cost lands between this span closing and
                    // command_record closing. replay_items isolates the
                    // display-list walk (recording + TextBlock build) so
                    // command_record − replay_items = the actual rasterization.
                    using (_diag.Span("paint", "raster.replay_items"))
                    {
                        foreach (var item in list.Items)
                        {
                            var start = Stopwatch.GetTimestamp();
                            Apply(canvas, item, scale, pendingImageSources, transforms, stats);
                            stats.Record(item, Stopwatch.GetTimestamp() - start);
                        }
                    }

                    canvas.Restore();
                }));
                stats.Emit(Activity.Current);
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

                    var stats = new RasterStats();
                    // replay_items isolates the display-list walk (recording +
                    // TextBlock build); on the GPU path the pixel work happens in
                    // raster.flush below, so the two spans split record from
                    // execute the same way the CPU path's command_record split does.
                    using (_diag.Span("paint", "raster.replay_items"))
                    {
                        foreach (var item in list.Items)
                        {
                            var start = Stopwatch.GetTimestamp();
                            Apply(canvas, item, scale, pendingImageSources, transforms, stats);
                            stats.Record(item, Stopwatch.GetTimestamp() - start);
                        }
                    }

                    canvas.Restore();
                    stats.Emit(Activity.Current);

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

    private void Apply(DrawingCanvas canvas, DisplayItem item, float scale, DisposableBag pendingImageSources, Stack<Matrix2D> transforms, RasterStats stats)
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
                DrawText(canvas, text, stats);
                break;
            case DrawTextShadow shadow:
                _diag.Counter("paint.draw_text_shadow", 1);
                DrawTextShadow(canvas, shadow, pendingImageSources, stats);
                break;
            case DrawTextDecoration decoration:
                _diag.Counter("paint.draw_text_decoration", 1);
                DrawTextDecoration(canvas, decoration, stats);
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

    private void DrawText(DrawingCanvas canvas, DrawText text, RasterStats stats)
    {
        if (string.IsNullOrEmpty(text.Text)) return;
        stats.TextChars += text.Text.Length;

        var spec = FontSpecFromDrawText(text);
        var size = (float)text.FontSize;
        var probe = FirstCodepoint(text.Text);
        var font = ResolveFont(spec, probe, size, stats);
        var color = ToColor(text.Color);

        var originX = (float)text.X;
        var originY = (float)text.Y;
        var brush = Brushes.Solid(color);

        // Fast path: fill cached, pre-tessellated glyph outlines. Repeated words
        // on a text-heavy page (the page footprint that dominates
        // raster.replay_items) re-tessellate identical geometry on every
        // DrawText, even with the TextBlock reused — ImageSharp regenerates the
        // glyph paths inside each DrawText call. Caching the outlines once per
        // (text, font) and filling a translated copy is byte-identical for
        // plain outline glyphs and ~1.5x faster on article-class pages. The
        // cache returns null for color/painted glyph runs, which keep going
        // through DrawText below so their layer fills are preserved.
        var outline = _useGlyphOutlineCache ? GetGlyphOutline(text.Text, font) : null;
        if (outline is not null)
        {
            stats.GlyphOutlineFilled++;
            canvas.Fill(brush, outline.Translate(originX, originY));
            return;
        }

        // Reusing the layout-time shaped run skips a paint-time reshape; a miss
        // (no ImageSharp run, or a font-size mismatch) rebuilds the TextBlock
        // here — re-shaping the run, the cost the postmortem suspected. Track
        // both so the trace shows the reuse ratio for text-heavy pages.
        TextBlock textBlock;
        if (text.Shaped is ImageSharpShapedRun shaped && shaped.Font.Size == size)
        {
            textBlock = shaped.TextBlock;
            stats.ShapedReused++;
        }
        else
        {
            textBlock = new TextBlock(text.Text, new TextOptions(font));
            stats.ShapedRebuilt++;
        }

        canvas.DrawText(textBlock, new PointF(originX, originY), -1, brush, null);
    }

    private readonly record struct GlyphOutlineKey(string Text, Font Font);

    /// <summary>
    /// Returns the pre-tessellated glyph outlines for <paramref name="text"/>
    /// rendered with <paramref name="font"/>, or <c>null</c> when the run must
    /// keep going through <c>DrawText</c> (a color/painted COLR/CPAL glyph,
    /// whose per-layer paint can't be expressed as a single filled outline, or
    /// the outline cache is full). The returned collection is at the glyph
    /// origin; callers translate it to the draw position before filling. The
    /// outlines are immutable and shared across every fragment with the same
    /// (text, font), so a repeated word tessellates exactly once.
    /// </summary>
    private IPathCollection? GetGlyphOutline(string text, Font font)
    {
        var key = new GlyphOutlineKey(text, font);
        if (_glyphOutlineCache.TryGetValue(key, out var cached))
            return cached;

        if (_glyphOutlineCache.Count >= MaxGlyphOutlineCacheEntries)
            return null;

        var built = BuildGlyphOutline(text, font);
        return _glyphOutlineCache.GetOrAdd(key, built);
    }

    /// <summary>
    /// Builds the merged outline path for <paramref name="text"/>, or returns
    /// <c>null</c> if any glyph carries a color/painted layer (COLR/CPAL) or a
    /// per-layer paint override — those must render through <c>DrawText</c> so
    /// their layer colors survive. Uses the same <see cref="TextOptions"/> the
    /// <c>DrawText</c> path uses so the produced geometry is identical.
    /// </summary>
    private static PathCollection? BuildGlyphOutline(string text, Font font)
    {
        var glyphs = TextBuilder.GenerateGlyphs(text, new TextOptions(font));
        if (glyphs.Count == 0)
            return null;

        var paths = new List<IPath>();
        foreach (var glyph in glyphs)
        {
            foreach (var layer in glyph.Layers)
            {
                // Anything other than a plain monochrome glyph outline (color
                // layers, decorations, custom paints) can't be collapsed into a
                // single fill without losing fidelity — bail to DrawText.
                if (layer.Kind != GlyphLayerKind.Glyph || layer.Paint is not null)
                    return null;
            }

            foreach (var path in glyph.PathList)
                paths.Add(path);
        }

        return new PathCollection(paths);
    }

    // ---- CSS Text Decoration 3 (wp:M5-css-15) ----

    private void DrawTextDecoration(DrawingCanvas canvas, DrawTextDecoration d, RasterStats stats)
    {
        if (d.Width <= 0 || d.Lines == TextDecorationLines.None) return;

        var spec = new FontSpec(d.FontFamilies, d.Bold, d.Italic);
        var size = (float)d.FontSize;
        var font = ResolveFont(spec, probeCodepoint: 'x', size, stats);
        var fm = font.FontMetrics;
        var unitsScale = fm.UnitsPerEm > 0 ? size / (float)fm.UnitsPerEm : size / 1000f;

        // Real font metrics (design units → CSS px). OpenType underline/strikeout
        // positions are measured from the baseline, positive = up.
        var ascender = Math.Abs(fm.HorizontalMetrics.Ascender) * unitsScale;
        var underlinePosFromBaseline = fm.UnderlinePosition * unitsScale; // typically negative (below)
        var underlineThickness = fm.UnderlineThickness * unitsScale;
        var strikeoutPosFromBaseline = fm.StrikeoutPosition * unitsScale; // positive (above baseline)

        // Resolved thickness: 0 sentinel means `auto`/`from-font` — prefer the
        // font's own underline thickness, falling back to ~1px-at-16px.
        var thickness = d.Thickness > 0
            ? d.Thickness
            : (underlineThickness > 0 ? underlineThickness : Math.Max(1f, size / 16f));

        var baseline = (float)d.BaselineY;
        var color = ToColor(d.Color);
        var x0 = (float)d.X;
        var x1 = (float)(d.X + d.Width);
        var offset = (float)d.UnderlineOffset;

        if ((d.Lines & TextDecorationLines.Underline) != 0)
        {
            // Underline sits just below the baseline. Honor text-underline-offset
            // (positive moves the line further from the text) on top of the
            // font's underline position.
            var yPos = underlinePosFromBaseline != 0
                ? baseline - (float)underlinePosFromBaseline + offset
                : baseline + Math.Max(1f, (float)thickness) + offset;
            DrawDecorationLine(canvas, x0, x1, yPos, (float)thickness, color, d.Style);
        }

        if ((d.Lines & TextDecorationLines.Overline) != 0)
        {
            // Overline rides above the text, at roughly the ascender / cap line.
            var yPos = baseline - (float)ascender;
            DrawDecorationLine(canvas, x0, x1, yPos, (float)thickness, color, d.Style);
        }

        if ((d.Lines & TextDecorationLines.LineThrough) != 0)
        {
            // Line-through crosses near the middle of the x-height. Prefer the
            // font's strikeout position; fall back to ~1/3 of the ascender.
            var yPos = strikeoutPosFromBaseline != 0
                ? baseline - (float)strikeoutPosFromBaseline
                : baseline - (float)ascender / 3f;
            DrawDecorationLine(canvas, x0, x1, yPos, (float)thickness, color, d.Style);
        }
    }

    private static void DrawDecorationLine(DrawingCanvas canvas, float x0, float x1, float y, float thickness, Color color, TextDecorationStyleKind style)
    {
        var w = Math.Max(0.5f, thickness);
        switch (style)
        {
            case TextDecorationStyleKind.Wavy:
                canvas.Draw(Pens.Solid(color, w), BuildWavyPath(x0, x1, y, w));
                break;
            case TextDecorationStyleKind.Double:
                // Two parallel lines separated by a gap of ~thickness.
                var gap = w + Math.Max(1f, w);
                canvas.Draw(Pens.Solid(color, w), HorizontalLine(x0, x1, y));
                canvas.Draw(Pens.Solid(color, w), HorizontalLine(x0, x1, y + gap));
                break;
            case TextDecorationStyleKind.Dotted:
                canvas.Draw(Pens.Dot(color, w), HorizontalLine(x0, x1, y));
                break;
            case TextDecorationStyleKind.Dashed:
                canvas.Draw(Pens.Dash(color, w), HorizontalLine(x0, x1, y));
                break;
            default:
                canvas.Draw(Pens.Solid(color, w), HorizontalLine(x0, x1, y));
                break;
        }
    }

    private static IPath HorizontalLine(float x0, float x1, float y)
    {
        var pb = new PathBuilder();
        pb.AddLine(new PointF(x0, y), new PointF(x1, y));
        return pb.Build();
    }

    private static IPath BuildWavyPath(float x0, float x1, float y, float thickness)
    {
        // Approximate the wavy underline as a sequence of cubic Bézier humps.
        // Wavelength scales with thickness so heavier underlines wave wider.
        var wavelength = Math.Max(4f, thickness * 4f);
        var amplitude = Math.Max(1f, thickness);
        var pb = new PathBuilder();
        pb.StartFigure();
        var x = x0;
        pb.MoveTo(new PointF(x, y));
        var up = true;
        while (x < x1)
        {
            var next = Math.Min(x1, x + wavelength / 2f);
            var dir = up ? -1f : 1f; // screen y grows downward; alternate humps
            var c1 = new PointF(x + (next - x) / 2f, y + dir * amplitude);
            var c2 = new PointF(x + (next - x) / 2f, y + dir * amplitude);
            pb.AddCubicBezier(new PointF(x, y), c1, c2, new PointF(next, y));
            x = next;
            up = !up;
        }
        return pb.Build();
    }

    private void DrawTextShadow(DrawingCanvas canvas, DrawTextShadow s, DisposableBag pendingImageSources, RasterStats stats)
    {
        if (string.IsNullOrEmpty(s.Text)) return;

        var spec = new FontSpec(s.FontFamilies, s.Bold, s.Italic);
        var size = (float)s.FontSize;
        var probe = FirstCodepoint(s.Text);
        var font = ResolveFont(spec, probe, size, stats);
        var color = ToColor(s.Color);
        var brush = Brushes.Solid(color);

        var originX = (float)(s.X + s.OffsetX);
        var originY = (float)(s.Y + s.OffsetY);

        TextBlock textBlock;
        if (s.Shaped is ImageSharpShapedRun shaped && shaped.Font.Size == size)
        {
            textBlock = shaped.TextBlock;
            stats.ShapedReused++;
        }
        else
        {
            textBlock = new TextBlock(s.Text, new TextOptions(font));
            stats.ShapedRebuilt++;
        }

        if (s.Blur <= 0)
        {
            // Sharp shadow: just an offset copy of the text in the shadow color.
            canvas.DrawText(textBlock, new PointF(originX, originY), -1, brush, null);
            return;
        }

        // Blurred shadow: render the text into an off-screen transparent buffer,
        // Gaussian-blur it, then composite the result onto the canvas at the
        // shadow offset. The buffer is sized in CSS px (1:1) with padding for
        // the blur spread; the canvas transform handles device scaling when the
        // blurred bitmap is blitted back.
        var em = font.FontMetrics.UnitsPerEm > 0 ? size / (float)font.FontMetrics.UnitsPerEm : size / 1000f;
        var ascent = Math.Abs(font.FontMetrics.HorizontalMetrics.Ascender) * em;
        var descent = Math.Abs(font.FontMetrics.HorizontalMetrics.Descender) * em;
        var advance = TextMeasurer.MeasureAdvance(s.Text, new TextOptions(font));
        var pad = (int)Math.Ceiling(s.Blur * 3) + 2;
        var width = (int)Math.Ceiling(advance.Width) + 2 * pad;
        var height = (int)Math.Ceiling(ascent + descent) + 2 * pad;
        if (width <= 0 || height <= 0) return;

        var glyphLayer = new Image<Rgba32>(width, height, new Rgba32(0, 0, 0, 0));
        // canvas.DrawImage records a command and rasterizes lazily when the
        // outer Mutate/Paint scope unwinds, so the layer must outlive this call.
        // Stage it in the bag that is disposed after readback (mirrors DrawImage).
        pendingImageSources.Add(glyphLayer);

        // This offscreen glyph render + Gaussian blur is a full nested
        // rasterization that the lazy outer command_record span never sees;
        // its own span makes shadow-heavy pages attributable.
        using (_diag.Span("paint", "raster.text_shadow_blur"))
        {
            Activity.Current?.SetTag("raster.text_shadow.width", width);
            Activity.Current?.SetTag("raster.text_shadow.height", height);
            Activity.Current?.SetTag("raster.text_shadow.blur", s.Blur);
            // The display-list DrawText origin (s.X, s.Y) is the top of the line box;
            // render the glyph run at (pad, pad) inside the layer so it matches.
            glyphLayer.Mutate(ctx => ctx.Paint(c =>
                c.DrawText(new TextBlock(s.Text, new TextOptions(font)), new PointF(pad, pad), -1, brush, null)));
            glyphLayer.Mutate(ctx => ctx.GaussianBlur((float)s.Blur));
        }

        // Composite at the offset origin minus the padding we added.
        var destRect = new RectangleF(originX - pad, originY - pad, width, height);
        canvas.DrawImage(glyphLayer, new Rectangle(0, 0, width, height), destRect, KnownResamplers.Bicubic);
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

    private Font ResolveFont(FontSpec spec, int probeCodepoint, float size, RasterStats? stats = null)
    {
        var key = new FontCacheKey(spec, size, probeCodepoint);
        if (_fontCache.TryGetValue(key, out var cached))
            return cached;

        // Cache miss: CreateFont resolves the family against the collection,
        // expensive and cold-start-heavy. Attribute the build to the render so
        // the trace separates cold font work from steady-state draw_text cost.
        var start = Stopwatch.GetTimestamp();
        var font = _fontCache.GetOrAdd(key, k => CreateFont(k.Spec, k.Size));
        if (stats is not null)
        {
            stats.FontCacheMiss++;
            stats.AddFontCreate(Stopwatch.GetTimestamp() - start);
        }
        _diag.Counter("paint.font.cache_miss", 1);
        return font;
    }

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
    private void DrawBoxShadow(DrawingCanvas canvas, DrawBoxShadow shadow, float scale, DisposableBag pendingImageSources)
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
        // This offscreen silhouette fill + Gaussian blur is a full nested
        // rasterization invisible to the lazy outer command_record span; its
        // own span makes shadow-heavy pages attributable.
        using (_diag.Span("paint", "raster.box_shadow_blur"))
        {
            Activity.Current?.SetTag("raster.box_shadow.width", imgW);
            Activity.Current?.SetTag("raster.box_shadow.height", imgH);
            Activity.Current?.SetTag("raster.box_shadow.blur", blur);
            // Drawing 3 paints paths through a DrawingCanvas (the same model the main
            // render path uses), so fill the silhouette inside a Paint scope, then
            // soften it with a separate Gaussian-blur Mutate.
            shadowImage.Mutate(ctx => ctx.Paint(canvas => canvas.Fill(Brushes.Solid(ToColor(shadow.Color)), shape)));
            if (blur > 0)
                shadowImage.Mutate(ctx => ctx.GaussianBlur((float)(blur / 2d)));
        }
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

    /// <summary>
    /// Per-render rasterization timing/quality counters accumulated during the
    /// display-list replay and emitted as tags on the <c>raster.command_record</c>
    /// span. The per-type <see cref="Record"/> timings cover <em>recording</em>
    /// time only (the <see cref="Apply"/> dispatch, including paint-time
    /// TextBlock builds); ImageSharp rasterizes lazily at scope unwind, so the
    /// pixel-fill cost is <c>command_record − replay_items</c>, not any single
    /// bucket here.
    /// </summary>
    private sealed class RasterStats
    {
        private long _fillRect, _strokeRect, _fillRounded, _strokeRounded;
        private long _boxShadow, _text, _textShadow, _textDecoration;
        private long _image, _gradient, _transform, _fontCreate;

        public int TextChars;
        public int ShapedReused;
        public int ShapedRebuilt;
        public int GlyphOutlineFilled;
        public int FontCacheMiss;

        public void Record(DisplayItem item, long ticks)
        {
            // Qualify with DisplayList.* because several record names (DrawText,
            // DrawImage, FillGradient, …) collide with method names on the
            // enclosing backend, which would otherwise bind as constant patterns.
            switch (item)
            {
                case DisplayList.FillRect: _fillRect += ticks; break;
                case DisplayList.StrokeRect: _strokeRect += ticks; break;
                case DisplayList.FillRoundedRect: _fillRounded += ticks; break;
                case DisplayList.StrokeRoundedRect: _strokeRounded += ticks; break;
                case DisplayList.DrawBoxShadow: _boxShadow += ticks; break;
                case DisplayList.DrawText: _text += ticks; break;
                case DisplayList.DrawTextShadow: _textShadow += ticks; break;
                case DisplayList.DrawTextDecoration: _textDecoration += ticks; break;
                case DisplayList.DrawImage: _image += ticks; break;
                case DisplayList.FillGradient: _gradient += ticks; break;
                case DisplayList.PushTransform:
                case DisplayList.PopTransform: _transform += ticks; break;
            }
        }

        public void AddFontCreate(long ticks) => _fontCreate += ticks;

        public void Emit(Activity? activity)
        {
            if (activity is null) return;
            static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
            activity.SetTag("raster.time.fill_rect_ms", Ms(_fillRect));
            activity.SetTag("raster.time.stroke_rect_ms", Ms(_strokeRect));
            activity.SetTag("raster.time.fill_rounded_rect_ms", Ms(_fillRounded));
            activity.SetTag("raster.time.stroke_rounded_rect_ms", Ms(_strokeRounded));
            activity.SetTag("raster.time.box_shadow_ms", Ms(_boxShadow));
            activity.SetTag("raster.time.draw_text_ms", Ms(_text));
            activity.SetTag("raster.time.text_shadow_ms", Ms(_textShadow));
            activity.SetTag("raster.time.text_decoration_ms", Ms(_textDecoration));
            activity.SetTag("raster.time.draw_image_ms", Ms(_image));
            activity.SetTag("raster.time.gradient_ms", Ms(_gradient));
            activity.SetTag("raster.time.transform_ms", Ms(_transform));
            activity.SetTag("raster.time.font_create_ms", Ms(_fontCreate));
            activity.SetTag("raster.text.chars", TextChars);
            activity.SetTag("raster.text.shaped_reused", ShapedReused);
            activity.SetTag("raster.text.shaped_rebuilt", ShapedRebuilt);
            activity.SetTag("raster.text.glyph_outline_filled", GlyphOutlineFilled);
            activity.SetTag("raster.font.cache_miss", FontCacheMiss);
        }
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
        _glyphOutlineCache.Clear();
        _ = _fonts;
        _ = _webFonts;
    }
}
