using FluentAssertions;
using Tessera.Css.Cascade;
using Tessera.Html;
using Tessera.Layout;
using Tessera.Layout.Text;
using Tessera.Paint.DisplayList;
using Xunit;

namespace Tessera.Paint.Tests;

public sealed class DisplayListBuilderTests
{
    private static Tessera.Paint.DisplayList.DisplayList BuildList(string html, Size viewport)
    {
        var document = HtmlParser.Parse(html);
        var style = new StyleEngine();
        var engine = new LayoutEngine(style, DefaultTextMeasurer.Instance);
        var root = engine.LayoutDocument(document, viewport);
        return new DisplayListBuilder().Build(root);
    }

    [Fact]
    public void Plain_text_produces_at_least_one_draw_text_item()
    {
        var dl = BuildList("<body><p>hello</p></body>", new Size(800, 600));

        dl.Items.OfType<DrawText>().Should().NotBeEmpty();
        dl.Items.OfType<DrawText>().First().Text.Should().Be("hello");
    }

    [Fact]
    public void Background_color_appears_as_fill_rect()
    {
        var dl = BuildList(
            "<body><div style=\"background-color: #ff0000\">hi</div></body>",
            new Size(400, 300));

        dl.Items.OfType<FillRect>()
            .Should().Contain(r => r.Color.R == 255 && r.Color.G == 0 && r.Color.B == 0);
    }

    [Fact]
    public void Wrapped_text_produces_multiple_draw_text_items_on_different_lines()
    {
        var dl = BuildList(
            "<body><p>one two three four five six seven eight nine ten</p></body>",
            new Size(120, 600));

        var lines = dl.Items.OfType<DrawText>().Select(d => d.Y).Distinct().ToList();
        lines.Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void Empty_document_yields_no_text_items()
    {
        var dl = BuildList("<body></body>", new Size(400, 300));
        dl.Items.OfType<DrawText>().Should().BeEmpty();
    }
}
