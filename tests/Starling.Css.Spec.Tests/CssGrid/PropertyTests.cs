using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssGrid;

/// <summary>
/// Property conformance for <see href="https://drafts.csswg.org/css-grid-2/">CSS Grid Layout Module Level 2</see>.
/// </summary>
[TestClass]
[Spec("css-grid", "https://drafts.csswg.org/css-grid-2/")]
public sealed class PropertyTests
{

    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/css-grid-2/#propdef-grid-template-columns"/>
    /// <para>Property <c>grid-template-columns</c> — value <c>none | &lt;track-list&gt; | &lt;auto-track-list&gt; | subgrid &lt;line-name-list&gt;?</c>; initial <c>none</c>.</para>
    /// </summary>
    [Spec("css-grid", "https://drafts.csswg.org/css-grid-2/#propdef-grid-template-columns")]
    [SpecFact]
    public void Parses_grid_template_columns()
    {
        var decls = Expand("grid-template-columns: 100px 1fr 2fr;");
        var value = decls.Single(d => d.Id == PropertyId.GridTemplateColumns).Value;
        value.Should().BeOfType<CssValueList>();
        ((CssValueList)value).Values.Should().HaveCount(3);
        ((CssValueList)value).Values[0].Should().Be(new CssLength(100, CssLengthUnit.Px));
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/css-grid-2/#propdef-grid-auto-flow"/>
    /// <para>Property <c>grid-auto-flow</c> — value <c>[ row | column ] || dense</c>; initial <c>row</c>.</para>
    /// </summary>
    [Spec("css-grid", "https://drafts.csswg.org/css-grid-2/#propdef-grid-auto-flow")]
    [SpecFact]
    public void Parses_grid_auto_flow()
    {
        var decls = Expand("grid-auto-flow: row dense;");
        decls.Should().ContainSingle();
        decls[0].Id.Should().Be(PropertyId.GridAutoFlow);
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/css-grid-2/#propdef-grid-row"/>
    /// <para>Property <c>grid-row</c> — value <c>&lt;grid-line&gt; [ / &lt;grid-line&gt; ]?</c>; initial <c>auto</c>.</para>
    /// </summary>
    [Spec("css-grid", "https://drafts.csswg.org/css-grid-2/#propdef-grid-row")]
    [SpecFact]
    public void Parses_grid_row()
    {
        var decls = Expand("grid-row: 2 / span 3;");
        decls.Single(d => d.Id == PropertyId.GridRowStart).Value.Should().Be(new CssNumber(2));
        decls.Single(d => d.Id == PropertyId.GridRowEnd).Value.Should().BeOfType<CssValueList>();
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/css-grid-2/#propdef-grid-column"/>
    /// <para>Property <c>grid-column</c> — value <c>&lt;grid-line&gt; [ / &lt;grid-line&gt; ]?</c>; initial <c>auto</c>.</para>
    /// </summary>
    [Spec("css-grid", "https://drafts.csswg.org/css-grid-2/#propdef-grid-column")]
    [SpecFact]
    public void Parses_grid_column()
    {
        var decls = Expand("grid-column: 1 / 3;");
        decls.Single(d => d.Id == PropertyId.GridColumnStart).Value.Should().Be(new CssNumber(1));
        decls.Single(d => d.Id == PropertyId.GridColumnEnd).Value.Should().Be(new CssNumber(3));
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/css-grid-2/#propdef-grid-area"/>
    /// <para>Property <c>grid-area</c> — value <c>&lt;grid-line&gt; [ / &lt;grid-line&gt; ]{0,3}</c>; initial <c>auto</c>.</para>
    /// </summary>
    [Spec("css-grid", "https://drafts.csswg.org/css-grid-2/#propdef-grid-area")]
    [SpecFact]
    public void Parses_grid_area()
    {
        var decls = Expand("grid-area: 1 / 2 / 3 / 4;");
        decls.Single(d => d.Id == PropertyId.GridRowStart).Value.Should().Be(new CssNumber(1));
        decls.Single(d => d.Id == PropertyId.GridColumnStart).Value.Should().Be(new CssNumber(2));
        decls.Single(d => d.Id == PropertyId.GridRowEnd).Value.Should().Be(new CssNumber(3));
        decls.Single(d => d.Id == PropertyId.GridColumnEnd).Value.Should().Be(new CssNumber(4));
    }
}
