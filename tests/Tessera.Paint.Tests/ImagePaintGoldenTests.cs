using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Tessera.Common.Image;
using Tessera.Dom;
using Tessera.Html;
using Tessera.Layout.Tree;
using Xunit;
using LayoutSize = Tessera.Layout.Size;

namespace Tessera.Paint.Tests;

[Trait("Category", "GoldenImage")]
public sealed class ImagePaintGoldenTests
{
    [Fact]
    public void Inline_png_image_paints_image_pixels()
    {
        // 40x20 solid red; the layout + paint pipeline should blit those red
        // pixels into the output PNG. We bypass the engine's fetch step by
        // populating an in-memory resolver directly.
        using var swatch = SolidSwatch(40, 20, new Rgba32(255, 0, 0));
        const string Html = "<body><p>before<img id=swatch>after</p></body>";
        var document = HtmlParser.Parse(Html);
        var imgElement = FindById(document, "swatch");
        var resolver = new ManualImageResolver();
        resolver.Add(imgElement, swatch);

        // Pin ImageSharp: this asserts on blitted image pixels, and the Skia
        // shim's ts_canvas_draw_image is a no-op on Graphite canvases (native
        // defect, see wp:M3-06g/06i). The Skia default covers everything else.
        var painter = new Painter();
        using var rendered = painter.RenderDocument(
            document, new LayoutSize(320, 180), defaultFontSize: 16f, images: resolver,
            backend: PaintBackend.ImageSharp);

        BitmapPixels.CountExact(rendered, 255, 0, 0).Should().BeGreaterThanOrEqualTo(
            500, "the 40x20 red swatch (800 px) should land in the output");
    }

    [Fact]
    public void Inline_jpeg_image_paints_image_pixels()
    {
        // Round-trip through a JPEG byte buffer so the test also exercises
        // ImageSharp's decoder path. JPEG is lossy, so we sample by counting
        // pixels in a wide colour band rather than exact matches.
        using var swatch = JpegRoundTrip(SolidSwatch(60, 20, new Rgba32(0, 0, 255)));
        const string Html = "<body><p>before<img id=swatch>after</p></body>";
        var document = HtmlParser.Parse(Html);
        var imgElement = FindById(document, "swatch");
        var resolver = new ManualImageResolver();
        resolver.Add(imgElement, swatch);

        // Pin ImageSharp: see the PNG case — Skia's ts_canvas_draw_image is a
        // no-op on Graphite canvases (native defect, wp:M3-06g/06i).
        var painter = new Painter();
        using var rendered = painter.RenderDocument(
            document, new LayoutSize(320, 180), defaultFontSize: 16f, images: resolver,
            backend: PaintBackend.ImageSharp);

        BitmapPixels.CountBluish(rendered).Should().BeGreaterThanOrEqualTo(
            500, "the 60x20 blue JPEG swatch should dominate a region of the output");
    }

    [Fact]
    public void Broken_src_falls_back_to_alt_text_and_does_not_crash()
    {
        // No image registered → BoxTreeBuilder should degrade to a TextBox
        // carrying the alt content. The pipeline should complete and the
        // alt text should render somewhere in the output.
        const string Html = "<body><p><img alt=\"alt text\"></p></body>";
        var document = HtmlParser.Parse(Html);

        var painter = new Painter();
        using var rendered = painter.RenderDocument(document, new LayoutSize(320, 180), defaultFontSize: 16f);

        BitmapPixels.CountNonWhite(rendered).Should().BeGreaterThan(
            30, "alt text should render as glyphs when the image fails to resolve");
    }

    private static Image<Rgba32> SolidSwatch(int w, int h, Rgba32 color)
    {
        var image = new Image<Rgba32>(w, h);
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++) row[x] = color;
            }
        });
        return image;
    }

    private static Image<Rgba32> JpegRoundTrip(Image<Rgba32> source)
    {
        using var ms = new MemoryStream();
        source.SaveAsJpeg(ms);
        source.Dispose();
        ms.Position = 0;
        using var decoded = Image.Load(ms);
        return decoded.CloneAs<Rgba32>();
    }

    private static Element FindById(Document document, string id)
    {
        foreach (var img in document.GetElementsByTagName("img"))
            if (img.Id == id) return img;
        throw new InvalidOperationException($"No <img id='{id}'> in fixture");
    }

    private sealed class ManualImageResolver : IImageResolver
    {
        private readonly Dictionary<Element, ResolvedImage> _byElement = [];

        // Copies the ImageSharp pixels into a backend-neutral DecodedImage so
        // the caller's `using var swatch` can dispose its bitmap independently.
        public void Add(Element element, Image<Rgba32> image)
        {
            var decoded = DecodedImage.CreatePooled(
                image.Width, image.Height, span => image.CopyPixelDataTo(span));
            _byElement[element] = new ResolvedImage(image.Width, image.Height, decoded);
        }

        public bool TryResolve(Element element, out ResolvedImage image)
            => _byElement.TryGetValue(element, out image);
    }
}
