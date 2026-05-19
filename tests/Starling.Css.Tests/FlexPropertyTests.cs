using FluentAssertions;
using Tessera.Css.Parser;
using Tessera.Css.Properties;
using Tessera.Css.Values;
using Xunit;
using Starling.Spec;

namespace Tessera.Css.Tests;

[Spec("css-flexbox-1", "https://www.w3.org/TR/css-flexbox-1/")]

public sealed class FlexPropertyTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    [Fact]
    public void Flex_shorthand_expands_to_grow_shrink_basis()
    {
        var decls = Expand("flex: 1 1 auto;");

        decls.Should().HaveCount(3);
        decls.Single(d => d.Id == PropertyId.FlexGrow).Value.Should().Be(new CssNumber(1));
        decls.Single(d => d.Id == PropertyId.FlexShrink).Value.Should().Be(new CssNumber(1));
        decls.Single(d => d.Id == PropertyId.FlexBasis).Value.Should().Be(new CssKeyword("auto"));
    }

    [Fact]
    public void Flex_none_expands_to_zero_zero_auto()
    {
        var decls = Expand("flex: none;");

        decls.Single(d => d.Id == PropertyId.FlexGrow).Value.Should().Be(new CssNumber(0));
        decls.Single(d => d.Id == PropertyId.FlexShrink).Value.Should().Be(new CssNumber(0));
        decls.Single(d => d.Id == PropertyId.FlexBasis).Value.Should().Be(new CssKeyword("auto"));
    }

    [Fact]
    public void Flex_auto_expands_to_one_one_auto()
    {
        var decls = Expand("flex: auto;");

        decls.Single(d => d.Id == PropertyId.FlexGrow).Value.Should().Be(new CssNumber(1));
        decls.Single(d => d.Id == PropertyId.FlexShrink).Value.Should().Be(new CssNumber(1));
        decls.Single(d => d.Id == PropertyId.FlexBasis).Value.Should().Be(new CssKeyword("auto"));
    }

    [Fact]
    public void Flex_single_number_sets_grow_and_zero_basis()
    {
        var decls = Expand("flex: 2;");

        decls.Single(d => d.Id == PropertyId.FlexGrow).Value.Should().Be(new CssNumber(2));
        decls.Single(d => d.Id == PropertyId.FlexBasis).Value.Should().Be(new CssLength(0, CssLengthUnit.Px));
    }

    [Fact]
    public void Flex_with_basis_length_assigns_basis()
    {
        var decls = Expand("flex: 0 1 200px;");

        decls.Single(d => d.Id == PropertyId.FlexGrow).Value.Should().Be(new CssNumber(0));
        decls.Single(d => d.Id == PropertyId.FlexShrink).Value.Should().Be(new CssNumber(1));
        decls.Single(d => d.Id == PropertyId.FlexBasis).Value.Should().Be(new CssLength(200, CssLengthUnit.Px));
    }

    [Fact]
    public void Flex_direction_parses_keywords()
    {
        var decls = Expand("flex-direction: row-reverse;");

        decls.Single().Id.Should().Be(PropertyId.FlexDirection);
        decls.Single().Value.Should().Be(new CssKeyword("row-reverse"));
    }

    [Fact]
    public void Flex_flow_shorthand_splits_direction_and_wrap()
    {
        var decls = Expand("flex-flow: column wrap;");

        decls.Single(d => d.Id == PropertyId.FlexDirection).Value.Should().Be(new CssKeyword("column"));
        decls.Single(d => d.Id == PropertyId.FlexWrap).Value.Should().Be(new CssKeyword("wrap"));
    }

    [Fact]
    public void Gap_shorthand_sets_row_and_column_gap()
    {
        var decls = Expand("gap: 8px 16px;");

        decls.Single(d => d.Id == PropertyId.RowGap).Value.Should().Be(new CssLength(8, CssLengthUnit.Px));
        decls.Single(d => d.Id == PropertyId.ColumnGap).Value.Should().Be(new CssLength(16, CssLengthUnit.Px));
    }

    [Fact]
    public void Gap_single_value_copies_to_both()
    {
        var decls = Expand("gap: 12px;");

        decls.Single(d => d.Id == PropertyId.RowGap).Value.Should().Be(new CssLength(12, CssLengthUnit.Px));
        decls.Single(d => d.Id == PropertyId.ColumnGap).Value.Should().Be(new CssLength(12, CssLengthUnit.Px));
    }

    [Fact]
    public void Place_items_shorthand_expands_align_and_justify()
    {
        var decls = Expand("place-items: center start;");

        decls.Single(d => d.Id == PropertyId.AlignItems).Value.Should().Be(new CssKeyword("center"));
        decls.Single(d => d.Id == PropertyId.JustifyItems).Value.Should().Be(new CssKeyword("start"));
    }

    [Fact]
    public void Order_parses_as_number()
    {
        var decls = Expand("order: -1;");

        decls.Single().Id.Should().Be(PropertyId.Order);
        decls.Single().Value.Should().Be(new CssNumber(-1));
    }
}
