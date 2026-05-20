using AwesomeAssertions;
using Starling.Css.Values;
using Starling.Html;
using Starling.Paint.Backend;
using Starling.Paint.DisplayList;
using LayoutRect = Starling.Layout.Rect;
using LayoutSize = Starling.Layout.Size;

namespace Starling.Paint.Tests;

[TestClass]
public sealed class EndToEndRenderTests
{
    [TestMethod]
    public void Rendering_a_paragraph_produces_an_image_of_requested_size()
    {
        var painter = new Painter();
        var document = HtmlParser.Parse("<body><p>Hello, world.</p></body>");
        using var image = painter.RenderDocument(document, new LayoutSize(640, 480));

        image.Width.Should().Be(640);
        image.Height.Should().Be(480);
    }

    [TestMethod]
    public void Rendered_paragraph_leaves_visible_pixels()
    {
        var painter = new Painter();
        var document = HtmlParser.Parse("<body><p>visible</p></body>");
        using var image = painter.RenderDocument(document, new LayoutSize(400, 200));

        BitmapPixels.CountNonWhite(image).Should().BeGreaterThan(0);
    }

    [TestMethod]
    public void Background_color_fills_pixels()
    {
        var painter = new Painter();
        var document = HtmlParser.Parse(
            "<body><div style=\"background-color: #008000; height: 50px\"></div></body>");
        using var image = painter.RenderDocument(document, new LayoutSize(200, 100));

        BitmapPixels.CountExact(image, 0, 128, 0).Should().BeGreaterThan(100);
    }

    [TestMethod]
    public void Different_font_families_produce_different_rasters()
    {
        // monospace and sans-serif have visibly different metrics — a string
        // long enough to wrap or measurably shift glyph widths should produce
        // distinct pixel buffers. If the painter ignored font-family the two
        // renders would be byte-identical.
        var painter = new Painter();
        const string text = "The quick brown fox jumps over the lazy dog";
        var sans = HtmlParser.Parse(
            $"<body><p style=\"font-family: sans-serif\">{text}</p></body>");
        var mono = HtmlParser.Parse(
            $"<body><p style=\"font-family: monospace\">{text}</p></body>");

        using var sansImg = painter.RenderDocument(sans, new LayoutSize(400, 200));
        using var monoImg = painter.RenderDocument(mono, new LayoutSize(400, 200));

        BitmapPixels.PixelsEqual(sansImg, monoImg).Should().BeFalse(
            "rendering the same text with sans-serif and monospace should differ at the pixel level");
    }

    [TestMethod]
    public void Backend_reuses_context_for_two_sequential_renders()
    {
        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);

        using var first = backend.Render(
            SolidFillList(new CssColor(255, 0, 0)), new LayoutSize(32, 32));
        using var second = backend.Render(
            SolidFillList(new CssColor(0, 128, 0)), new LayoutSize(32, 32));

        first.GetPixel(16, 16).Should().Be(((byte)255, (byte)0, (byte)0, (byte)255));
        second.GetPixel(16, 16).Should().Be(((byte)0, (byte)128, (byte)0, (byte)255));
    }

    private static Starling.Paint.DisplayList.DisplayList SolidFillList(CssColor color)
    {
        var list = new Starling.Paint.DisplayList.DisplayList();
        list.Add(new FillRect(new LayoutRect(0, 0, 32, 32), color, FillRectPixelAlignment.Preserve));
        return list;
    }
}
