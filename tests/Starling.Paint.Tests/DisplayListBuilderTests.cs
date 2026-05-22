using AwesomeAssertions;
using Starling.Css.Cascade;
using Starling.Html;
using Starling.Layout;
using Starling.Layout.Text;
using Starling.Paint.DisplayList;
using Starling.Spec;
namespace Starling.Paint.Tests;

[TestClass]
public sealed class DisplayListBuilderTests
{
    private static Starling.Paint.DisplayList.DisplayList BuildList(string html, Size viewport)
    {
        var document = HtmlParser.Parse(html);
        var style = new StyleEngine();
        var engine = new LayoutEngine(style, DefaultTextMeasurer.Instance);
        var root = engine.LayoutDocument(document, viewport);
        return new DisplayListBuilder().Build(root);
    }

    [TestMethod]
    public void Plain_text_produces_at_least_one_draw_text_item()
    {
        var dl = BuildList("<body><p>hello</p></body>", new Size(800, 600));

        dl.Items.OfType<DrawText>().Should().NotBeEmpty();
        dl.Items.OfType<DrawText>().First().Text.Should().Be("hello");
    }

    [TestMethod]
    public void Background_color_appears_as_fill_rect()
    {
        var dl = BuildList(
            "<body><div style=\"background-color: #ff0000\">hi</div></body>",
            new Size(400, 300));

        dl.Items.OfType<FillRect>()
            .Should().Contain(r => r.Color.R == 255 && r.Color.G == 0 && r.Color.B == 0);
    }

    [TestMethod]
    [Spec("css2", "https://www.w3.org/TR/CSS21/visuren.html#anonymous-block-level", section: "9.2.1.1")]
    public void Anonymous_block_does_not_repaint_the_containers_background()
    {
        // A flex (or inline-flex) container whose only child is bare text wraps
        // that text in an anonymous block. Per CSS 2.2 §9.2.1.1 an anonymous
        // box's non-inherited properties (background, border, …) take their
        // initial values — so the anonymous wrapper must NOT re-paint the
        // container's background. With a semi-transparent fill the double-paint
        // was visible as a brighter rectangle behind the text (the netclaw.dev
        // "Open source · Apache 2.0" pill and the "Search docs" field).
        var dl = BuildList(
            "<body><div style=\"display:flex; background-color: rgba(255,0,0,0.5); padding:6px 18px\">hi there</div></body>",
            new Size(400, 300));

        dl.Items.OfType<FillRect>()
            .Count(r => r.Color.R == 255 && r.Color.G == 0 && r.Color.B == 0)
            .Should().Be(1);
    }

    [TestMethod]
    public void Wrapped_text_produces_multiple_draw_text_items_on_different_lines()
    {
        var dl = BuildList(
            "<body><p>one two three four five six seven eight nine ten</p></body>",
            new Size(120, 600));

        var lines = dl.Items.OfType<DrawText>().Select(d => d.Y).Distinct().ToList();
        lines.Should().HaveCountGreaterThan(1);
    }

    [TestMethod]
    public void Empty_document_yields_no_text_items()
    {
        var dl = BuildList("<body></body>", new Size(400, 300));
        dl.Items.OfType<DrawText>().Should().BeEmpty();
    }

    [TestMethod]
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

    [TestMethod]
    public void Multi_word_unquoted_family_is_joined_with_spaces()
    {
        var dl = BuildList(
            "<body><p style=\"font-family: Open Sans\">styled</p></body>",
            new Size(400, 300));

        var text = dl.Items.OfType<DrawText>().First();
        text.FontFamilies.Should().Equal("Open Sans");
    }

    [TestMethod]
    public void Bold_weight_marks_draw_text_bold()
    {
        var dl = BuildList(
            "<body><p style=\"font-weight: 700\">heavy</p></body>",
            new Size(400, 300));

        dl.Items.OfType<DrawText>().Should().OnlyContain(t => t.Bold);
    }

    [TestMethod]
    public void Italic_style_marks_draw_text_italic()
    {
        var dl = BuildList(
            "<body><em>slanted</em></body>",
            new Size(400, 300));

        dl.Items.OfType<DrawText>().Should().Contain(t => t.Italic);
    }

