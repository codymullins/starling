using System.Globalization;
using System.Numerics;
using System.Xml;
using System.Xml.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Starling.Common.Image;

namespace Starling.Paint.Svg;

/// <summary>
/// A pure-managed first-cut SVG rasterizer. Parses an SVG document and renders
/// the shapes real-world icons use into a backend-neutral
/// <see cref="DecodedImage"/> (straight RGBA8888, top-down, tightly packed),
/// matching the contract the OS-native raster decoders produce — so the engine
/// can treat a decoded SVG exactly like a decoded PNG.
/// </summary>
/// <remarks>
/// <para>
/// Rasterization runs on ImageSharp.Drawing 3 (already the engine's paint
/// backend), keeping this firmly inside the managed-first interop policy: no
/// native code, no new dependency. It lives in <c>Starling.Paint</c> — the only
/// engine project that references ImageSharp.Drawing — rather than in the native
/// interop seam <c>Starling.Codecs</c>, which references only
/// <c>Starling.Common</c> and must not take a draw dependency.
/// </para>
/// <para>
/// Supported: <c>&lt;svg&gt;</c> (width/height/viewBox), <c>&lt;g&gt;</c>,
/// <c>&lt;path&gt;</c>, <c>&lt;rect&gt;</c> (incl. rx/ry), <c>&lt;circle&gt;</c>,
/// <c>&lt;ellipse&gt;</c>, <c>&lt;line&gt;</c>, <c>&lt;polyline&gt;</c>,
/// <c>&lt;polygon&gt;</c>; fill/stroke/stroke-width/fill-rule/opacity via
/// presentation attributes, <c>style="…"</c>, and flat <c>&lt;style&gt;</c>
/// class rules; <c>transform</c>; hex/rgb()/named/currentColor colors. Unknown
/// elements and attributes are ignored gracefully.
/// </para>
/// </remarks>
public static class SvgImageDecoder
{
    /// <summary>Default intrinsic size when neither width/height nor viewBox is present (CSS replaced-element default).</summary>
    private const int DefaultDimension = 150;

    /// <summary>Clamp the rasterized canvas so a hostile/huge viewBox can't OOM.</summary>
    private const int MaxDimension = 4096;

    /// <summary>
    /// Decode SVG <paramref name="utf8"/> bytes into a <see cref="DecodedImage"/>.
    /// <paramref name="currentColor"/> resolves <c>currentColor</c> references
    /// (defaults to opaque black). Throws <see cref="SvgDecodeException"/> on
    /// malformed XML or a document with no rasterizable root.
    /// </summary>
    public static DecodedImage Decode(ReadOnlySpan<byte> utf8, Color? currentColor = null)
        => DecodeText(DecodeText(utf8), currentColor);

    /// <summary>Decode from an already-decoded SVG source string.</summary>
    public static DecodedImage DecodeText(string svg, Color? currentColor = null)
    {
        var root = ParseRoot(svg);

        // --- intrinsic size + viewBox → user-space transform -----------------
        ViewBox? viewBox = ParseViewBox(Attr(root, "viewBox"));
        float? widthAttr = ParseLength(Attr(root, "width"));
        float? heightAttr = ParseLength(Attr(root, "height"));

        float intrinsicW = widthAttr ?? viewBox?.Width ?? DefaultDimension;
        float intrinsicH = heightAttr ?? viewBox?.Height ?? DefaultDimension;

        int pxW = Math.Clamp((int)MathF.Ceiling(intrinsicW <= 0 ? DefaultDimension : intrinsicW), 1, MaxDimension);
        int pxH = Math.Clamp((int)MathF.Ceiling(intrinsicH <= 0 ? DefaultDimension : intrinsicH), 1, MaxDimension);

        // Map the viewBox into the viewport. preserveAspectRatio defaults to
        // "xMidYMid meet": a uniform scale that fits, then centering.
        Matrix3x2 viewportTransform = Matrix3x2.Identity;
        if (viewBox is { } vb && vb.Width > 0 && vb.Height > 0)
        {
            float s = MathF.Min(pxW / vb.Width, pxH / vb.Height);
            float tx = -vb.MinX * s + (pxW - vb.Width * s) / 2f;
            float ty = -vb.MinY * s + (pxH - vb.Height * s) / 2f;
            viewportTransform = new Matrix3x2(s, 0, 0, s, tx, ty);
        }

        // --- collect <style> class rules -------------------------------------
        var sheet = new SvgStyleSheet();
        CollectStyles(root, sheet);

        var rootStyle = new SvgStyle { CurrentColor = currentColor ?? Color.Black };

        // --- render via the ImageSharp.Drawing canvas ------------------------
        // The DrawingCanvas Save(options) REPLACES the current transform rather
        // than composing it, so we compose transforms ourselves and always pass
        // the full accumulated matrix down the tree (mirroring how
        // ImageSharpBackend threads its own transform stack).
        using var image = new Image<Rgba32>(pxW, pxH, new Rgba32(0, 0, 0, 0));
        image.Mutate(ctx => ctx.Paint(canvas =>
        {
            // Render the <svg> root as an element (not just its children) so its
            // own presentation attributes — `fill="none"`, `stroke`,
            // `stroke-width`, … — apply and inherit down the tree. Many icons
            // (e.g. stroke-only line icons with fill="none" stroke="currentColor"
            // on the root) otherwise fall back to the default black fill and
            // paint as a solid blob.
            RenderElement(canvas, root, rootStyle, viewportTransform, sheet);
        }));

        var buffer = new byte[checked(pxW * pxH * 4)];
        image.CopyPixelDataTo(buffer);
        return DecodedImage.FromBuffer(pxW, pxH, buffer);
    }

