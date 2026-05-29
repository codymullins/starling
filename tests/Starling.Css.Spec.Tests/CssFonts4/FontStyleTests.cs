using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssFonts4;

/// <summary>
/// Conformance tests for the <c>font-style</c> property.
/// Spec: <see href="https://www.w3.org/TR/css-fonts-4/#propdef-font-style">CSS Fonts 4 §4.2</see>.
/// </summary>
[TestClass]
[Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#propdef-font-style")]
public sealed class FontStyleTests
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
        var decls = Expand("font-style: normal;");
        decls.Should().ContainSingle();
        decls[0].Id.Should().Be(PropertyId.FontStyle);
        decls[0].Value.Should().Be(new CssKeyword("normal"));
    }

    [SpecFact]
    public void Keyword_italic()
    {
        var decls = Expand("font-style: italic;");
        decls[0].Value.Should().Be(new CssKeyword("italic"));
    }

    [SpecFact]
    public void Keyword_oblique_no_angle()
    {
        var decls = Expand("font-style: oblique;");
        decls[0].Value.Should().Be(new CssKeyword("oblique"));
    }

    // ── CSS Fonts Level 4: oblique with angle ─────────────────────────────

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-style-prop")]
    public void L4_oblique_with_angle_produces_keyword_and_angle()
    {
        // CSS Fonts 4 §4.2: `oblique <angle>` is valid.
        // The parser produces a CssValueList of [CssKeyword("oblique"), CssAngle(14deg)].
        var decls = Expand("font-style: oblique 14deg;");
        decls.Should().ContainSingle();
        decls[0].Id.Should().Be(PropertyId.FontStyle);

        var list = decls[0].Value.Should().BeOfType<CssValueList>().Subject;
        list.Values.Should().HaveCount(2);
        list.Values[0].Should().Be(new CssKeyword("oblique"));
        list.Values[1].Should().Be(new CssAngle(14, CssAngleUnit.Degrees));
    }

    // ── property is inherited ─────────────────────────────────────────────

    [SpecFact]
    public void Font_style_is_inherited()
    {
        PropertyRegistry.Inherits(PropertyId.FontStyle).Should().BeTrue();
    }

    // ── initial value ─────────────────────────────────────────────────────

    [SpecFact]
    public void Font_style_initial_value_is_normal()
    {
        PropertyRegistry.InitialValue(PropertyId.FontStyle).Should().Be(new CssKeyword("normal"));
    }
}
