using FluentAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Spec;

namespace Starling.Css.Tests;

[Spec("css-grid-2", "https://www.w3.org/TR/css-grid-2/")]

[TestClass]
public sealed class GridPropertyTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    [TestMethod]
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

    [TestMethod]
    public void Grid_template_columns_recognises_repeat_function()
    {
        var decls = Expand("grid-template-columns: repeat(3, 1fr);");

        var value = decls.Single(d => d.Id == PropertyId.GridTemplateColumns).Value;
        value.Should().BeOfType<CssFunctionValue>();
        ((CssFunctionValue)value).Name.Should().Be("repeat");
    }

    [TestMethod]
    public void Grid_template_columns_recognises_minmax_function()
    {
        var decls = Expand("grid-template-columns: minmax(100px, 1fr);");

        var value = decls.Single(d => d.Id == PropertyId.GridTemplateColumns).Value;
        value.Should().BeOfType<CssFunctionValue>();
        ((CssFunctionValue)value).Name.Should().Be("minmax");
    }

    [TestMethod]
    public void Grid_area_with_one_value_copies_to_all_four()
    {
        var decls = Expand("grid-area: header;");

        decls.Single(d => d.Id == PropertyId.GridRowStart).Value.Should().Be(new CssKeyword("header"));
        decls.Single(d => d.Id == PropertyId.GridColumnStart).Value.Should().Be(new CssKeyword("header"));
        decls.Single(d => d.Id == PropertyId.GridRowEnd).Value.Should().Be(new CssKeyword("header"));
        decls.Single(d => d.Id == PropertyId.GridColumnEnd).Value.Should().Be(new CssKeyword("header"));
    }

    [TestMethod]
    public void Grid_area_with_four_slash_separated_values()
    {
        var decls = Expand("grid-area: 1 / 2 / 3 / 4;");

        decls.Single(d => d.Id == PropertyId.GridRowStart).Value.Should().Be(new CssNumber(1));
        decls.Single(d => d.Id == PropertyId.GridColumnStart).Value.Should().Be(new CssNumber(2));
        decls.Single(d => d.Id == PropertyId.GridRowEnd).Value.Should().Be(new CssNumber(3));
        decls.Single(d => d.Id == PropertyId.GridColumnEnd).Value.Should().Be(new CssNumber(4));
    }

    [TestMethod]
    public void Grid_column_splits_start_and_end_on_slash()
    {
        var decls = Expand("grid-column: 1 / 3;");

        decls.Single(d => d.Id == PropertyId.GridColumnStart).Value.Should().Be(new CssNumber(1));
        decls.Single(d => d.Id == PropertyId.GridColumnEnd).Value.Should().Be(new CssNumber(3));
    }

    [TestMethod]
    public void Grid_row_splits_start_and_end_on_slash()
    {
        var decls = Expand("grid-row: 2 / span 3;");

        decls.Single(d => d.Id == PropertyId.GridRowStart).Value.Should().Be(new CssNumber(2));
        decls.Single(d => d.Id == PropertyId.GridRowEnd).Value.Should().BeOfType<CssValueList>();
    }

    [TestMethod]
    public void Place_content_expands_to_align_content_and_justify_content()
    {
        var decls = Expand("place-content: space-between center;");

        decls.Single(d => d.Id == PropertyId.AlignContent).Value.Should().Be(new CssKeyword("space-between"));
        decls.Single(d => d.Id == PropertyId.JustifyContent).Value.Should().Be(new CssKeyword("center"));
    }

    [TestMethod]
    public void Place_self_with_single_value_duplicates()
    {
        var decls = Expand("place-self: center;");

        decls.Single(d => d.Id == PropertyId.AlignSelf).Value.Should().Be(new CssKeyword("center"));
        decls.Single(d => d.Id == PropertyId.JustifySelf).Value.Should().Be(new CssKeyword("center"));
    }

    [TestMethod]
    public void Grid_auto_flow_parses_keyword()
    {
        var decls = Expand("grid-auto-flow: row dense;");

        decls.Should().ContainSingle();
        decls[0].Id.Should().Be(PropertyId.GridAutoFlow);
    }
}
