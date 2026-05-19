using FluentAssertions;
using Tessera.Css.Parser;
using Tessera.Css.Properties;
using Tessera.Css.Values;
using Xunit;
using Starling.Spec;

namespace Tessera.Css.Tests;

[Spec("css-grid-2", "https://www.w3.org/TR/css-grid-2/")]

public sealed class GridPropertyTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    [Fact]
    public void Grid_template_columns_parses_track_list_as_value_list()
    {
        // TODO(lane-A): once `fr` unit lands, expect CssLength with Fr unit.
        var decls = Expand("grid-template-columns: 100px 1fr 2fr;");

        var value = decls.Single(d => d.Id == PropertyId.GridTemplateColumns).Value;
        value.Should().BeOfType<CssValueList>();
        var list = (CssValueList)value;
        list.Values.Should().HaveCount(3);
        list.Values[0].Should().Be(new CssLength(100, CssLengthUnit.Px));
    }

    [Fact]
    public void Grid_template_columns_recognises_repeat_function()
    {
        var decls = Expand("grid-template-columns: repeat(3, 1fr);");

        var value = decls.Single(d => d.Id == PropertyId.GridTemplateColumns).Value;
        value.Should().BeOfType<CssFunctionValue>();
        ((CssFunctionValue)value).Name.Should().Be("repeat");
    }

    [Fact]
    public void Grid_template_columns_recognises_minmax_function()
    {
        var decls = Expand("grid-template-columns: minmax(100px, 1fr);");

        var value = decls.Single(d => d.Id == PropertyId.GridTemplateColumns).Value;
        value.Should().BeOfType<CssFunctionValue>();
        ((CssFunctionValue)value).Name.Should().Be("minmax");
    }

    [Fact]
    public void Grid_area_with_one_value_copies_to_all_four()
    {
        var decls = Expand("grid-area: header;");

        decls.Single(d => d.Id == PropertyId.GridRowStart).Value.Should().Be(new CssKeyword("header"));
        decls.Single(d => d.Id == PropertyId.GridColumnStart).Value.Should().Be(new CssKeyword("header"));
        decls.Single(d => d.Id == PropertyId.GridRowEnd).Value.Should().Be(new CssKeyword("header"));
        decls.Single(d => d.Id == PropertyId.GridColumnEnd).Value.Should().Be(new CssKeyword("header"));
    }

    [Fact]
    public void Grid_area_with_four_slash_separated_values()
    {
        var decls = Expand("grid-area: 1 / 2 / 3 / 4;");

        decls.Single(d => d.Id == PropertyId.GridRowStart).Value.Should().Be(new CssNumber(1));
        decls.Single(d => d.Id == PropertyId.GridColumnStart).Value.Should().Be(new CssNumber(2));
        decls.Single(d => d.Id == PropertyId.GridRowEnd).Value.Should().Be(new CssNumber(3));
        decls.Single(d => d.Id == PropertyId.GridColumnEnd).Value.Should().Be(new CssNumber(4));
    }

    [Fact]
    public void Grid_column_splits_start_and_end_on_slash()
    {
        var decls = Expand("grid-column: 1 / 3;");

        decls.Single(d => d.Id == PropertyId.GridColumnStart).Value.Should().Be(new CssNumber(1));
        decls.Single(d => d.Id == PropertyId.GridColumnEnd).Value.Should().Be(new CssNumber(3));
    }

    [Fact]
    public void Grid_row_splits_start_and_end_on_slash()
    {
        var decls = Expand("grid-row: 2 / span 3;");

        decls.Single(d => d.Id == PropertyId.GridRowStart).Value.Should().Be(new CssNumber(2));
        decls.Single(d => d.Id == PropertyId.GridRowEnd).Value.Should().BeOfType<CssValueList>();
    }

    [Fact]
    public void Place_content_expands_to_align_content_and_justify_content()
    {
        var decls = Expand("place-content: space-between center;");

        decls.Single(d => d.Id == PropertyId.AlignContent).Value.Should().Be(new CssKeyword("space-between"));
        decls.Single(d => d.Id == PropertyId.JustifyContent).Value.Should().Be(new CssKeyword("center"));
    }

    [Fact]
    public void Place_self_with_single_value_duplicates()
    {
        var decls = Expand("place-self: center;");

        decls.Single(d => d.Id == PropertyId.AlignSelf).Value.Should().Be(new CssKeyword("center"));
        decls.Single(d => d.Id == PropertyId.JustifySelf).Value.Should().Be(new CssKeyword("center"));
    }

    [Fact]
    public void Grid_auto_flow_parses_keyword()
    {
        var decls = Expand("grid-auto-flow: row dense;");

        decls.Should().ContainSingle();
        decls[0].Id.Should().Be(PropertyId.GridAutoFlow);
    }
}
