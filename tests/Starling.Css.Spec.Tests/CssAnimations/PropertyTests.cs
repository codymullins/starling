using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssAnimations;

/// <summary>
/// Property conformance for <see href="https://drafts.csswg.org/css-animations-1/">CSS Animations Module Level 1</see>.
/// </summary>
[TestClass]
[Spec("css-animations", "https://drafts.csswg.org/css-animations-1/")]
public sealed class PropertyTests
{

    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/css-animations-1/#propdef-animation-name"/>
    /// <para>Property <c>animation-name</c> — value <c>[ none | &lt;keyframes-name&gt; ]#</c>; initial <c>none</c>.</para>
    /// </summary>
    [Spec("css-animations", "https://drafts.csswg.org/css-animations-1/#propdef-animation-name")]
    [SpecFact]
    public void Parses_animation_name()
    {
        var decls = Expand("animation-name: fade;");
        decls.Single().Id.Should().Be(PropertyId.AnimationName);
        decls.Single().Value.Should().Be(new CssKeyword("fade"));
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/css-animations-1/#propdef-animation-duration"/>
    /// <para>Property <c>animation-duration</c> — value <c>&lt;time [0s,∞]&gt;#</c>; initial <c>0s</c>.</para>
    /// </summary>
    [Spec("css-animations", "https://drafts.csswg.org/css-animations-1/#propdef-animation-duration")]
    [SpecFact]
    public void Parses_animation_duration()
    {
        var decls = Expand("animation-duration: 1s;");
        decls.Single().Id.Should().Be(PropertyId.AnimationDuration);
        ((CssTime)decls.Single().Value).InSeconds.Should().Be(1);
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/css-animations-1/#propdef-animation-timing-function"/>
    /// <para>Property <c>animation-timing-function</c> — value <c>&lt;easing-function&gt;#</c>; initial <c>ease</c>.</para>
    /// </summary>
    [Spec("css-animations", "https://drafts.csswg.org/css-animations-1/#propdef-animation-timing-function")]
    [SpecFact]
    public void Parses_animation_timing_function()
    {
        var decls = Expand("animation-timing-function: linear;");
        decls.Single().Id.Should().Be(PropertyId.AnimationTimingFunction);
        decls.Single().Value.Should().Be(new CssKeyword("linear"));
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/css-animations-1/#propdef-animation"/>
    /// <para>Property <c>animation</c> — value <c>&lt;single-animation&gt;#</c>; initial <c>see individual properties</c>.</para>
    /// </summary>
    [Spec("css-animations", "https://drafts.csswg.org/css-animations-1/#propdef-animation")]
    [SpecFact]
    public void Parses_animation()
    {
        var decls = Expand("animation: fade 1s linear infinite;");
        decls.Single(d => d.Id == PropertyId.AnimationName).Value.Should().Be(new CssKeyword("fade"));
        ((CssTime)decls.Single(d => d.Id == PropertyId.AnimationDuration).Value).InSeconds.Should().Be(1);
        decls.Single(d => d.Id == PropertyId.AnimationTimingFunction).Value.Should().Be(new CssKeyword("linear"));
        decls.Single(d => d.Id == PropertyId.AnimationIterationCount).Value.Should().Be(new CssKeyword("infinite"));
    }
}
