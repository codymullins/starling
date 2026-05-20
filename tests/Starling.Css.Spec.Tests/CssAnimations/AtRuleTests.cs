using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Animations;

namespace Starling.Css.Spec.Tests.CssAnimations;

/// <summary>
/// At-rule conformance for <see href="https://drafts.csswg.org/css-animations-1/">CSS Animations Module Level 1</see>.
/// </summary>
[TestClass]
[Spec("css-animations", "https://drafts.csswg.org/css-animations-1/")]
public sealed class AtRuleTests
{

    /// <summary>Spec: <see href="https://drafts.csswg.org/css-animations-1/#at-ruledef-keyframes"/>
    /// <para>At-rule <c>@keyframes</c>.</para>
    /// </summary>
    [Spec("css-animations", "https://drafts.csswg.org/css-animations-1/#at-ruledef-keyframes")]
    [SpecFact]
    public void Parses_at_keyframes()
    {
        var sheet = new CssParser("@keyframes fade { from { opacity: 0 } to { opacity: 1 } }").ParseStyleSheet();
        var rules = KeyframesParser.ParseAll(sheet).ToList();
        rules.Should().ContainSingle();
        var k = rules[0];
        k.Name.Should().Be("fade");
        k.Frames.Select(f => f.Offset).Should().Equal(0.0, 1.0);
        k.Frames[0].Declarations.Should().ContainSingle()
            .Which.Property.Should().Be("opacity");
        k.Frames[1].Declarations[0].Value.Should().BeOfType<Starling.Css.Values.CssNumber>()
            .Which.Value.Should().Be(1);
    }
}