    private static XElement ParseRoot(string svg)
    {
        XDocument doc;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore, // tolerate <!DOCTYPE svg ...>
                XmlResolver = null,                   // never fetch external DTDs/entities
                CheckCharacters = false,
            };
            using var reader = XmlReader.Create(new System.IO.StringReader(svg), settings);
            doc = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new SvgDecodeException($"SVG is not well-formed XML: {ex.Message}", ex);
        }

        var root = doc.Root;
        if (root is null || !root.Name.LocalName.Equals("svg", StringComparison.OrdinalIgnoreCase))
            throw new SvgDecodeException("SVG document has no <svg> root element.");
        return root;
    }

    private static void RenderChildren(
        DrawingCanvas canvas, XElement parent, SvgStyle parentStyle, Matrix3x2 transform, SvgStyleSheet sheet)
    {
        foreach (var el in parent.Elements())
            RenderElement(canvas, el, parentStyle, transform, sheet);
    }

    private static void RenderElement(
        DrawingCanvas canvas, XElement el, SvgStyle parentStyle, Matrix3x2 parentTransform, SvgStyleSheet sheet)
    {
        string name = el.Name.LocalName;

        // Skip non-rendered containers (but their <style> was already harvested).
        if (name is "defs" or "style" or "title" or "desc" or "metadata" or "symbol"
            or "clipPath" or "mask" or "linearGradient" or "radialGradient" or "filter")
            return;

        // Resolve cascaded style: clone parent, apply class rules, then
        // presentation attributes, then style="…" (highest priority).
        var style = parentStyle.Clone();
        if (!sheet.IsEmpty)
            sheet.Apply(style, name, Attr(el, "class"));
        ApplyPresentationAttributes(style, el);
        style.ApplyStyleString(Attr(el, "style"));

        // Compose this element's local transform onto the inherited one. SVG
        // applies the local transform first to a point, so for row-vector
        // matrices (point * M) the composition is local * parent.
        var transform = SvgTransform.Parse(Attr(el, "transform")) * parentTransform;

        switch (name)
        {
            case "g":
            case "svg":
            case "a":
                RenderChildren(canvas, el, style, transform, sheet);
                break;
            case "path":
                DrawShape(canvas, SvgPathParser.Parse(Attr(el, "d")), style, transform);
                break;
            case "rect":
                DrawShape(canvas, BuildRect(el), style, transform);
                break;
            case "circle":
                DrawShape(canvas, BuildCircle(el), style, transform);
                break;
            case "ellipse":
                DrawShape(canvas, BuildEllipse(el), style, transform);
                break;
            case "line":
                DrawShape(canvas, BuildLine(el), style, transform, strokeOnly: true);
                break;
            case "polyline":
                DrawShape(canvas, BuildPoly(el, close: false), style, transform);
                break;
            case "polygon":
                DrawShape(canvas, BuildPoly(el, close: true), style, transform);
                break;
            default:
                // Unknown element: ignore but descend (some wrappers, e.g.
                // <switch>, hold renderable children).
                RenderChildren(canvas, el, style, transform, sheet);
                break;
        }
    }

    private static void DrawShape(
        DrawingCanvas canvas, IPath? path, SvgStyle style, Matrix3x2 transform, bool strokeOnly = false)
    {
        if (path is null)
            return;

        var fill = strokeOnly ? null : style.EffectiveFill();
        if (fill is { } fc)
        {
            // The canvas reads transform + fill rule from its current state. Save
            // both, draw, then restore. The transform is the full composed
            // matrix because Save replaces (does not compose) the transform.
            canvas.Save(new DrawingOptions
            {
                Transform = To4x4(transform),
                ShapeOptions = new ShapeOptions
                {
                    IntersectionRule = style.FillEvenOdd ? IntersectionRule.EvenOdd : IntersectionRule.NonZero,
                },
            });
            canvas.Fill(Brushes.Solid(fc), path);
            canvas.Restore();
        }

        var stroke = style.EffectiveStroke();
        if (stroke is { } sc && style.StrokeWidth > 0)
        {
            canvas.Save(new DrawingOptions { Transform = To4x4(transform) });
            canvas.Draw(Pens.Solid(sc, style.StrokeWidth), path);
            canvas.Restore();
        }
    }

    // --- shape builders ------------------------------------------------------

    private static IPath? BuildRect(XElement el)
    {
        float x = ParseLength(Attr(el, "x")) ?? 0;
        float y = ParseLength(Attr(el, "y")) ?? 0;
        float w = ParseLength(Attr(el, "width")) ?? 0;
        float h = ParseLength(Attr(el, "height")) ?? 0;
        if (w <= 0 || h <= 0)
            return null;

        float? rxRaw = ParseLength(Attr(el, "rx"));
        float? ryRaw = ParseLength(Attr(el, "ry"));
        float rx = MathF.Min(rxRaw ?? ryRaw ?? 0, w / 2f);
        float ry = MathF.Min(ryRaw ?? rxRaw ?? 0, h / 2f);

        if (rx > 0 && ry > 0)
        {
            // ImageSharp's rounded-rectangle helper uses a single corner radius;
            // SVG allows distinct rx/ry. Average them — icons set rx == ry.
            float radius = (rx + ry) / 2f;
            return RoundedRect(x, y, w, h, radius);
        }

        return new RectanglePolygon(x, y, w, h);
    }

    private static IPath RoundedRect(float x, float y, float w, float h, float r)
    {
        // Trace a rounded rect clockwise with quarter-circle corners as cubic
        // Béziers (k = 0.5523 approximates a 90° arc).
        const float k = 0.5522847498f;
        var pb = new PathBuilder();
        pb.MoveTo(new PointF(x + r, y));
        pb.LineTo(new PointF(x + w - r, y));
        pb.AddCubicBezier(new PointF(x + w - r, y), new PointF(x + w - r + r * k, y), new PointF(x + w, y + r - r * k), new PointF(x + w, y + r));
        pb.LineTo(new PointF(x + w, y + h - r));
        pb.AddCubicBezier(new PointF(x + w, y + h - r), new PointF(x + w, y + h - r + r * k), new PointF(x + w - r + r * k, y + h), new PointF(x + w - r, y + h));
        pb.LineTo(new PointF(x + r, y + h));
        pb.AddCubicBezier(new PointF(x + r, y + h), new PointF(x + r - r * k, y + h), new PointF(x, y + h - r + r * k), new PointF(x, y + h - r));
        pb.LineTo(new PointF(x, y + r));
        pb.AddCubicBezier(new PointF(x, y + r), new PointF(x, y + r - r * k), new PointF(x + r - r * k, y), new PointF(x + r, y));
        pb.CloseFigure();
        return pb.Build();
    }

    private static EllipsePolygon? BuildCircle(XElement el)
    {
        float cx = ParseLength(Attr(el, "cx")) ?? 0;
        float cy = ParseLength(Attr(el, "cy")) ?? 0;
        float r = ParseLength(Attr(el, "r")) ?? 0;
        return r <= 0 ? null : new EllipsePolygon(cx, cy, r, r);
    }

    private static EllipsePolygon? BuildEllipse(XElement el)
    {
        float cx = ParseLength(Attr(el, "cx")) ?? 0;
        float cy = ParseLength(Attr(el, "cy")) ?? 0;
        float rx = ParseLength(Attr(el, "rx")) ?? 0;
        float ry = ParseLength(Attr(el, "ry")) ?? 0;
        return rx <= 0 || ry <= 0 ? null : new EllipsePolygon(cx, cy, rx, ry);
    }

    private static IPath BuildLine(XElement el)
    {
        float x1 = ParseLength(Attr(el, "x1")) ?? 0;
        float y1 = ParseLength(Attr(el, "y1")) ?? 0;
        float x2 = ParseLength(Attr(el, "x2")) ?? 0;
        float y2 = ParseLength(Attr(el, "y2")) ?? 0;
        var pb = new PathBuilder();
        pb.AddLine(new PointF(x1, y1), new PointF(x2, y2));
        return pb.Build();
    }

    private static IPath? BuildPoly(XElement el, bool close)
    {
        var pts = ParsePoints(Attr(el, "points"));
        if (pts.Count < 2)
            return null;
        var pb = new PathBuilder();
        pb.AddLines(pts);
        if (close)
            pb.CloseFigure();
        return pb.Build();
    }

    private static List<PointF> ParsePoints(string? s)
    {
        var list = new List<PointF>();
        if (string.IsNullOrWhiteSpace(s))
            return list;
        var nums = s.Split([' ', '\t', '\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i + 1 < nums.Length; i += 2)
        {
            if (float.TryParse(nums[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                float.TryParse(nums[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                list.Add(new PointF(x, y));
        }
        return list;
    }

    // --- style attribute plumbing -------------------------------------------

    private static void ApplyPresentationAttributes(SvgStyle style, XElement el)
    {
        // color must be resolved before fill/stroke so currentColor sees it.
        ApplyAttr(style, el, "color");
        ApplyAttr(style, el, "fill");
        ApplyAttr(style, el, "stroke");
        ApplyAttr(style, el, "stroke-width");
        ApplyAttr(style, el, "fill-rule");
        ApplyAttr(style, el, "opacity");
        ApplyAttr(style, el, "fill-opacity");
        ApplyAttr(style, el, "stroke-opacity");
    }

    private static void ApplyAttr(SvgStyle style, XElement el, string name)
    {
        var v = Attr(el, name);
        if (v is not null)
            style.ApplyDeclaration(name, v);
    }

    private static void CollectStyles(XElement el, SvgStyleSheet sheet)
    {
        foreach (var styleEl in el.Descendants().Where(e => e.Name.LocalName.Equals("style", StringComparison.OrdinalIgnoreCase)))
            sheet.AddCss(styleEl.Value);
    }

    // --- attribute / unit helpers -------------------------------------------

    /// <summary>Read an attribute by local name, namespace-agnostic.</summary>
    private static string? Attr(XElement el, string localName)
    {
        foreach (var a in el.Attributes())
            if (a.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
                return a.Value;
        return null;
    }

    private static float? ParseLength(string? v)
    {
        if (string.IsNullOrWhiteSpace(v))
            return null;
        v = v.Trim();
        if (v.EndsWith('%'))
            return null; // percentage lengths unsupported (first cut)
        // Strip a trailing unit suffix (px/pt/etc.); user units == px here.
        int end = v.Length;
        while (end > 0 && char.IsLetter(v[end - 1]))
            end--;
        return float.TryParse(v.AsSpan(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : null;
    }

    private static ViewBox? ParseViewBox(string? v)
    {
        if (string.IsNullOrWhiteSpace(v))
            return null;
        var parts = v.Split([' ', '\t', '\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
            return null;
        if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var minX) &&
            float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var minY) &&
            float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) &&
            float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var h))
            return new ViewBox(minX, minY, w, h);
        return null;
    }

    /// <summary>
    /// Map a 2D affine <see cref="Matrix3x2"/> into the <see cref="Matrix4x4"/>
    /// the ImageSharp.Drawing canvas consumes (row 0/1 carry the linear part,
    /// row 3 the translation).
    /// </summary>
    private static Matrix4x4 To4x4(Matrix3x2 m) => new(
        m.M11, m.M12, 0f, 0f,
        m.M21, m.M22, 0f, 0f,
        0f, 0f, 1f, 0f,
        m.M31, m.M32, 0f, 1f);

    private static string DecodeText(ReadOnlySpan<byte> utf8)
    {
        // Strip a UTF-8 BOM if present, then decode. SVG is XML; the bytes we get
        // are virtually always UTF-8 in practice.
        if (utf8.Length >= 3 && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF)
            utf8 = utf8[3..];
        return System.Text.Encoding.UTF8.GetString(utf8);
    }

    private readonly record struct ViewBox(float MinX, float MinY, float Width, float Height);
}
