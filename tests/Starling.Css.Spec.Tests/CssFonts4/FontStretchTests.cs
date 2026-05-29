using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssFonts4;

/// <summary>
/// Conformance tests for the <c>font-stretch</c> property.
/// Spec: <see href="https://www.w3.org/TR/css-fonts-4/#propdef-font-stretch">CSS Fonts 4 §4.4</see>.
/// </summary>
[TestClass]
[Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#propdef-font-stretch")]
public sealed class FontStretchTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    // ── keywords ──────────────────────────────────────────────────────────

    [SpecFact]
    public void Keyword_normal()
    {
        var decls = Expand("font-stretch: normal;");
        decls.Should().ContainSingle();
        decls[0].Id.Should().Be(PropertyId.FontStretch);
        decls[0].Value.Should().Be(new CssKeyword("normal"));
    }

    [SpecFact]
    public void Keyword_condensed()
    {
        var decls = Expand("font-stretch: condensed;");
        decls[0].Value.Should().Be(new CssKeyword("condensed"));
    }

    [SpecFact]
    public void Keyword_expanded()
    {
        var decls = Expand("font-stretch: expanded;");
        decls[0].Value.Should().Be(new CssKeyword("expanded"));
    }

    [SpecFact]
    public void Keyword_ultra_condensed()
    {
        var decls = Expand("font-stretch: ultra-condensed;");
        decls[0].Value.Should().Be(new CssKeyword("ultra-condensed"));
    }

    [SpecFact]
    public void Keyword_extra_condensed()
    {
        var decls = Expand("font-stretch: extra-condensed;");
        decls[0].Value.Should().Be(new CssKeyword("extra-condensed"));
    }

    [SpecFact]
    public void Keyword_semi_condensed()
    {
        var decls = Expand("font-stretch: semi-condensed;");
        decls[0].Value.Should().Be(new CssKeyword("semi-condensed"));
    }

    [SpecFact]
    public void Keyword_semi_expanded()
    {
        var decls = Expand("font-stretch: semi-expanded;");
        decls[0].Value.Should().Be(new CssKeyword("semi-expanded"));
    }

    [SpecFact]
    public void Keyword_extra_expanded()
    {
        var decls = Expand("font-stretch: extra-expanded;");
        decls[0].Value.Should().Be(new CssKeyword("extra-expanded"));
    }

    [SpecFact]
    public void Keyword_ultra_expanded()
    {
        var decls = Expand("font-stretch: ultra-expanded;");
        decls[0].Value.Should().Be(new CssKeyword("ultra-expanded"));
    }

    // ── CSS Fonts Level 4: percentage value ───────────────────────────────

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-stretch-prop")]
    public void L4_percentage_80()
    {
        // CSS Fonts 4 §4.4: font-stretch accepts a percentage in addition to keywords.
        var decls = Expand("font-stretch: 80%;");
        decls[0].Value.Should().Be(new CssPercentage(80));
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-stretch-prop")]
    public void L4_percentage_100()
    {
        var decls = Expand("font-stretch: 100%;");
        decls[0].Value.Should().Be(new CssPercentage(100));
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-stretch-prop")]
    public void L4_percentage_125()
    {
        var decls = Expand("font-stretch: 125%;");
        decls[0].Value.Should().Be(new CssPercentage(125));
    }

    // ── property is inherited ─────────────────────────────────────────────

    [SpecFact]
    public void Font_stretch_is_inherited()
    {
        PropertyRegistry.Inherits(PropertyId.FontStretch).Should().BeTrue();
    }

    // ── initial value ─────────────────────────────────────────────────────

    [SpecFact]
    public void Font_stretch_initial_value_is_normal()
    {
        PropertyRegistry.InitialValue(PropertyId.FontStretch).Should().Be(new CssKeyword("normal"));
    }
}
