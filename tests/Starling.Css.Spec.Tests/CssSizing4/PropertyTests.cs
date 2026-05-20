using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssSizing4;

/// <summary>
/// Property conformance for <see href="https://drafts.csswg.org/css-sizing-4/">CSS Box Sizing Module Level 4</see>.
/// </summary>
[TestClass]
[Spec("css-sizing-4", "https://drafts.csswg.org/css-sizing-4/")]
public sealed class PropertyTests
{

    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/css-sizing-4/#propdef-aspect-ratio"/>
    /// <para>Property <c>aspect-ratio</c> — value <c>auto || &lt;ratio&gt;</c>; initial <c>auto</c>.</para>
    /// </summary>
    [Spec("css-sizing-4", "https://drafts.csswg.org/css-sizing-4/#propdef-aspect-ratio")]
    [SpecFact]
    public void Parses_aspect_ratio()
    {
        var decls = Expand("aspect-ratio: 16 / 9;");
        var value = decls.Single(d => d.Id == PropertyId.AspectRatio).Value;
        value.Should().BeOfType<CssValueList>();
        var list = (CssValueList)value;
        list.Values.Should().HaveCount(3);
        list.Values[0].Should().Be(new CssNumber(16));
        list.Values[2].Should().Be(new CssNumber(9));
    }

}