    [TestMethod]
    public void Underlined_link_emits_text_and_underline_decoration()
    {
        // UA stylesheet: a { color: blue; text-decoration: underline; }. The
        // underline is now a typed decoration primitive (was a FillRect hack),
        // colored with the link's currentColor (blue).
        var dl = BuildList("<body><a href=\"/next\">go next</a></body>", new Size(400, 300));

        dl.Items.OfType<DrawText>().Should().Contain(d => d.Text.Contains("go", StringComparison.Ordinal));
        dl.Items.OfType<DrawTextDecoration>().Should().Contain(d =>
            d.Lines.HasFlag(TextDecorationLines.Underline)
            && d.Color.B == 255 && d.Color.R == 0 && d.Color.G == 0
            && d.Width > 0);
    }

    [TestMethod]
    public void Underline_decoration_no_longer_emitted_as_fill_rect()
    {
        // Regression: the old FillRect underline hack is gone. No 1–2px tall
        // blue fill should be emitted for an underlined link.
        var dl = BuildList("<body><a href=\"/next\">go next</a></body>", new Size(400, 300));

        dl.Items.OfType<FillRect>().Should().NotContain(r =>
            r.Color.B == 255 && r.Color.R == 0 && r.Color.G == 0 && r.Bounds.Height <= 2);
    }

    [TestMethod]
    public void Line_through_emits_line_through_decoration()
    {
        var dl = BuildList(
            "<body><span style=\"text-decoration: line-through\">struck</span></body>",
            new Size(400, 300));

        dl.Items.OfType<DrawTextDecoration>().Should().Contain(d =>
            d.Lines.HasFlag(TextDecorationLines.LineThrough));
    }

    [TestMethod]
    public void Overline_emits_overline_decoration()
    {
        var dl = BuildList(
            "<body><span style=\"text-decoration: overline\">topped</span></body>",
            new Size(400, 300));

        dl.Items.OfType<DrawTextDecoration>().Should().Contain(d =>
            d.Lines.HasFlag(TextDecorationLines.Overline));
    }

    [TestMethod]
    public void Decoration_color_overrides_text_color()
    {
        var dl = BuildList(
            "<body><span style=\"color: black; text-decoration: underline; text-decoration-color: red\">x</span></body>",
            new Size(400, 300));

        dl.Items.OfType<DrawTextDecoration>().Should().Contain(d =>
            d.Lines.HasFlag(TextDecorationLines.Underline)
            && d.Color.R == 255 && d.Color.G == 0 && d.Color.B == 0);
    }

    [TestMethod]
    public void Decoration_color_defaults_to_current_color()
    {
        var dl = BuildList(
            "<body><span style=\"color: green; text-decoration: underline\">x</span></body>",
            new Size(400, 300));

        // green = rgb(0, 128, 0) per CSS named colors.
        dl.Items.OfType<DrawTextDecoration>().Should().Contain(d =>
            d.Color.R == 0 && d.Color.G == 128 && d.Color.B == 0);
    }

    [TestMethod]
    public void Decoration_style_dashed_is_carried_through()
    {
        var dl = BuildList(
            "<body><span style=\"text-decoration: underline dashed\">x</span></body>",
            new Size(400, 300));

        dl.Items.OfType<DrawTextDecoration>().Should().Contain(d =>
            d.Style == TextDecorationStyleKind.Dashed);
    }

    [TestMethod]
    public void Decoration_thickness_is_honored()
    {
        var dl = BuildList(
            "<body><span style=\"text-decoration: underline; text-decoration-thickness: 4px\">x</span></body>",
            new Size(400, 300));

        dl.Items.OfType<DrawTextDecoration>().Should().Contain(d =>
            Math.Abs(d.Thickness - 4d) < 0.01);
    }

    [TestMethod]
    public void Text_shadow_emits_shadow_layer_beneath_text()
    {
        var dl = BuildList(
            "<body><span style=\"text-shadow: 2px 3px 1px gray\">shadowed</span></body>",
            new Size(400, 300));

        var items = dl.Items;
        var shadowIndex = items.ToList().FindIndex(i => i is DrawTextShadow);
        var textIndex = items.ToList().FindIndex(i => i is DrawText t && t.Text.Contains("shadow", StringComparison.Ordinal));

        shadowIndex.Should().BeGreaterThanOrEqualTo(0);
        textIndex.Should().BeGreaterThanOrEqualTo(0);
        // The shadow paints before (beneath) the glyphs.
        shadowIndex.Should().BeLessThan(textIndex);

        dl.Items.OfType<DrawTextShadow>().Should().Contain(s =>
            Math.Abs(s.OffsetX - 2d) < 0.01 && Math.Abs(s.OffsetY - 3d) < 0.01 && Math.Abs(s.Blur - 1d) < 0.01);
    }
}
