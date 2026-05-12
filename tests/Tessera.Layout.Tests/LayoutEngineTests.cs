using FluentAssertions;
using Tessera.Css.Cascade;
using Tessera.Html;
using Tessera.Layout.Box;
using Xunit;

namespace Tessera.Layout.Tests;

public sealed class LayoutEngineTests
{
    private static LayoutEngine NewEngine() => new(new StyleEngine());

    private static BlockBox Layout(string html, Size viewport)
        => NewEngine().LayoutDocument(HtmlParser.Parse(html), viewport);

    [Fact]
    public void Document_root_has_viewport_width()
    {
        var root = Layout("<body><p>x</p></body>", new Size(800, 600));
        root.Frame.Width.Should().Be(800);
    }

    [Fact]
    public void Block_children_stack_vertically()
    {
        var root = Layout("""
            <body><div>a</div><div>b</div><div>c</div></body>
            """, new Size(400, 600));

        // root -> html(BlockBox) -> body(BlockBox) -> 3 divs
        var body = FindBox(root, "body");
        body.Should().NotBeNull();
        var divs = body!.Children.Where(b => b.Element?.LocalName == "div").ToList();
        divs.Should().HaveCount(3);

        // Each div's frame should advance Y monotonically.
        for (var i = 1; i < divs.Count; i++)
            divs[i].Frame.Y.Should().BeGreaterThan(divs[i - 1].Frame.Y);
    }

    [Fact]
    public void Inline_text_runs_get_wrapped_into_anonymous_block()
    {
        var root = Layout("<body><p>some words here</p></body>", new Size(800, 600));
        var p = FindBox(root, "p");
        p.Should().NotBeNull();
        // The <p>'s children should be wrapped in an anonymous block hosting the text.
        p!.Children.Should().NotBeEmpty();
        var textBox = FlattenTextBoxes(p).First();
        textBox.Fragments.Should().NotBeEmpty();
    }

    [Fact]
    public void Text_wraps_when_a_line_overflows_the_container()
    {
        // Skinny viewport forces wrap.
        var root = Layout(
            "<body><p>the quick brown fox jumps over the lazy dog the quick brown fox</p></body>",
            new Size(120, 600));

        var fragments = AllFragments(root);
        fragments.Should().NotBeEmpty();
        // At least one fragment should be on a line other than y=0.
        fragments.Select(f => f.Y).Distinct().Count().Should().BeGreaterThan(1);
    }

    [Fact]
    public void Wide_viewport_keeps_text_on_a_single_line()
    {
        var root = Layout("<body><p>tiny line</p></body>", new Size(2000, 600));
        var fragments = AllFragments(root);
        fragments.Should().NotBeEmpty();
        fragments.Select(f => f.Y).Distinct().Should().ContainSingle();
    }

    [Fact]
    public void Display_none_excludes_subtree_from_box_tree()
    {
        var root = Layout(
            "<body><div style=\"display:none\">hidden</div><div>visible</div></body>",
            new Size(400, 600));

        var body = FindBox(root, "body")!;
        var divs = body.Children.Where(b => b.Element?.LocalName == "div").ToList();
        divs.Should().HaveCount(1);
        divs[0].Element!.GetAttribute("style").Should().NotContain("none");
    }

    [Fact]
    public void Display_contents_drops_the_box_but_keeps_descendants()
    {
        var root = Layout(
            "<body><div style=\"display:contents\"><p>inner</p></div></body>",
            new Size(400, 600));

        // The wrapping <div> should be elided; <p> should be a direct child of body.
        var body = FindBox(root, "body")!;
        body.Children.OfType<BlockBox>().Should().Contain(b => b.Element != null && b.Element.LocalName == "p");
        body.Children.OfType<BlockBox>().Should().NotContain(b => b.Element != null && b.Element.LocalName == "div");
    }

    [Fact]
    public void Body_height_grows_to_contain_children()
    {
        var root = Layout(
            "<body><div>a</div><div>b</div></body>",
            new Size(400, 600));

        var body = FindBox(root, "body")!;
        body.Frame.Height.Should().BeGreaterThan(0);
    }

    // ---------------------------------------------------------------- helpers

    private static Box.Box? FindBox(Box.Box root, string localName)
    {
        if (root.Element?.LocalName == localName) return root;
        foreach (var child in root.Children)
        {
            var hit = FindBox(child, localName);
            if (hit is not null) return hit;
        }
        return null;
    }

    private static IEnumerable<TextBox> FlattenTextBoxes(Box.Box box)
    {
        if (box is TextBox tb) { yield return tb; yield break; }
        foreach (var child in box.Children)
            foreach (var inner in FlattenTextBoxes(child))
                yield return inner;
    }

    private static List<TextFragment> AllFragments(Box.Box root)
    {
        var result = new List<TextFragment>();
        Recurse(root);
        return result;

        void Recurse(Box.Box b)
        {
            if (b is TextBox tb) result.AddRange(tb.Fragments);
            foreach (var c in b.Children) Recurse(c);
        }
    }
}
