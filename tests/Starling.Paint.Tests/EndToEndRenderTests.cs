using FluentAssertions;
using Tessera.Css.Values;
using Tessera.Html;
using Tessera.Paint.Backend;
using Tessera.Paint.DisplayList;
using Xunit;
using LayoutRect = Tessera.Layout.Rect;
using LayoutSize = Tessera.Layout.Size;

namespace Tessera.Paint.Tests;

public sealed class EndToEndRenderTests
{
    [Fact]
    public void Rendering_a_paragraph_produces_an_image_of_requested_size()
    {
        var painter = new Painter();
        var document = HtmlParser.Parse("<body><p>Hello, world.</p></body>");
        using var image = painter.RenderDocument(document, new LayoutSize(640, 480));

        image.Width.Should().Be(640);
        image.Height.Should().Be(480);
    }

    [Fact]
    public void Rendered_paragraph_leaves_visible_pixels()
    {
        var painter = new Painter();
        var document = HtmlParser.Parse("<body><p>visible</p></body>");
        using var image = painter.RenderDocument(document, new LayoutSize(400, 200));

        BitmapPixels.CountNonWhite(image).Should().BeGreaterThan(0);
    }

    [Fact]
    public void Background_color_fills_pixels()
    {
        var painter = new Painter();
        var document = HtmlParser.Parse(
            "<body><div style=\"background-color: #008000; height: 50px\"></div></body>");
        using var image = painter.RenderDocument(document, new LayoutSize(200, 100));

        BitmapPixels.CountExact(image, 0, 128, 0).Should().BeGreaterThan(100);
    }

    [Fact]
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

    [Fact]
    public void Skia_backend_reuses_context_for_two_sequential_renders()
    {
        using var backend = new SkiaGraphiteBackend();

        using var first = backend.Render(
            SolidFillList(new CssColor(255, 0, 0)), new LayoutSize(32, 32));
        using var second = backend.Render(
            SolidFillList(new CssColor(0, 128, 0)), new LayoutSize(32, 32));

        first.GetPixel(16, 16).Should().Be(((byte)255, (byte)0, (byte)0, (byte)255));
        second.GetPixel(16, 16).Should().Be(((byte)0, (byte)128, (byte)0, (byte)255));
    }

    private static Tessera.Paint.DisplayList.DisplayList SolidFillList(CssColor color)
    {
        var list = new Tessera.Paint.DisplayList.DisplayList();
        list.Add(new FillRect(new LayoutRect(0, 0, 32, 32), color));
        return list;
    }
}
