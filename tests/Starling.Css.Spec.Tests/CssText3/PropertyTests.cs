using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssText3;

/// <summary>
/// Typed-parse conformance for the inline properties of
/// <see href="https://www.w3.org/TR/css-text-3/">CSS Text Module Level 3</see>
/// (plus the CSS Text 4 <c>white-space</c> longhands it expands to). The
/// behavioral application of these values is exercised in
/// <c>Starling.Layout.Tests.CssText3InlineTests</c>.
/// </summary>
[TestClass]
[Spec("css-text-3", "https://www.w3.org/TR/css-text-3/")]
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

    // ---- white-space (CSS Text 3 §3 / shorthand per CSS Text 4 §3) --------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-3/#white-space-property"/>
    /// <para>Property <c>white-space</c> — value <c>normal | pre | nowrap | pre-wrap | pre-line</c>; initial <c>normal</c>.</para>
    /// </summary>
    [Spec("css-text-3", "https://www.w3.org/TR/css-text-3/#white-space-property", "3")]
    [SpecFact]
    public void Parses_white_space_keyword()
    {
        var decls = Expand("white-space: pre;");
        decls.Should().Contain(d => d.Id == PropertyId.WhiteSpace && d.Value.Equals(new CssKeyword("pre")));
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-3/#white-space-property"/>
    /// <para>The legacy <c>white-space</c> keyword expands to the
    /// <c>white-space-collapse</c> + <c>text-wrap</c> longhands (CSS Text 4 §3).</para>
    /// </summary>
    [Spec("css-text-3", "https://www.w3.org/TR/css-text-3/#white-space-property", "3")]
    [SpecFact]
    public void White_space_pre_expands_to_preserve_and_nowrap()
    {
        var decls = Expand("white-space: pre;");
        decls.Should().Contain(d => d.Id == PropertyId.WhiteSpaceCollapse && d.Value.Equals(new CssKeyword("preserve")));
        decls.Should().Contain(d => d.Id == PropertyId.TextWrap && d.Value.Equals(new CssKeyword("nowrap")));
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-3/#white-space-property"/></summary>
    [Spec("css-text-3", "https://www.w3.org/TR/css-text-3/#white-space-property", "3")]
    [SpecFact]
    public void White_space_pre_wrap_expands_to_preserve_and_wrap()
    {
        var decls = Expand("white-space: pre-wrap;");
        decls.Should().Contain(d => d.Id == PropertyId.WhiteSpaceCollapse && d.Value.Equals(new CssKeyword("preserve")));
        decls.Should().Contain(d => d.Id == PropertyId.TextWrap && d.Value.Equals(new CssKeyword("wrap")));
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-3/#white-space-property"/></summary>
    [Spec("css-text-3", "https://www.w3.org/TR/css-text-3/#white-space-property", "3")]
    [SpecFact]
    public void White_space_pre_line_expands_to_preserve_breaks_and_wrap()
    {
        var decls = Expand("white-space: pre-line;");
        decls.Should().Contain(d => d.Id == PropertyId.WhiteSpaceCollapse && d.Value.Equals(new CssKeyword("preserve-breaks")));
        decls.Should().Contain(d => d.Id == PropertyId.TextWrap && d.Value.Equals(new CssKeyword("wrap")));
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-3/#white-space-property"/></summary>
    [Spec("css-text-3", "https://www.w3.org/TR/css-text-3/#white-space-property", "3")]
    [SpecFact]
    public void White_space_nowrap_expands_to_collapse_and_nowrap()
    {
        var decls = Expand("white-space: nowrap;");
        decls.Should().Contain(d => d.Id == PropertyId.WhiteSpaceCollapse && d.Value.Equals(new CssKeyword("collapse")));
        decls.Should().Contain(d => d.Id == PropertyId.TextWrap && d.Value.Equals(new CssKeyword("nowrap")));
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-3/#white-space-collapsing"/>
    /// <para>The modern <c>white-space-collapse</c> longhand parses on its own.</para>
    /// </summary>
    [Spec("css-text-3", "https://www.w3.org/TR/css-text-3/#white-space-collapsing", "3")]
    [SpecFact]
    public void Parses_white_space_collapse_longhand()
    {
        Single("white-space-collapse: preserve;").Should().Be(new CssKeyword("preserve"));
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-3/#text-wrap"/></summary>
    [Spec("css-text-3", "https://www.w3.org/TR/css-text-3/#text-wrap", "3")]
    [SpecFact]
    public void Parses_text_wrap_longhand()
    {
        Single("text-wrap: nowrap;").Should().Be(new CssKeyword("nowrap"));
    }

    // ---- text-transform (CSS Text 3 §2.1) --------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-3/#text-transform-property"/>
    /// <para>Property <c>text-transform</c> — value <c>none | [capitalize | uppercase | lowercase] ...</c>; initial <c>none</c>.</para>
    /// </summary>
    [Spec("css-text-3", "https://www.w3.org/TR/css-text-3/#text-transform-property", "2.1")]
    [SpecFact]
    public void Parses_text_transform()
    {
        Single("text-transform: uppercase;").Should().Be(new CssKeyword("uppercase"));
    }

    // ---- letter-spacing / word-spacing (CSS Text 3 §8) -------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-3/#letter-spacing-property"/>
    /// <para>Property <c>letter-spacing</c> — value <c>normal | &lt;length&gt;</c>; initial <c>normal</c>.</para>
    /// </summary>
    [Spec("css-text-3", "https://www.w3.org/TR/css-text-3/#letter-spacing-property", "8.2")]
    [SpecFact]
    public void Parses_letter_spacing_length()
    {
        Single("letter-spacing: 2px;").Should().Be(new CssLength(2, CssLengthUnit.Px));
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-3/#letter-spacing-property"/></summary>
    [Spec("css-text-3", "https://www.w3.org/TR/css-text-3/#letter-spacing-property", "8.2")]
    [SpecFact]
    public void Parses_letter_spacing_normal()
    {
        Single("letter-spacing: normal;").Should().Be(new CssKeyword("normal"));
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-3/#word-spacing-property"/>
    /// <para>Property <c>word-spacing</c> — value <c>normal | &lt;length&gt;</c>; initial <c>normal</c>.</para>
    /// </summary>
    [Spec("css-text-3", "https://www.w3.org/TR/css-text-3/#word-spacing-property", "8.1")]
    [SpecFact]
    public void Parses_word_spacing_length()
    {
        Single("word-spacing: 0.5em;").Should().Be(new CssLength(0.5, CssLengthUnit.Em));
    }

    // ---- text-indent (CSS Text 3 §9.1) -----------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-3/#text-indent-property"/>
    /// <para>Property <c>text-indent</c> — value <c>&lt;length-percentage&gt; ...</c>; initial <c>0</c>.</para>
    /// </summary>
    [Spec("css-text-3", "https://www.w3.org/TR/css-text-3/#text-indent-property", "9.1")]
    [SpecFact]
    public void Parses_text_indent_length()
    {
        Single("text-indent: 2em;").Should().Be(new CssLength(2, CssLengthUnit.Em));
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-3/#text-indent-property"/></summary>
    [Spec("css-text-3", "https://www.w3.org/TR/css-text-3/#text-indent-property", "9.1")]
    [SpecFact]
    public void Parses_text_indent_percentage()
    {
        Single("text-indent: 10%;").Should().Be(new CssPercentage(10));
    }

    // ---- tab-size (CSS Text 3 §6.4) --------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-3/#tab-size-property"/>
    /// <para>Property <c>tab-size</c> — value <c>&lt;number&gt; | &lt;length&gt;</c>; initial <c>8</c>.</para>
    /// </summary>
    [Spec("css-text-3", "https://www.w3.org/TR/css-text-3/#tab-size-property", "6.4")]
    [SpecFact]
    public void Parses_tab_size_number()
    {
        Single("tab-size: 4;").Should().Be(new CssNumber(4));
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-3/#tab-size-property"/></summary>
    [Spec("css-text-3", "https://www.w3.org/TR/css-text-3/#tab-size-property", "6.4")]
    [SpecFact]
    public void Parses_tab_size_length()
    {
        Single("tab-size: 20px;").Should().Be(new CssLength(20, CssLengthUnit.Px));
    }

    // ---- overflow-wrap / word-break (CSS Text 3 §6.2 / §5.2) -------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-3/#overflow-wrap-property"/>
    /// <para>Property <c>overflow-wrap</c> — value <c>normal | break-word | anywhere</c>; initial <c>normal</c>.</para>
    /// </summary>
    [Spec("css-text-3", "https://www.w3.org/TR/css-text-3/#overflow-wrap-property", "6.2")]
    [SpecFact]
    public void Parses_overflow_wrap()
    {
        Single("overflow-wrap: anywhere;").Should().Be(new CssKeyword("anywhere"));
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-3/#word-break-property"/>
    /// <para>Property <c>word-break</c> — value <c>normal | keep-all | break-all | break-word</c>; initial <c>normal</c>.</para>
    /// </summary>
    [Spec("css-text-3", "https://www.w3.org/TR/css-text-3/#word-break-property", "5.2")]
    [SpecFact]
    public void Parses_word_break()
    {
        Single("word-break: break-all;").Should().Be(new CssKeyword("break-all"));
    }

    // ---- initial values (CSS Text 3) -------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-3/"/>
    /// <para>Initial values for the inline text properties.</para>
    /// </summary>
    [Spec("css-text-3", "https://www.w3.org/TR/css-text-3/")]
    [SpecFact]
    public void Initial_values_match_spec()
    {
        PropertyRegistry.InitialValue(PropertyId.WhiteSpace).Should().Be(new CssKeyword("normal"));
        PropertyRegistry.InitialValue(PropertyId.TextTransform).Should().Be(new CssKeyword("none"));
        PropertyRegistry.InitialValue(PropertyId.LetterSpacing).Should().Be(new CssKeyword("normal"));
        PropertyRegistry.InitialValue(PropertyId.WordSpacing).Should().Be(new CssKeyword("normal"));
        PropertyRegistry.InitialValue(PropertyId.TextIndent).Should().Be(CssLength.Zero);
        PropertyRegistry.InitialValue(PropertyId.TabSize).Should().Be(new CssNumber(8));
        PropertyRegistry.InitialValue(PropertyId.OverflowWrap).Should().Be(new CssKeyword("normal"));
        PropertyRegistry.InitialValue(PropertyId.WordBreak).Should().Be(new CssKeyword("normal"));
    }

    // ---- inheritance (CSS Text 3 — all are inherited) --------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-3/"/>
    /// <para>The inline text properties are all inherited.</para>
    /// </summary>
    [Spec("css-text-3", "https://www.w3.org/TR/css-text-3/")]
    [SpecFact]
    public void Properties_are_inherited()
    {
        PropertyRegistry.Inherits(PropertyId.WhiteSpace).Should().BeTrue();
        PropertyRegistry.Inherits(PropertyId.TextTransform).Should().BeTrue();
        PropertyRegistry.Inherits(PropertyId.LetterSpacing).Should().BeTrue();
        PropertyRegistry.Inherits(PropertyId.WordSpacing).Should().BeTrue();
        PropertyRegistry.Inherits(PropertyId.TextIndent).Should().BeTrue();
        PropertyRegistry.Inherits(PropertyId.TabSize).Should().BeTrue();
        PropertyRegistry.Inherits(PropertyId.OverflowWrap).Should().BeTrue();
        PropertyRegistry.Inherits(PropertyId.WordBreak).Should().BeTrue();
    }
}
