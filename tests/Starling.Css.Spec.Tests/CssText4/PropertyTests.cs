using AwesomeAssertions;
using Starling.Css.Cascade;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Dom;

namespace Starling.Css.Spec.Tests.CssText4;

/// <summary>
/// Property parse + cascade conformance for the Level-4 additions of
/// <see href="https://www.w3.org/TR/css-text-4/">CSS Text Module Level 4</see>.
/// This file targets only the surface that Level 4 adds on top of Level 3
/// (which is covered by <c>Starling.Css.Spec.Tests.CssText3.PropertyTests</c>
/// and <c>Starling.Layout.Tests.CssText3InlineTests</c>): the new keywords
/// (<c>word-break: break-word</c>, <c>text-wrap: balance|pretty|stable</c>,
/// <c>line-break: anywhere</c>, the <c>text-align-last</c> property, the
/// <c>text-indent</c> <c>hanging</c>/<c>each-line</c> modifiers) plus the
/// inheritance + initial-value contract for the Level-4 longhands.
/// </summary>
[TestClass]
[Spec("css-text-4", "https://www.w3.org/TR/css-text-4/")]
public sealed class PropertyTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    private static CssValue Single(string css)
        => Expand(css).Single().Value;

    // ---- word-break (CSS Text 4 §6.1) -----------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-4/#word-break-property"/>
    /// <para>Level 4 keeps <c>normal | break-all | keep-all</c> and adds the
    /// legacy <c>break-word</c> value.</para>
    /// </summary>
    [Spec("css-text-4", "https://www.w3.org/TR/css-text-4/#word-break-property", "6.1")]
    [SpecFact]
    public void Word_break_keep_all()
        => Single("word-break: keep-all;").Should().Be(new CssKeyword("keep-all"));

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-4/#word-break-property"/>
    /// <para><c>break-word</c> is the Level-4 legacy value for <c>word-break</c>.</para>
    /// </summary>
    [Spec("css-text-4", "https://www.w3.org/TR/css-text-4/#word-break-property", "6.1")]
    [SpecFact]
    public void Word_break_break_word()
        => Single("word-break: break-word;").Should().Be(new CssKeyword("break-word"));

    // ---- overflow-wrap (CSS Text 4 §6.2) --------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-4/#overflow-wrap-property"/>
    /// <para>The <c>break-word</c> value of <c>overflow-wrap</c>.</para>
    /// </summary>
    [Spec("css-text-4", "https://www.w3.org/TR/css-text-4/#overflow-wrap-property", "6.2")]
    [SpecFact]
    public void Overflow_wrap_break_word()
        => Single("overflow-wrap: break-word;").Should().Be(new CssKeyword("break-word"));

    // ---- line-break (CSS Text 4 §6.3) -----------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-4/#line-break-property"/>
    /// <para>Property <c>line-break</c> — value <c>auto | loose | normal | strict | anywhere</c>;
    /// initial <c>auto</c>.</para>
    /// </summary>
    [Spec("css-text-4", "https://www.w3.org/TR/css-text-4/#line-break-property", "6.3")]
    [SpecFact]
    public void Line_break_loose()
        => Single("line-break: loose;").Should().Be(new CssKeyword("loose"));

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-4/#line-break-property"/></summary>
    [Spec("css-text-4", "https://www.w3.org/TR/css-text-4/#line-break-property", "6.3")]
    [SpecFact]
    public void Line_break_strict()
        => Single("line-break: strict;").Should().Be(new CssKeyword("strict"));

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-4/#line-break-property"/>
    /// <para><c>anywhere</c> is the Level-4 addition to <c>line-break</c>.</para>
    /// </summary>
    [Spec("css-text-4", "https://www.w3.org/TR/css-text-4/#line-break-property", "6.3")]
    [SpecFact]
    public void Line_break_anywhere()
        => Single("line-break: anywhere;").Should().Be(new CssKeyword("anywhere"));

    // ---- hyphens (CSS Text 4 §6.5) --------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-4/#hyphens-property"/>
    /// <para>Property <c>hyphens</c> — value <c>none | manual | auto</c>;
    /// initial <c>manual</c>.</para>
    /// </summary>
    [Spec("css-text-4", "https://www.w3.org/TR/css-text-4/#hyphens-property", "6.5")]
    [SpecFact]
    public void Hyphens_auto()
        => Single("hyphens: auto;").Should().Be(new CssKeyword("auto"));

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-4/#hyphens-property"/></summary>
    [Spec("css-text-4", "https://www.w3.org/TR/css-text-4/#hyphens-property", "6.5")]
    [SpecFact]
    public void Hyphens_none()
        => Single("hyphens: none;").Should().Be(new CssKeyword("none"));

    // ---- text-wrap (CSS Text 4 §7.1) ------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-4/#text-wrap"/>
    /// <para>Level-4 <c>text-wrap: balance</c> — a typesetting hint to even out
    /// line lengths.</para>
    /// </summary>
    [Spec("css-text-4", "https://www.w3.org/TR/css-text-4/#text-wrap", "7.1")]
    [SpecFact]
    public void Text_wrap_balance()
        => Single("text-wrap: balance;").Should().Be(new CssKeyword("balance"));

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-4/#text-wrap"/>
    /// <para>Level-4 <c>text-wrap: pretty</c>.</para>
    /// </summary>
    [Spec("css-text-4", "https://www.w3.org/TR/css-text-4/#text-wrap", "7.1")]
    [SpecFact]
    public void Text_wrap_pretty()
        => Single("text-wrap: pretty;").Should().Be(new CssKeyword("pretty"));

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-4/#text-wrap"/>
    /// <para>Level-4 <c>text-wrap: stable</c>.</para>
    /// </summary>
    [Spec("css-text-4", "https://www.w3.org/TR/css-text-4/#text-wrap", "7.1")]
    [SpecFact]
    public void Text_wrap_stable()
        => Single("text-wrap: stable;").Should().Be(new CssKeyword("stable"));

    // ---- text-align-last (CSS Text 4 §9.3) ------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-4/#text-align-last-property"/>
    /// <para>Property <c>text-align-last</c> — value
    /// <c>auto | start | end | left | right | center | justify</c>;
    /// initial <c>auto</c>.</para>
    /// </summary>
    [Spec("css-text-4", "https://www.w3.org/TR/css-text-4/#text-align-last-property", "9.3")]
    [SpecFact]
    public void Text_align_last_justify()
        => Single("text-align-last: justify;").Should().Be(new CssKeyword("justify"));

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-4/#text-align-last-property"/></summary>
    [Spec("css-text-4", "https://www.w3.org/TR/css-text-4/#text-align-last-property", "9.3")]
    [SpecFact]
    public void Text_align_last_center()
        => Single("text-align-last: center;").Should().Be(new CssKeyword("center"));

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-4/#text-align-last-property"/></summary>
    [Spec("css-text-4", "https://www.w3.org/TR/css-text-4/#text-align-last-property", "9.3")]
    [SpecFact]
    public void Text_align_last_start_end()
    {
        Single("text-align-last: start;").Should().Be(new CssKeyword("start"));
        Single("text-align-last: end;").Should().Be(new CssKeyword("end"));
    }

    // ---- text-indent (CSS Text 4 §8.1) ----------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-4/#text-indent-property"/>
    /// <para>The base <c>&lt;length-percentage&gt;</c> value still parses in Level 4.</para>
    /// </summary>
    [Spec("css-text-4", "https://www.w3.org/TR/css-text-4/#text-indent-property", "8.1")]
    [SpecFact]
    public void Text_indent_length()
        => Single("text-indent: 3em;").Should().Be(new CssLength(3, CssLengthUnit.Em));

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-4/#text-indent-property"/>
    /// <para>Level 4 adds the <c>hanging</c> and <c>each-line</c> modifier
    /// keywords to <c>text-indent</c> (e.g. <c>text-indent: 2em hanging</c>).
    /// They parse as a value list alongside the length.</para>
    /// </summary>
    [Spec("css-text-4", "https://www.w3.org/TR/css-text-4/#text-indent-property", "8.1")]
    [SpecFact]
    public void Text_indent_hanging_each_line_keywords()
    {
        // The length + modifier keyword parse as a CssValueList. CssValueList
        // does not implement structural equality, so inspect the components.
        var hanging = Expand("text-indent: 2em hanging;").Single().Value
            .Should().BeOfType<CssValueList>().Subject;
        hanging.Values.Should().Equal(new CssLength(2, CssLengthUnit.Em), new CssKeyword("hanging"));

        var eachLine = Expand("text-indent: 2em each-line;").Single().Value
            .Should().BeOfType<CssValueList>().Subject;
        eachLine.Values.Should().Equal(new CssLength(2, CssLengthUnit.Em), new CssKeyword("each-line"));
    }

    // ---- tab-size (CSS Text 4 §11.2) ------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-4/#tab-size-property"/>
    /// <para>Property <c>tab-size</c> — value <c>&lt;number&gt; | &lt;length&gt;</c>.</para>
    /// </summary>
    [Spec("css-text-4", "https://www.w3.org/TR/css-text-4/#tab-size-property", "11.2")]
    [SpecFact]
    public void Tab_size_number_and_length()
    {
        Single("tab-size: 2;").Should().Be(new CssNumber(2));
        Single("tab-size: 4ch;").Should().Be(new CssLength(4, CssLengthUnit.Ch));
    }

    // ---- letter-spacing / word-spacing (CSS Text 4 §10) -----------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-4/#letter-spacing-property"/>
    /// <para><c>letter-spacing</c> accepts <c>normal</c> and a <c>&lt;length&gt;</c>.</para>
    /// </summary>
    [Spec("css-text-4", "https://www.w3.org/TR/css-text-4/#letter-spacing-property", "10.1")]
    [SpecFact]
    public void Letter_spacing_normal_and_length()
    {
        Single("letter-spacing: normal;").Should().Be(new CssKeyword("normal"));
        Single("letter-spacing: 0.1em;").Should().Be(new CssLength(0.1, CssLengthUnit.Em));
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-4/#word-spacing-property"/>
    /// <para><c>word-spacing</c> accepts <c>normal</c> and a <c>&lt;length&gt;</c>.</para>
    /// </summary>
    [Spec("css-text-4", "https://www.w3.org/TR/css-text-4/#word-spacing-property", "10.2")]
    [SpecFact]
    public void Word_spacing_normal_and_length()
    {
        Single("word-spacing: normal;").Should().Be(new CssKeyword("normal"));
        Single("word-spacing: 4px;").Should().Be(new CssLength(4, CssLengthUnit.Px));
    }

    // ---- initial values (CSS Text 4) ------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-4/"/>
    /// <para>Initial values for the Level-4 longhands.</para>
    /// </summary>
    [Spec("css-text-4", "https://www.w3.org/TR/css-text-4/")]
    [SpecFact]
    public void Initial_values_match_spec()
    {
        PropertyRegistry.InitialValue(PropertyId.LineBreak).Should().Be(new CssKeyword("auto"));
        PropertyRegistry.InitialValue(PropertyId.Hyphens).Should().Be(new CssKeyword("manual"));
        PropertyRegistry.InitialValue(PropertyId.TextWrap).Should().Be(new CssKeyword("wrap"));
        PropertyRegistry.InitialValue(PropertyId.TextAlignLast).Should().Be(new CssKeyword("auto"));
        PropertyRegistry.InitialValue(PropertyId.WhiteSpaceCollapse).Should().Be(new CssKeyword("collapse"));
    }

    // ---- inheritance (CSS Text 4 — these all inherit) -------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-4/"/>
    /// <para>The Level-4 text longhands are all inherited.</para>
    /// </summary>
    [Spec("css-text-4", "https://www.w3.org/TR/css-text-4/")]
    [SpecFact]
    public void Properties_are_inherited()
    {
        PropertyRegistry.Inherits(PropertyId.LineBreak).Should().BeTrue();
        PropertyRegistry.Inherits(PropertyId.Hyphens).Should().BeTrue();
        PropertyRegistry.Inherits(PropertyId.TextWrap).Should().BeTrue();
        PropertyRegistry.Inherits(PropertyId.TextAlignLast).Should().BeTrue();
        PropertyRegistry.Inherits(PropertyId.WhiteSpaceCollapse).Should().BeTrue();
    }

    // ---- cascade: inherited values flow to descendants ------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-4/#hyphens-property"/>
    /// <para>An inherited Level-4 longhand set on an ancestor reaches a child
    /// that does not set it (cascade + inheritance).</para>
    /// </summary>
    [Spec("css-text-4", "https://www.w3.org/TR/css-text-4/#hyphens-property", "6.5")]
    [SpecFact]
    public void Hyphens_inherits_to_child_through_cascade()
    {
        var doc = new Document();
        var parent = doc.CreateElement("div");
        var child = doc.CreateElement("span");
        doc.AppendChild(parent);
        parent.AppendChild(child);
        parent.SetAttribute("style", "hyphens: auto;");

        var engine = new StyleEngine();
        engine.Compute(child).Get(PropertyId.Hyphens).Should().Be(new CssKeyword("auto"));
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-4/#text-align-last-property"/>
    /// <para><c>text-align-last</c> is inherited, so a value on the ancestor
    /// applies to a non-setting descendant.</para>
    /// </summary>
    [Spec("css-text-4", "https://www.w3.org/TR/css-text-4/#text-align-last-property", "9.3")]
    [SpecFact]
    public void Text_align_last_inherits_to_child_through_cascade()
    {
        var doc = new Document();
        var parent = doc.CreateElement("div");
        var child = doc.CreateElement("span");
        doc.AppendChild(parent);
        parent.AppendChild(child);
        parent.SetAttribute("style", "text-align-last: justify;");

        var engine = new StyleEngine();
        engine.Compute(child).Get(PropertyId.TextAlignLast).Should().Be(new CssKeyword("justify"));
    }
}
