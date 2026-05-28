using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssFonts4;

/// <summary>
/// Conformance tests for the <c>font-weight</c> property.
/// Spec: <see href="https://www.w3.org/TR/css-fonts-4/#propdef-font-weight">CSS Fonts 4 §4.3</see>.
/// </summary>
[TestClass]
[Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#propdef-font-weight")]
public sealed class FontWeightTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    // ── absolute keywords ─────────────────────────────────────────────────

    [SpecFact]
    public void Keyword_normal()
    {
        var decls = Expand("font-weight: normal;");
        decls.Should().ContainSingle();
        decls[0].Id.Should().Be(PropertyId.FontWeight);
        decls[0].Value.Should().Be(new CssKeyword("normal"));
    }

    [SpecFact]
    public void Keyword_bold()
    {
        var decls = Expand("font-weight: bold;");
        decls[0].Value.Should().Be(new CssKeyword("bold"));
    }

    // ── relative keywords ─────────────────────────────────────────────────

    [SpecFact]
    public void Keyword_bolder()
    {
        var decls = Expand("font-weight: bolder;");
        decls[0].Value.Should().Be(new CssKeyword("bolder"));
    }

    [SpecFact]
    public void Keyword_lighter()
    {
        var decls = Expand("font-weight: lighter;");
        decls[0].Value.Should().Be(new CssKeyword("lighter"));
    }

    // ── numeric (L3 values) ───────────────────────────────────────────────

    [SpecFact]
    public void Numeric_100()
    {
        var decls = Expand("font-weight: 100;");
        decls[0].Value.Should().Be(new CssNumber(100));
    }

    [SpecFact]
    public void Numeric_400()
    {
        var decls = Expand("font-weight: 400;");
        decls[0].Value.Should().Be(new CssNumber(400));
    }

    [SpecFact]
    public void Numeric_700()
    {
        var decls = Expand("font-weight: 700;");
        decls[0].Value.Should().Be(new CssNumber(700));
    }

    [SpecFact]
    public void Numeric_900()
    {
        var decls = Expand("font-weight: 900;");
        decls[0].Value.Should().Be(new CssNumber(900));
    }

    // ── CSS Fonts Level 4: any number in [1, 1000] ────────────────────────

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-weight-numeric-values")]
    public void L4_arbitrary_numeric_350()
    {
        // CSS Fonts 4 §4.3: any number in range [1,1000] is valid, not just multiples of 100.
        var decls = Expand("font-weight: 350;");
        decls[0].Value.Should().Be(new CssNumber(350));
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-weight-numeric-values")]
    public void L4_minimum_weight_1()
    {
        var decls = Expand("font-weight: 1;");
        decls[0].Value.Should().Be(new CssNumber(1));
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-weight-numeric-values")]
    public void L4_maximum_weight_1000()
    {
        var decls = Expand("font-weight: 1000;");
        decls[0].Value.Should().Be(new CssNumber(1000));
    }

    // ── property is inherited ─────────────────────────────────────────────

    [SpecFact]
    public void Font_weight_is_inherited()
    {
        PropertyRegistry.Inherits(PropertyId.FontWeight).Should().BeTrue();
    }

    // ── initial value ─────────────────────────────────────────────────────

    [SpecFact]
    public void Font_weight_initial_value_is_400()
    {
        // CSS Fonts 4 §4.3 — initial is `normal` which computes to 400.
        // Starling stores the computed numeric 400 directly.
        PropertyRegistry.InitialValue(PropertyId.FontWeight).Should().Be(new CssNumber(400));
    }
}
