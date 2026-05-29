using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssFonts4;

/// <summary>
/// Conformance tests for the <c>font-size</c> property.
/// Spec: <see href="https://www.w3.org/TR/css-fonts-4/#propdef-font-size">CSS Fonts 4 §4.5</see>.
/// </summary>
[TestClass]
[Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#propdef-font-size")]
public sealed class FontSizeTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    // ── absolute size keywords ────────────────────────────────────────────

    [SpecFact]
    public void Keyword_xx_small()
    {
        var decls = Expand("font-size: xx-small;");
        decls.Should().ContainSingle();
        decls[0].Id.Should().Be(PropertyId.FontSize);
        decls[0].Value.Should().Be(new CssKeyword("xx-small"));
    }

    [SpecFact]
    public void Keyword_x_small()
    {
        var decls = Expand("font-size: x-small;");
        decls[0].Value.Should().Be(new CssKeyword("x-small"));
    }

    [SpecFact]
    public void Keyword_small()
    {
        var decls = Expand("font-size: small;");
        decls[0].Value.Should().Be(new CssKeyword("small"));
    }

    [SpecFact]
    public void Keyword_medium()
    {
        var decls = Expand("font-size: medium;");
        decls[0].Value.Should().Be(new CssKeyword("medium"));
    }

    [SpecFact]
    public void Keyword_large()
    {
        var decls = Expand("font-size: large;");
        decls[0].Value.Should().Be(new CssKeyword("large"));
    }

    [SpecFact]
    public void Keyword_x_large()
    {
        var decls = Expand("font-size: x-large;");
        decls[0].Value.Should().Be(new CssKeyword("x-large"));
    }

    [SpecFact]
    public void Keyword_xx_large()
    {
        var decls = Expand("font-size: xx-large;");
        decls[0].Value.Should().Be(new CssKeyword("xx-large"));
    }

    [SpecFact]
    public void Keyword_xxx_large()
    {
        var decls = Expand("font-size: xxx-large;");
        decls[0].Value.Should().Be(new CssKeyword("xxx-large"));
    }

    // ── relative size keywords ────────────────────────────────────────────

    [SpecFact]
    public void Keyword_smaller()
    {
        var decls = Expand("font-size: smaller;");
        decls[0].Value.Should().Be(new CssKeyword("smaller"));
    }

    [SpecFact]
    public void Keyword_larger()
    {
        var decls = Expand("font-size: larger;");
        decls[0].Value.Should().Be(new CssKeyword("larger"));
    }

    // ── length values ─────────────────────────────────────────────────────

    [SpecFact]
    public void Length_px()
    {
        var decls = Expand("font-size: 16px;");
        decls[0].Value.Should().Be(new CssLength(16, CssLengthUnit.Px));
    }

    [SpecFact]
    public void Length_em()
    {
        var decls = Expand("font-size: 2em;");
        decls[0].Value.Should().Be(new CssLength(2, CssLengthUnit.Em));
    }

    [SpecFact]
    public void Length_rem()
    {
        var decls = Expand("font-size: 1rem;");
        decls[0].Value.Should().Be(new CssLength(1, CssLengthUnit.Rem));
    }

    [SpecFact]
    public void Length_pt()
    {
        var decls = Expand("font-size: 12pt;");
        decls[0].Value.Should().Be(new CssLength(12, CssLengthUnit.Pt));
    }

    // ── percentage ────────────────────────────────────────────────────────

    [SpecFact]
    public void Percentage_100()
    {
        var decls = Expand("font-size: 100%;");
        decls[0].Value.Should().Be(new CssPercentage(100));
    }

    [SpecFact]
    public void Percentage_85()
    {
        var decls = Expand("font-size: 85%;");
        decls[0].Value.Should().Be(new CssPercentage(85));
    }

    // ── property is inherited ─────────────────────────────────────────────

    [SpecFact]
    public void Font_size_is_inherited()
    {
        PropertyRegistry.Inherits(PropertyId.FontSize).Should().BeTrue();
    }

    // ── initial value ─────────────────────────────────────────────────────

    [SpecFact]
    public void Font_size_initial_value_is_16px()
    {
        // CSS Fonts 4 §4.5 — initial is `medium`; Starling resolves to 16px.
        PropertyRegistry.InitialValue(PropertyId.FontSize).Should().Be(new CssLength(16, CssLengthUnit.Px));
    }
}
