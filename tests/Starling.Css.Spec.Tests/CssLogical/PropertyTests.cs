using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssLogical;

/// <summary>
/// Property conformance for <see href="https://drafts.csswg.org/css-logical-1/">CSS Logical Properties and Values Module Level 1</see>.
/// </summary>
[TestClass]
[Spec("css-logical", "https://drafts.csswg.org/css-logical-1/")]
public sealed class PropertyTests
{

    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/css-logical-1/#propdef-block-size"/>
    /// <para>Property <c>block-size</c> — value <c>&lt;'width'&gt;</c>; initial <c>auto</c>.</para>
    /// </summary>
    [Spec("css-logical", "https://drafts.csswg.org/css-logical-1/#propdef-block-size")]
    [SpecFact]
    public void Parses_block_size()
    {
        var decls = Expand("block-size: 100px;");
        decls.Single(d => d.Id == PropertyId.BlockSize).Value.Should().Be(new CssLength(100, CssLengthUnit.Px));
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/css-logical-1/#propdef-inline-size"/>
    /// <para>Property <c>inline-size</c> — value <c>&lt;'width'&gt;</c>; initial <c>auto</c>.</para>
    /// </summary>
    [Spec("css-logical", "https://drafts.csswg.org/css-logical-1/#propdef-inline-size")]
    [SpecFact]
    public void Parses_inline_size()
    {
        var decls = Expand("inline-size: 200px;");
        decls.Single(d => d.Id == PropertyId.InlineSize).Value.Should().Be(new CssLength(200, CssLengthUnit.Px));
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/css-logical-1/#propdef-margin-inline-start"/>
    /// <para>Property <c>margin-inline-start</c> — value <c>&lt;'margin-top'&gt;</c>; initial <c>0</c>.</para>
    /// </summary>
    [Spec("css-logical", "https://drafts.csswg.org/css-logical-1/#propdef-margin-inline-start")]
    [SpecFact]
    public void Parses_margin_inline_start()
    {
        var decls = Expand("margin-inline-start: 12px;");
        decls.Single().Id.Should().Be(PropertyId.MarginInlineStart);
        decls.Single().Value.Should().Be(new CssLength(12, CssLengthUnit.Px));
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/css-logical-1/#propdef-margin-block"/>
    /// <para>Property <c>margin-block</c> — value <c>&lt;'margin-top'&gt;{1,2}</c>; initial <c>see individual properties</c>.</para>
    /// </summary>
    [Spec("css-logical", "https://drafts.csswg.org/css-logical-1/#propdef-margin-block")]
    [SpecFact]
    public void Parses_margin_block()
    {
        var decls = Expand("margin-block: 10px;");
        decls.Single(d => d.Id == PropertyId.MarginBlockStart).Value.Should().Be(new CssLength(10, CssLengthUnit.Px));
        decls.Single(d => d.Id == PropertyId.MarginBlockEnd).Value.Should().Be(new CssLength(10, CssLengthUnit.Px));
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/css-logical-1/#propdef-margin-inline"/>
    /// <para>Property <c>margin-inline</c> — value <c>&lt;'margin-top'&gt;{1,2}</c>; initial <c>see individual properties</c>.</para>
    /// </summary>
    [Spec("css-logical", "https://drafts.csswg.org/css-logical-1/#propdef-margin-inline")]
    [SpecFact]
    public void Parses_margin_inline()
    {
        var decls = Expand("margin-inline: 4px 8px;");
        decls.Single(d => d.Id == PropertyId.MarginInlineStart).Value.Should().Be(new CssLength(4, CssLengthUnit.Px));
        decls.Single(d => d.Id == PropertyId.MarginInlineEnd).Value.Should().Be(new CssLength(8, CssLengthUnit.Px));
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/css-logical-1/#propdef-inset-inline"/>
    /// <para>Property <c>inset-inline</c> — value <c>&lt;'top'&gt;{1,2}</c>; initial <c>see individual properties</c>.</para>
    /// </summary>
    [Spec("css-logical", "https://drafts.csswg.org/css-logical-1/#propdef-inset-inline")]
    [SpecFact]
    public void Parses_inset_inline()
    {
        var decls = Expand("inset-inline: 5px 10px;");
        decls.Single(d => d.Id == PropertyId.InsetInlineStart).Value.Should().Be(new CssLength(5, CssLengthUnit.Px));
        decls.Single(d => d.Id == PropertyId.InsetInlineEnd).Value.Should().Be(new CssLength(10, CssLengthUnit.Px));
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/css-logical-1/#propdef-inset"/>
    /// <para>Property <c>inset</c> — value <c>&lt;'top'&gt;{1,4}</c>; initial <c>see individual properties</c>.</para>
    /// </summary>
    [Spec("css-logical", "https://drafts.csswg.org/css-logical-1/#propdef-inset")]
    [SpecFact]
    public void Parses_inset()
    {
        var decls = Expand("inset: 1px 2px 3px 4px;");
        decls.Single(d => d.Id == PropertyId.Top).Value.Should().Be(new CssLength(1, CssLengthUnit.Px));
        decls.Single(d => d.Id == PropertyId.Right).Value.Should().Be(new CssLength(2, CssLengthUnit.Px));
        decls.Single(d => d.Id == PropertyId.Bottom).Value.Should().Be(new CssLength(3, CssLengthUnit.Px));
        decls.Single(d => d.Id == PropertyId.Left).Value.Should().Be(new CssLength(4, CssLengthUnit.Px));
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/css-logical-1/#propdef-padding-inline"/>
    /// <para>Property <c>padding-inline</c> — value <c>&lt;'padding-top'&gt;{1,2}</c>; initial <c>see individual properties</c>.</para>
    /// </summary>
    [Spec("css-logical", "https://drafts.csswg.org/css-logical-1/#propdef-padding-inline")]
    [SpecFact]
    public void Parses_padding_inline()
    {
        var decls = Expand("padding-inline: 1rem;");
        decls.Single(d => d.Id == PropertyId.PaddingInlineStart).Value.Should().Be(new CssLength(1, CssLengthUnit.Rem));
        decls.Single(d => d.Id == PropertyId.PaddingInlineEnd).Value.Should().Be(new CssLength(1, CssLengthUnit.Rem));
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/css-logical-1/#propdef-border-inline-start"/>
    /// <para>Property <c>border-inline-start</c> — value <c>&lt;'border-top-width'&gt; || &lt;'border-top-style'&gt; || &lt;color&gt;</c>; initial <c>see individual properties</c>.</para>
    /// </summary>
    [Spec("css-logical", "https://drafts.csswg.org/css-logical-1/#propdef-border-inline-start")]
    [SpecFact]
    public void Parses_border_inline_start()
    {
        var decls = Expand("border-inline-start: 2px solid red;");
        decls.Single(d => d.Id == PropertyId.BorderInlineStartWidth).Value.Should().Be(new CssLength(2, CssLengthUnit.Px));
        decls.Single(d => d.Id == PropertyId.BorderInlineStartStyle).Value.Should().Be(new CssKeyword("solid"));
        decls.Single(d => d.Id == PropertyId.BorderInlineStartColor).Value.Should().BeOfType<Starling.Css.Values.CssColor>();
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/css-logical-1/#propdef-border-inline"/>
    /// <para>Property <c>border-inline</c> — value <c>&lt;'border-block-start'&gt;</c>; initial <c>see individual properties</c>.</para>
    /// </summary>
    [Spec("css-logical", "https://drafts.csswg.org/css-logical-1/#propdef-border-inline")]
    [SpecFact]
    public void Parses_border_inline()
    {
        var decls = Expand("border-inline: 1px dashed blue;");
        decls.Any(d => d.Id == PropertyId.BorderInlineStartWidth).Should().BeTrue();
        decls.Any(d => d.Id == PropertyId.BorderInlineEndWidth).Should().BeTrue();
        decls.Single(d => d.Id == PropertyId.BorderInlineStartStyle).Value.Should().Be(new CssKeyword("dashed"));
        decls.Single(d => d.Id == PropertyId.BorderInlineEndStyle).Value.Should().Be(new CssKeyword("dashed"));
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/css-logical-1/#propdef-border-start-start-radius"/>
    /// <para>Property <c>border-start-start-radius</c> — value <c>&lt;'border-top-left-radius'&gt;</c>; initial <c>Same as border-top-left-radius</c>.</para>
    /// </summary>
    [Spec("css-logical", "https://drafts.csswg.org/css-logical-1/#propdef-border-start-start-radius")]
    [SpecFact]
    public void Parses_border_start_start_radius()
    {
        var decls = Expand("border-start-start-radius: 4px;");
        decls.Single().Id.Should().Be(PropertyId.BorderStartStartRadius);
        decls.Single().Value.Should().Be(new CssLength(4, CssLengthUnit.Px));
    }

}
