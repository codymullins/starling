using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssMulticol1;

/// <summary>
/// Property + cascade conformance for
/// <see href="https://www.w3.org/TR/css-multicol-1/">CSS Multi-column Layout Module Level 1</see>.
/// Parse + cascade level only — actual column fragmentation is not yet implemented.
/// </summary>
[TestClass]
[Spec("css-multicol-1", "https://www.w3.org/TR/css-multicol-1/")]
public sealed class MulticolTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    private static CssValue ValueOf(string css, PropertyId id)
        => Expand(css).Single(d => d.Id == id).Value;

    // ----- column-count / column-width (§3, §2) -----

    [Spec("css-multicol-1", "https://www.w3.org/TR/css-multicol-1/#cc", section: "3.2")]
    [SpecFact]
    public void Column_count_parses_auto_and_integer()
    {
        ValueOf("column-count: auto", PropertyId.ColumnCount).Should().Be(new CssKeyword("auto"));
        ValueOf("column-count: 3", PropertyId.ColumnCount).Should().Be(new CssNumber(3));
    }

    [Spec("css-multicol-1", "https://www.w3.org/TR/css-multicol-1/#cw", section: "3.1")]
    [SpecFact]
    public void Column_width_parses_auto_and_length()
    {
        ValueOf("column-width: auto", PropertyId.ColumnWidth).Should().Be(new CssKeyword("auto"));
        ValueOf("column-width: 12em", PropertyId.ColumnWidth).Should().Be(new CssLength(12, CssLengthUnit.Em));
    }

    // ----- columns shorthand (§7.1) -----

    [Spec("css-multicol-1", "https://www.w3.org/TR/css-multicol-1/#columns", section: "7.1")]
    [SpecFact]
    public void Columns_shorthand_sets_width_and_count()
    {
        var decls = Expand("columns: 200px 3");
        decls.Single(d => d.Id == PropertyId.ColumnWidth).Value.Should().Be(new CssLength(200, CssLengthUnit.Px));
        decls.Single(d => d.Id == PropertyId.ColumnCount).Value.Should().Be(new CssNumber(3));
    }

    [Spec("css-multicol-1", "https://www.w3.org/TR/css-multicol-1/#columns", section: "7.1")]
    [SpecFact]
    public void Columns_shorthand_single_count_resets_width_to_auto()
    {
        var decls = Expand("columns: 4");
        decls.Single(d => d.Id == PropertyId.ColumnCount).Value.Should().Be(new CssNumber(4));
        decls.Single(d => d.Id == PropertyId.ColumnWidth).Value.Should().Be(new CssKeyword("auto"));
    }

    // ----- column-rule shorthand + longhands (§6) -----

    [Spec("css-multicol-1", "https://www.w3.org/TR/css-multicol-1/#column-rule", section: "6.4")]
    [SpecFact]
    public void Column_rule_shorthand_sets_width_style_color()
    {
        var decls = Expand("column-rule: 2px dotted blue");
        decls.Single(d => d.Id == PropertyId.ColumnRuleWidth).Value.Should().Be(new CssLength(2, CssLengthUnit.Px));
        decls.Single(d => d.Id == PropertyId.ColumnRuleStyle).Value.Should().Be(new CssKeyword("dotted"));
        decls.Single(d => d.Id == PropertyId.ColumnRuleColor).Value.Should().BeOfType<Starling.Css.Values.CssColor>();
    }

    [Spec("css-multicol-1", "https://www.w3.org/TR/css-multicol-1/#crc", section: "6.3")]
    [SpecFact]
    public void Column_rule_color_longhand_parses()
        => ValueOf("column-rule-color: red", PropertyId.ColumnRuleColor).Should().BeOfType<Starling.Css.Values.CssColor>();

    // ----- column-span / column-fill (§8, §7.2) -----

    [Spec("css-multicol-1", "https://www.w3.org/TR/css-multicol-1/#column-span", section: "8")]
    [SpecFact]
    public void Column_span_parses_none_and_all()
    {
        ValueOf("column-span: none", PropertyId.ColumnSpan).Should().Be(new CssKeyword("none"));
        ValueOf("column-span: all", PropertyId.ColumnSpan).Should().Be(new CssKeyword("all"));
    }

    [Spec("css-multicol-1", "https://www.w3.org/TR/css-multicol-1/#column-fill", section: "7.2")]
    [SpecFact]
    public void Column_fill_parses_auto_and_balance()
    {
        ValueOf("column-fill: auto", PropertyId.ColumnFill).Should().Be(new CssKeyword("auto"));
        ValueOf("column-fill: balance", PropertyId.ColumnFill).Should().Be(new CssKeyword("balance"));
    }

    // ----- initial values + inheritance (§3-§8) -----

    [Spec("css-multicol-1", "https://www.w3.org/TR/css-multicol-1/", section: "3")]
    [SpecFact]
    public void Initial_values_match_spec()
    {
        PropertyRegistry.InitialValue(PropertyId.ColumnCount).Should().Be(new CssKeyword("auto"));
        PropertyRegistry.InitialValue(PropertyId.ColumnWidth).Should().Be(new CssKeyword("auto"));
        PropertyRegistry.InitialValue(PropertyId.ColumnRuleStyle).Should().Be(new CssKeyword("none"));
        PropertyRegistry.InitialValue(PropertyId.ColumnSpan).Should().Be(new CssKeyword("none"));
        PropertyRegistry.InitialValue(PropertyId.ColumnFill).Should().Be(new CssKeyword("balance"));
    }

    [Spec("css-multicol-1", "https://www.w3.org/TR/css-multicol-1/", section: "3")]
    [SpecFact]
    public void Multicol_properties_are_not_inherited()
    {
        PropertyRegistry.Inherits(PropertyId.ColumnCount).Should().BeFalse();
        PropertyRegistry.Inherits(PropertyId.ColumnWidth).Should().BeFalse();
        PropertyRegistry.Inherits(PropertyId.ColumnRuleColor).Should().BeFalse();
        PropertyRegistry.Inherits(PropertyId.ColumnSpan).Should().BeFalse();
    }
}
