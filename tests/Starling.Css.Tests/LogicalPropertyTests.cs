using FluentAssertions;
using Tessera.Css.Parser;
using Tessera.Css.Properties;
using Tessera.Css.Values;
using Xunit;
using Starling.Spec;

namespace Tessera.Css.Tests;

[Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/")]

public sealed class LogicalPropertyTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    [Fact]
    public void Margin_inline_start_round_trips()
    {
        PropertyRegistry.TryGetPropertyId("margin-inline-start", out var id).Should().BeTrue();
        id.Should().Be(PropertyId.MarginInlineStart);
        PropertyRegistry.Name(PropertyId.MarginInlineStart).Should().Be("margin-inline-start");
    }

    [Fact]
    public void Margin_inline_start_parses_length()
    {
        var decls = Expand("margin-inline-start: 12px;");

        decls.Single().Id.Should().Be(PropertyId.MarginInlineStart);
        decls.Single().Value.Should().Be(new CssLength(12, CssLengthUnit.Px));
    }

    [Fact]
    public void Margin_inline_shorthand_splits_start_and_end()
    {
        var decls = Expand("margin-inline: 4px 8px;");

        decls.Single(d => d.Id == PropertyId.MarginInlineStart).Value.Should().Be(new CssLength(4, CssLengthUnit.Px));
        decls.Single(d => d.Id == PropertyId.MarginInlineEnd).Value.Should().Be(new CssLength(8, CssLengthUnit.Px));
    }

    [Fact]
    public void Margin_block_shorthand_with_one_value_duplicates()
    {
        var decls = Expand("margin-block: 10px;");

        decls.Single(d => d.Id == PropertyId.MarginBlockStart).Value.Should().Be(new CssLength(10, CssLengthUnit.Px));
        decls.Single(d => d.Id == PropertyId.MarginBlockEnd).Value.Should().Be(new CssLength(10, CssLengthUnit.Px));
    }

    [Fact]
    public void Padding_inline_shorthand_expands()
    {
        var decls = Expand("padding-inline: 1rem;");

        decls.Single(d => d.Id == PropertyId.PaddingInlineStart).Value.Should().Be(new CssLength(1, CssLengthUnit.Rem));
        decls.Single(d => d.Id == PropertyId.PaddingInlineEnd).Value.Should().Be(new CssLength(1, CssLengthUnit.Rem));
    }

    [Fact]
    public void Inset_shorthand_uses_4_value_box_pattern_on_physical_sides()
    {
        var decls = Expand("inset: 1px 2px 3px 4px;");

        decls.Single(d => d.Id == PropertyId.Top).Value.Should().Be(new CssLength(1, CssLengthUnit.Px));
        decls.Single(d => d.Id == PropertyId.Right).Value.Should().Be(new CssLength(2, CssLengthUnit.Px));
        decls.Single(d => d.Id == PropertyId.Bottom).Value.Should().Be(new CssLength(3, CssLengthUnit.Px));
        decls.Single(d => d.Id == PropertyId.Left).Value.Should().Be(new CssLength(4, CssLengthUnit.Px));
    }

    [Fact]
    public void Inset_inline_shorthand_splits_start_end()
    {
        var decls = Expand("inset-inline: 5px 10px;");

        decls.Single(d => d.Id == PropertyId.InsetInlineStart).Value.Should().Be(new CssLength(5, CssLengthUnit.Px));
        decls.Single(d => d.Id == PropertyId.InsetInlineEnd).Value.Should().Be(new CssLength(10, CssLengthUnit.Px));
    }

    [Fact]
    public void Border_inline_start_shorthand_assigns_width_style_color()
    {
        var decls = Expand("border-inline-start: 2px solid red;");

        decls.Single(d => d.Id == PropertyId.BorderInlineStartWidth).Value.Should().Be(new CssLength(2, CssLengthUnit.Px));
        decls.Single(d => d.Id == PropertyId.BorderInlineStartStyle).Value.Should().Be(new CssKeyword("solid"));
        decls.Single(d => d.Id == PropertyId.BorderInlineStartColor).Value.Should().BeOfType<CssColor>();
    }

    [Fact]
    public void Border_inline_shorthand_assigns_both_start_and_end()
    {
        var decls = Expand("border-inline: 1px dashed blue;");

        decls.Any(d => d.Id == PropertyId.BorderInlineStartWidth).Should().BeTrue();
        decls.Any(d => d.Id == PropertyId.BorderInlineEndWidth).Should().BeTrue();
        decls.Single(d => d.Id == PropertyId.BorderInlineStartStyle).Value.Should().Be(new CssKeyword("dashed"));
        decls.Single(d => d.Id == PropertyId.BorderInlineEndStyle).Value.Should().Be(new CssKeyword("dashed"));
    }

    [Fact]
    public void Logical_corner_radius_round_trips()
    {
        PropertyRegistry.Name(PropertyId.BorderStartStartRadius).Should().Be("border-start-start-radius");
        PropertyRegistry.Name(PropertyId.BorderEndEndRadius).Should().Be("border-end-end-radius");

        var decls = Expand("border-start-start-radius: 4px;");

        decls.Single().Id.Should().Be(PropertyId.BorderStartStartRadius);
        decls.Single().Value.Should().Be(new CssLength(4, CssLengthUnit.Px));
    }

    [Fact]
    public void Inline_and_block_size_round_trip()
    {
        var decls = Expand("inline-size: 200px; block-size: 100px;");

        decls.Single(d => d.Id == PropertyId.InlineSize).Value.Should().Be(new CssLength(200, CssLengthUnit.Px));
        decls.Single(d => d.Id == PropertyId.BlockSize).Value.Should().Be(new CssLength(100, CssLengthUnit.Px));
    }

    [Fact]
    public void Writing_mode_inherits()
    {
        PropertyRegistry.Inherits(PropertyId.WritingMode).Should().BeTrue();
        PropertyRegistry.Inherits(PropertyId.Direction).Should().BeTrue();
    }
}
