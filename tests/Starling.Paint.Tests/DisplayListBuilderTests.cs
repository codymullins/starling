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

    [Fact]
    public void Font_family_list_is_carried_onto_draw_text()
    {
        // The font-family value parser preserves case for ident family names
        // and lowercases only the generic keywords ("sans-serif" et al.).
        var dl = BuildList(
            "<body><p style=\"font-family: 'Helvetica Neue', Helvetica, sans-serif\">styled</p></body>",
            new Size(400, 300));

        var text = dl.Items.OfType<DrawText>().First();
        text.FontFamilies.Should().Equal("Helvetica Neue", "Helvetica", "sans-serif");
    }

    [Fact]
    public void Multi_word_unquoted_family_is_joined_with_spaces()
    {
        var dl = BuildList(
            "<body><p style=\"font-family: Open Sans\">styled</p></body>",
            new Size(400, 300));

        var text = dl.Items.OfType<DrawText>().First();
        text.FontFamilies.Should().Equal("Open Sans");
    }

    [Fact]
    public void Bold_weight_marks_draw_text_bold()
    {
        var dl = BuildList(
            "<body><p style=\"font-weight: 700\">heavy</p></body>",
            new Size(400, 300));

        dl.Items.OfType<DrawText>().Should().OnlyContain(t => t.Bold);
    }

    [Fact]
    public void Italic_style_marks_draw_text_italic()
    {
        var dl = BuildList(
            "<body><em>slanted</em></body>",
            new Size(400, 300));

        dl.Items.OfType<DrawText>().Should().Contain(t => t.Italic);
    }

    [Fact]
    public void Underlined_link_emits_text_and_underline_fill()
    {
        var dl = BuildList("<body><a href=\"/next\">go next</a></body>", new Size(400, 300));

        dl.Items.OfType<DrawText>().Should().Contain(d => d.Text.Contains("go", StringComparison.Ordinal));
        dl.Items.OfType<FillRect>().Should().Contain(r =>
            r.Color.B == 255 && r.Color.R == 0 && r.Color.G == 0 && r.Bounds.Height >= 1 && r.Bounds.Height <= 2);
    }
}
