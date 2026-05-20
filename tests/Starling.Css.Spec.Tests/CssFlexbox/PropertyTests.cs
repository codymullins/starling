using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssFlexbox;

/// <summary>
/// Property conformance for <see href="https://drafts.csswg.org/css-flexbox-2/">CSS Flexible Box Layout Module Level 2</see>.
/// </summary>
[TestClass]
[Spec("css-flexbox", "https://drafts.csswg.org/css-flexbox-2/")]
public sealed class PropertyTests
{

    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/css-flexbox-2/#propdef-flex-direction"/>
    /// <para>Property <c>flex-direction</c> — value <c>row | row-reverse | column | column-reverse</c>; initial <c>row</c>.</para>
    /// </summary>
    [Spec("css-flexbox", "https://drafts.csswg.org/css-flexbox-2/#propdef-flex-direction")]
    [SpecFact]
    public void Parses_flex_direction()
    {
        var decls = Expand("flex-direction: row-reverse;");
        decls.Single().Id.Should().Be(PropertyId.FlexDirection);
        decls.Single().Value.Should().Be(new CssKeyword("row-reverse"));
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/css-flexbox-2/#propdef-flex-flow"/>
    /// <para>Property <c>flex-flow</c> — value <c>&lt;'flex-direction'&gt; || &lt;'flex-wrap'&gt;</c>; initial <c>see individual properties</c>.</para>
    /// </summary>
    [Spec("css-flexbox", "https://drafts.csswg.org/css-flexbox-2/#propdef-flex-flow")]
    [SpecFact]
    public void Parses_flex_flow()
    {
        var decls = Expand("flex-flow: column wrap;");
        decls.Single(d => d.Id == PropertyId.FlexDirection).Value.Should().Be(new CssKeyword("column"));
        decls.Single(d => d.Id == PropertyId.FlexWrap).Value.Should().Be(new CssKeyword("wrap"));
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/css-flexbox-2/#propdef-flex"/>
    /// <para>Property <c>flex</c> — value <c>none | [ &lt;'flex-grow'&gt; &lt;'flex-shrink'&gt;? || &lt;'flex-basis'&gt; ]</c>; initial <c>0 1 auto</c>.</para>
    /// </summary>
    [Spec("css-flexbox", "https://drafts.csswg.org/css-flexbox-2/#propdef-flex")]
    [SpecFact]
    public void Parses_flex()
    {
        var decls = Expand("flex: 1 1 auto;");
        decls.Should().HaveCount(3);
        decls.Single(d => d.Id == PropertyId.FlexGrow).Value.Should().Be(new CssNumber(1));
        decls.Single(d => d.Id == PropertyId.FlexShrink).Value.Should().Be(new CssNumber(1));
        decls.Single(d => d.Id == PropertyId.FlexBasis).Value.Should().Be(new CssKeyword("auto"));
    }

}
