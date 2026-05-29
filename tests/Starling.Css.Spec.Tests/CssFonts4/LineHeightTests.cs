using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssFonts4;

/// <summary>
/// Conformance tests for the <c>line-height</c> property.
/// Spec: <see href="https://www.w3.org/TR/css-fonts-4/#propdef-line-height">CSS Fonts 4 §4.7</see>
/// (also CSS Inline 3 §3.3).
/// </summary>
[TestClass]
[Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#propdef-line-height")]
public sealed class LineHeightTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    // ── normal ────────────────────────────────────────────────────────────

    [SpecFact]
    public void Keyword_normal()
    {
        var decls = Expand("line-height: normal;");
        decls.Should().ContainSingle();
        decls[0].Id.Should().Be(PropertyId.LineHeight);
        decls[0].Value.Should().Be(new CssKeyword("normal"));
    }

    // ── number (unitless, multiplier of font-size) ────────────────────────

    [SpecFact]
    public void Number_1_5()
    {
        var decls = Expand("line-height: 1.5;");
        decls[0].Value.Should().Be(new CssNumber(1.5));
    }

    [SpecFact]
    public void Number_1()
    {
        var decls = Expand("line-height: 1;");
        decls[0].Value.Should().Be(new CssNumber(1));
    }

    [SpecFact]
    public void Number_2()
    {
        var decls = Expand("line-height: 2;");
        decls[0].Value.Should().Be(new CssNumber(2));
    }

    // ── length ────────────────────────────────────────────────────────────

    [SpecFact]
    public void Length_em()
    {
        var decls = Expand("line-height: 1.2em;");
        decls[0].Value.Should().Be(new CssLength(1.2, CssLengthUnit.Em));
    }

    [SpecFact]
    public void Length_px()
    {
        var decls = Expand("line-height: 24px;");
        decls[0].Value.Should().Be(new CssLength(24, CssLengthUnit.Px));
    }

    // ── percentage ────────────────────────────────────────────────────────

    [SpecFact]
    public void Percentage_150()
    {
        var decls = Expand("line-height: 150%;");
        decls[0].Value.Should().Be(new CssPercentage(150));
    }

    // ── property is inherited ─────────────────────────────────────────────

    [SpecFact]
    public void Line_height_is_inherited()
    {
        PropertyRegistry.Inherits(PropertyId.LineHeight).Should().BeTrue();
    }

    // ── initial value ─────────────────────────────────────────────────────

    [SpecFact]
    public void Line_height_initial_value_is_normal()
    {
        PropertyRegistry.InitialValue(PropertyId.LineHeight).Should().Be(new CssKeyword("normal"));
    }
}
