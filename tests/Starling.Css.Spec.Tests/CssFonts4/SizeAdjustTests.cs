using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssFonts4;

/// <summary>
/// Conformance tests for the <c>size-adjust</c> descriptor / property.
/// Spec: <see href="https://www.w3.org/TR/css-fonts-4/#descdef-font-face-size-adjust">CSS Fonts 4 §9.6</see>.
/// </summary>
[TestClass]
[Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#descdef-font-face-size-adjust")]
public sealed class SizeAdjustTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    // size-adjust is registered in PropertyId/PropertyRegistry (initial 100%,
    // not inherited) and is accepted inside @font-face by the parser.

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#descdef-font-face-size-adjust")]
    public void Size_adjust_percentage_parses_as_property()
    {
        // CSS Fonts 4 §9.6: as a property, size-adjust accepts a <percentage>.
        // `size-adjust: 120%` should produce one declaration with a CssPercentage.
        var decls = Expand("size-adjust: 120%;");
        decls.Should().ContainSingle();
        decls[0].Value.Should().Be(new CssPercentage(120));
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#descdef-font-face-size-adjust")]
    public void Size_adjust_in_font_face_descriptor_is_parsed()
    {
        // CSS Fonts 4 §9.6: `size-adjust` may appear inside @font-face and
        // scales the font's em square. FontFaceParser currently ignores it.
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: "Adjusted";
                src: url("adjusted.woff2");
                size-adjust: 120%;
            }
            """);
        // Once implemented: the rule should be parsed and size-adjust exposed.
        var rules = Starling.Css.FontFace.FontFaceParser.ParseAll(sheet).ToList();
        rules.Should().ContainSingle();
        // No SizeAdjust property on FontFaceRule yet — this is the pending gap.
    }
}
