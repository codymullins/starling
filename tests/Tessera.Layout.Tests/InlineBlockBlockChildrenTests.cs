using FluentAssertions;
using Tessera.Css.Cascade;
using Tessera.Html;
using Tessera.Layout.Box;
using Xunit;

namespace Tessera.Layout.Tests;

/// <summary>
/// An <c>inline-block</c> establishes a new block formatting context (CSS 2.1
/// §10.1). When its children mix block and inline content, the block children
/// must lay out vertically as in any block container; the inline runs around
/// them are wrapped in anonymous blocks. Without this, legacy pages whose
/// table-cell-wrapped forms (Google's homepage) contain block children render
/// nothing for those children.
/// </summary>
public sealed class InlineBlockBlockChildrenTests
{
    private static LayoutEngine NewEngine() => new(new StyleEngine());

    private static BlockBox Layout(string html, Size viewport)
        => NewEngine().LayoutDocument(HtmlParser.Parse(html), viewport);

    [Fact]
    public void Block_child_inside_inline_block_lays_out_and_inline_block_has_height()
    {
        // The inline-block <span> wraps a block <div> and an inline <span>.
        // Per §10.1, the inline-block forms a BFC, so the inner div stacks
        // above the inline anonymous block that holds "inline sibling".
        const string html = """
            <body><span style="display:inline-block">
              <div>block child</div>
              <span>inline sibling</span>
            </span></body>
            """;
        var root = Layout(html, new Size(800, 600));

        var inlineBlock = FindBox(root, "span");
        inlineBlock.Should().NotBeNull();
        inlineBlock!.Frame.Height.Should().BeGreaterThan(0,
            "the inline-block must size to contain the inner block + inline line");

        var innerDiv = FindBox(inlineBlock, "div");
        innerDiv.Should().NotBeNull("the block child must appear in the box tree");
        innerDiv!.Frame.Height.Should().BeGreaterThan(0);

        var allText = FlattenTextBoxes(inlineBlock).Select(tb => tb.Text).ToList();
        allText.Should().Contain(s => s.Contains("block child"));
        allText.Should().Contain(s => s.Contains("inline sibling"));
    }

    [Fact]
    public void Google_table_cell_lays_out_input_above_submit_button()
    {
        // Mirrors Google's homepage cell shape: a <td> (UA: inline-block)
        // containing a <div> wrapping a text input, followed by a submit
        // button. The text input must sit visually above the button.
        const string html = """
            <body><table><tr>
              <td>
                <div><input type="text" size="57" name="q"></div>
                <input type="submit" value="Search">
              </td>
            </tr></table></body>
            """;
        var root = Layout(html, new Size(1024, 768));

        var textInput = FindInputByType(root, "text");
        var submit = FindInputByType(root, "submit");

        textInput.Should().NotBeNull("the search box must be in the box tree");
        submit.Should().NotBeNull("the submit button must be in the box tree");

        textInput!.Frame.Width.Should().BeGreaterThan(0);
        textInput.Frame.Height.Should().BeGreaterThan(0);
        submit!.Frame.Width.Should().BeGreaterThan(0);
        submit.Frame.Height.Should().BeGreaterThan(0);

        // Vertical stacking: the input lives in a block <div>, the submit
        // sits in the anonymous block below. Document-space Y comparison.
        AbsoluteY(textInput).Should().BeLessThan(AbsoluteY(submit),
            "the text input should sit above the submit button inside the cell");
    }

    [Fact]
    public void Inline_block_with_only_text_still_uses_text_only_path()
    {
        // Regression guard: a pure-text inline-block must still size by its
        // measured text content, not by the BFC sub-pass.
        const string html = """
            <body><span style="display:inline-block">hello world</span></body>
            """;
        var root = Layout(html, new Size(800, 600));

        var inlineBlock = FindBox(root, "span")!;
        inlineBlock.Frame.Width.Should().BeGreaterThan(0);
        inlineBlock.Frame.Height.Should().BeGreaterThan(0);

        var text = FlattenTextBoxes(inlineBlock).Select(tb => tb.Text).ToList();
        text.Should().Contain(s => s.Contains("hello"));
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

    private static Box.Box? FindInputByType(Box.Box root, string type)
    {
        if (root.Element?.LocalName == "input" &&
            string.Equals(root.Element.GetAttribute("type"), type, StringComparison.OrdinalIgnoreCase))
            return root;
        foreach (var child in root.Children)
        {
            var hit = FindInputByType(child, type);
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

    /// <summary>Sum each ancestor's <see cref="Box.Box.Frame"/> Y to get a
    /// document-space Y coordinate.</summary>
    private static double AbsoluteY(Box.Box box)
    {
        var y = 0d;
        for (var b = box; b is not null; b = b.Parent)
            y += b.Frame.Y;
        return y;
    }
}
