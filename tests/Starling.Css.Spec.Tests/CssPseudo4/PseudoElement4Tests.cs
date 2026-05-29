using AwesomeAssertions;
using Starling.Css.Selectors;
using Starling.Dom;

namespace Starling.Css.Spec.Tests.CssPseudo4;

/// <summary>
/// Conformance suite for
/// <see href="https://www.w3.org/TR/css-pseudo-4/">CSS Pseudo-Elements Module Level 4</see>.
/// Covers §2–§4: syntax, named pseudo-elements, pseudo-class interaction, and specificity.
/// </summary>
[TestClass]
[Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/")]
public sealed class PseudoElement4Tests
{
    // -------------------------------------------------------------------------
    // §2  Pseudo-element syntax — double-colon notation
    // -------------------------------------------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: <c>::before</c> with double-colon notation produces a
    /// <see cref="PseudoElementSelector"/> with <see cref="PseudoElement.Before"/>.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void Double_colon_before_produces_PseudoElementSelector_with_Before_kind()
    {
        var selector = SelectorParser.ParseSelectorList("p::before").Selectors.Single();
        var pe = selector.RightmostCompound.SimpleSelectors
            .OfType<PseudoElementSelector>().Single();
        pe.Kind.Should().Be(PseudoElement.Before);
        pe.Name.Should().Be("before");
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: <c>::after</c> with double-colon notation.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void Double_colon_after_produces_After_kind()
    {
        var selector = SelectorParser.ParseSelectorList("p::after").Selectors.Single();
        selector.TargetPseudoElement.Should().Be(PseudoElement.After);
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: <c>::first-line</c> with double-colon notation.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void Double_colon_first_line_produces_FirstLine_kind()
    {
        var selector = SelectorParser.ParseSelectorList("p::first-line").Selectors.Single();
        selector.TargetPseudoElement.Should().Be(PseudoElement.FirstLine);
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: <c>::first-letter</c> with double-colon notation.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void Double_colon_first_letter_produces_FirstLetter_kind()
    {
        var selector = SelectorParser.ParseSelectorList("p::first-letter").Selectors.Single();
        selector.TargetPseudoElement.Should().Be(PseudoElement.FirstLetter);
    }

    // -------------------------------------------------------------------------
    // §2  Legacy single-colon notation (CSS2 compat)
    // -------------------------------------------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: legacy <c>:before</c> (single colon) is accepted and maps to
    /// <see cref="PseudoElement.Before"/>.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void Legacy_single_colon_before_is_accepted()
    {
        var selector = SelectorParser.ParseSelectorList("p:before").Selectors.Single();
        selector.TargetPseudoElement.Should().Be(PseudoElement.Before);
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: legacy <c>:after</c> (single colon) maps to <see cref="PseudoElement.After"/>.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void Legacy_single_colon_after_is_accepted()
    {
        var selector = SelectorParser.ParseSelectorList("p:after").Selectors.Single();
        selector.TargetPseudoElement.Should().Be(PseudoElement.After);
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: legacy <c>:first-line</c> maps to <see cref="PseudoElement.FirstLine"/>.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void Legacy_single_colon_first_line_is_accepted()
    {
        var selector = SelectorParser.ParseSelectorList("p:first-line").Selectors.Single();
        selector.TargetPseudoElement.Should().Be(PseudoElement.FirstLine);
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: legacy <c>:first-letter</c> maps to <see cref="PseudoElement.FirstLetter"/>.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void Legacy_single_colon_first_letter_is_accepted()
    {
        var selector = SelectorParser.ParseSelectorList("p:first-letter").Selectors.Single();
        selector.TargetPseudoElement.Should().Be(PseudoElement.FirstLetter);
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: <c>:selection</c> (single colon) is NOT a legacy pseudo-element;
    /// it must parse as a pseudo-class, not a pseudo-element.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void Single_colon_selection_is_pseudo_class_not_pseudo_element()
    {
        // :selection is not in the CSS2 legacy list; it is NOT treated as a pseudo-element.
        var selector = SelectorParser.ParseSelectorList("p:selection").Selectors.Single();
        // TargetPseudoElement should be null (selector has no pseudo-element target).
        selector.TargetPseudoElement.Should().BeNull();
        // The compound should contain a PseudoClassSelector named "selection".
        selector.RightmostCompound.SimpleSelectors
            .OfType<PseudoClassSelector>()
            .Should().ContainSingle(pc => pc.Name == "selection");
    }

    // -------------------------------------------------------------------------
    // §3  Named pseudo-elements — §3.1 ::before, §3.2 ::after
    // -------------------------------------------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#generated-content"/>
    /// <para>§3.1: <c>::before</c> on an element with a type selector produces
    /// a compound with two simple selectors.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#generated-content", section: "3.1")]
    [SpecFact]
    public void Before_compound_has_type_selector_and_pseudo_element()
    {
        var selector = SelectorParser.ParseSelectorList("p::before").Selectors.Single();
        var simples = selector.RightmostCompound.SimpleSelectors;
        simples.Should().HaveCount(2);
        simples[0].Should().BeOfType<TypeSelector>().Which.LocalName.Should().Be("p");
        simples[1].Should().BeOfType<PseudoElementSelector>().Which.Kind.Should().Be(PseudoElement.Before);
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#generated-content"/>
    /// <para>§3.2: <c>::after</c> appears as the last simple selector
    /// and is correctly identified as the target pseudo-element.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#generated-content", section: "3.2")]
    [SpecFact]
    public void After_is_target_pseudo_element()
    {
        var selector = SelectorParser.ParseSelectorList("div::after").Selectors.Single();
        selector.TargetPseudoElement.Should().Be(PseudoElement.After);
    }

    // -------------------------------------------------------------------------
    // §3  Named pseudo-elements — §3.3 ::first-line, §3.4 ::first-letter
    // -------------------------------------------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#first-line-pseudo"/>
    /// <para>§3.3: <c>::first-line</c> parses correctly.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#first-line-pseudo", section: "3.3")]
    [SpecFact]
    public void First_line_parsed_as_pseudo_element()
    {
        var selector = SelectorParser.ParseSelectorList("div::first-line").Selectors.Single();
        selector.TargetPseudoElement.Should().Be(PseudoElement.FirstLine);
        selector.RightmostCompound.SimpleSelectors
            .OfType<PseudoElementSelector>()
            .Single().Kind.Should().Be(PseudoElement.FirstLine);
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#first-letter-pseudo"/>
    /// <para>§3.4: <c>::first-letter</c> parses correctly.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#first-letter-pseudo", section: "3.4")]
    [SpecFact]
    public void First_letter_parsed_as_pseudo_element()
    {
        var selector = SelectorParser.ParseSelectorList("p::first-letter").Selectors.Single();
        selector.TargetPseudoElement.Should().Be(PseudoElement.FirstLetter);
    }

    // -------------------------------------------------------------------------
    // §3  Named pseudo-elements — §3.5 ::selection, §3.6 ::backdrop,
    //     §3.7 ::marker, §3.8 ::placeholder, §3.9 ::file-selector-button
    // -------------------------------------------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#highlight-selectors"/>
    /// <para>§3.5: <c>::selection</c> parses as <see cref="PseudoElement.Selection"/>.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#highlight-selectors", section: "3.5")]
    [SpecFact]
    public void Selection_pseudo_element_parses()
    {
        var selector = SelectorParser.ParseSelectorList("p::selection").Selectors.Single();
        selector.TargetPseudoElement.Should().Be(PseudoElement.Selection);
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#backdrop-pseudo"/>
    /// <para>§3.6 (or equivalent): <c>::backdrop</c> parses as <see cref="PseudoElement.Backdrop"/>.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#backdrop-pseudo", section: "3")]
    [SpecFact]
    public void Backdrop_pseudo_element_parses()
    {
        var selector = SelectorParser.ParseSelectorList("dialog::backdrop").Selectors.Single();
        selector.TargetPseudoElement.Should().Be(PseudoElement.Backdrop);
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#marker-pseudo"/>
    /// <para>§3.7: <c>::marker</c> parses as <see cref="PseudoElement.Marker"/>.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#marker-pseudo", section: "3.7")]
    [SpecFact]
    public void Marker_pseudo_element_parses()
    {
        var selector = SelectorParser.ParseSelectorList("li::marker").Selectors.Single();
        selector.TargetPseudoElement.Should().Be(PseudoElement.Marker);
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#placeholder-pseudo"/>
    /// <para>§3.8: <c>::placeholder</c> parses as <see cref="PseudoElement.Placeholder"/>.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#placeholder-pseudo", section: "3.8")]
    [SpecFact]
    public void Placeholder_pseudo_element_parses()
    {
        var selector = SelectorParser.ParseSelectorList("input::placeholder").Selectors.Single();
        selector.TargetPseudoElement.Should().Be(PseudoElement.Placeholder);
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#file-selector-button-pseudo"/>
    /// <para>§3.9: <c>::file-selector-button</c> parses as <see cref="PseudoElement.FileSelectorButton"/>.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#file-selector-button-pseudo", section: "3.9")]
    [SpecFact]
    public void File_selector_button_pseudo_element_parses()
    {
        var selector = SelectorParser.ParseSelectorList("input[type=\"file\"]::file-selector-button")
            .Selectors.Single();
        selector.TargetPseudoElement.Should().Be(PseudoElement.FileSelectorButton);
    }

    // -------------------------------------------------------------------------
    // §2 / §3  All named pseudo-elements round-trip (data-driven)
    // -------------------------------------------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>All defined pseudo-elements parse to the expected <see cref="PseudoElement"/> kind.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [TestMethod]
    [DataRow("::marker", PseudoElement.Marker)]
    [DataRow("::placeholder", PseudoElement.Placeholder)]
    [DataRow("::first-line", PseudoElement.FirstLine)]
    [DataRow("::first-letter", PseudoElement.FirstLetter)]
    [DataRow("::before", PseudoElement.Before)]
    [DataRow("::after", PseudoElement.After)]
    [DataRow("::selection", PseudoElement.Selection)]
    [DataRow("::backdrop", PseudoElement.Backdrop)]
    [DataRow("::file-selector-button", PseudoElement.FileSelectorButton)]
    public void Named_pseudo_elements_have_expected_kind(string source, PseudoElement expected)
    {
        var selector = SelectorParser.ParseSelectorList($"div{source}").Selectors.Single();
        selector.TargetPseudoElement.Should().Be(expected);
    }

    // -------------------------------------------------------------------------
    // §2  Name is stored normalised to lower-case
    // -------------------------------------------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: pseudo-element names are ASCII case-insensitive; the parser
    /// normalises them to lower-case.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void Pseudo_element_name_is_lowercased()
    {
        var selector = SelectorParser.ParseSelectorList("div::BEFORE").Selectors.Single();
        selector.TargetPseudoElement.Should().Be(PseudoElement.Before);
        var pe = selector.RightmostCompound.SimpleSelectors
            .OfType<PseudoElementSelector>().Single();
        pe.Name.Should().Be("before");
    }

    // -------------------------------------------------------------------------
    // §2  Unknown pseudo-elements parse as PseudoElement.Unknown (tolerate)
    // -------------------------------------------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: the spec requires parsers to tolerate unknown pseudo-elements
    /// (treat as invalid but do not crash). The implementation maps them to
    /// <see cref="PseudoElement.Unknown"/>.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void Unknown_pseudo_element_maps_to_Unknown_kind()
    {
        var selector = SelectorParser.ParseSelectorList("p::vendor-extension").Selectors.Single();
        selector.TargetPseudoElement.Should().Be(PseudoElement.Unknown);
        var pe = selector.RightmostCompound.SimpleSelectors
            .OfType<PseudoElementSelector>().Single();
        pe.Name.Should().Be("vendor-extension");
    }

    // -------------------------------------------------------------------------
    // §2  Pseudo-element must appear last in compound
    // -------------------------------------------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: a class selector following a pseudo-element in the same compound is
    /// invalid (a <see cref="FormatException"/> is thrown).</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void Class_after_pseudo_element_in_compound_is_invalid()
    {
        var act = () => SelectorParser.ParseSelectorList("p::before.foo");
        act.Should().Throw<FormatException>();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: an ID selector following a pseudo-element in the same compound is invalid.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void Id_after_pseudo_element_in_compound_is_invalid()
    {
        var act = () => SelectorParser.ParseSelectorList("p::before#id");
        act.Should().Throw<FormatException>();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: an attribute selector following a pseudo-element in the same compound is invalid.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void Attribute_after_pseudo_element_in_compound_is_invalid()
    {
        var act = () => SelectorParser.ParseSelectorList("p::before[attr]");
        act.Should().Throw<FormatException>();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: a type selector after a pseudo-element is invalid.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void Type_selector_preceding_pseudo_element_in_complex_is_valid()
    {
        // p.foo::before is valid — type and class before the pseudo-element.
        var selector = SelectorParser.ParseSelectorList("p.foo::before").Selectors.Single();
        selector.TargetPseudoElement.Should().Be(PseudoElement.Before);
        var simples = selector.RightmostCompound.SimpleSelectors;
        simples.Should().HaveCount(3);
        simples[0].Should().BeOfType<TypeSelector>();
        simples[1].Should().BeOfType<ClassSelector>();
        simples[2].Should().BeOfType<PseudoElementSelector>();
    }

    // -------------------------------------------------------------------------
    // §2  Pseudo-class after pseudo-element is valid
    // -------------------------------------------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: a pseudo-class (<c>:hover</c>) may follow a pseudo-element
    /// in the same compound (e.g. <c>::before:hover</c>).</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void Pseudo_class_after_pseudo_element_is_valid()
    {
        // ::before:hover is legal per CSS Pseudo-4 §2.
        var selector = SelectorParser.ParseSelectorList("p::before:hover").Selectors.Single();
        selector.TargetPseudoElement.Should().Be(PseudoElement.Before);
        var simples = selector.RightmostCompound.SimpleSelectors;
        // Expected: TypeSelector("p"), PseudoElementSelector(Before), PseudoClassSelector("hover")
        simples.Should().HaveCount(3);
        simples[2].Should().BeOfType<PseudoClassSelector>().Which.Name.Should().Be("hover");
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: <c>::after:not(.hidden)</c> — functional pseudo-class after
    /// pseudo-element is valid.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void Functional_pseudo_class_after_pseudo_element_is_valid()
    {
        var selector = SelectorParser.ParseSelectorList("p::after:not(.hidden)").Selectors.Single();
        selector.TargetPseudoElement.Should().Be(PseudoElement.After);
        selector.RightmostCompound.SimpleSelectors
            .OfType<PseudoClassSelector>()
            .Should().ContainSingle(pc => pc.Name == "not");
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: <c>::first-line:hover</c> — pseudo-class after ::first-line.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void First_line_with_hover_pseudo_class_is_valid()
    {
        var selector = SelectorParser.ParseSelectorList("p::first-line:hover").Selectors.Single();
        selector.TargetPseudoElement.Should().Be(PseudoElement.FirstLine);
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: <c>::selection:focus</c> — pseudo-class after ::selection.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void Selection_with_focus_pseudo_class_is_valid()
    {
        var selector = SelectorParser.ParseSelectorList("p::selection:focus").Selectors.Single();
        selector.TargetPseudoElement.Should().Be(PseudoElement.Selection);
        selector.RightmostCompound.SimpleSelectors
            .OfType<PseudoClassSelector>()
            .Should().ContainSingle(pc => pc.Name == "focus");
    }

    // -------------------------------------------------------------------------
    // §2  Standalone pseudo-element (no originating element prefix)
    // -------------------------------------------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: a standalone <c>::before</c> without a type qualifier is valid;
    /// the compound has a single simple selector.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void Standalone_before_without_type_selector_is_valid()
    {
        var selector = SelectorParser.ParseSelectorList("::before").Selectors.Single();
        selector.TargetPseudoElement.Should().Be(PseudoElement.Before);
        selector.RightmostCompound.SimpleSelectors.Should().HaveCount(1);
        selector.RightmostCompound.SimpleSelectors[0].Should().BeOfType<PseudoElementSelector>();
    }

    // -------------------------------------------------------------------------
    // §2  Selector list with multiple pseudo-element rules
    // -------------------------------------------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: a comma-separated selector list with multiple pseudo-element
    /// selectors parses both correctly.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void Selector_list_with_multiple_pseudo_elements()
    {
        var list = SelectorParser.ParseSelectorList("a::before, p::after");
        list.Selectors.Should().HaveCount(2);
        list.Selectors[0].TargetPseudoElement.Should().Be(PseudoElement.Before);
        list.Selectors[1].TargetPseudoElement.Should().Be(PseudoElement.After);
    }

    // -------------------------------------------------------------------------
    // §2  Universal selector with pseudo-element
    // -------------------------------------------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: <c>*::before</c> (universal + pseudo-element) parses correctly.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void Universal_selector_with_pseudo_element_parses()
    {
        var selector = SelectorParser.ParseSelectorList("*::before").Selectors.Single();
        selector.TargetPseudoElement.Should().Be(PseudoElement.Before);
        selector.RightmostCompound.SimpleSelectors[0].Should().BeOfType<UniversalSelector>();
    }

    // -------------------------------------------------------------------------
    // §4  Specificity of pseudo-elements
    // -------------------------------------------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-specificity"/>
    /// <para>§4: <c>p::before</c> — pseudo-element adds 1 to type count;
    /// specificity is (0, 0, 2).</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-specificity", section: "4")]
    [SpecFact]
    public void Specificity_of_type_plus_before_is_0_0_2()
    {
        var selector = SelectorParser.ParseSelectorList("p::before").Selectors.Single();
        selector.Specificity.Should().Be(new Specificity(0, 0, 2));
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-specificity"/>
    /// <para>§4: standalone <c>::before</c> specificity is (0, 0, 1).</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-specificity", section: "4")]
    [SpecFact]
    public void Specificity_of_standalone_before_is_0_0_1()
    {
        var selector = SelectorParser.ParseSelectorList("::before").Selectors.Single();
        selector.Specificity.Should().Be(new Specificity(0, 0, 1));
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-specificity"/>
    /// <para>§4: <c>#foo::after</c> — ID adds to ids column; specificity is (1, 0, 1).</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-specificity", section: "4")]
    [SpecFact]
    public void Specificity_of_id_plus_after_is_1_0_1()
    {
        var selector = SelectorParser.ParseSelectorList("#foo::after").Selectors.Single();
        selector.Specificity.Should().Be(new Specificity(1, 0, 1));
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-specificity"/>
    /// <para>§4: <c>.foo::before</c> — class adds to classes column; specificity is (0, 1, 1).</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-specificity", section: "4")]
    [SpecFact]
    public void Specificity_of_class_plus_before_is_0_1_1()
    {
        var selector = SelectorParser.ParseSelectorList(".foo::before").Selectors.Single();
        selector.Specificity.Should().Be(new Specificity(0, 1, 1));
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-specificity"/>
    /// <para>§4: <c>div.foo::before</c> — type + class + pseudo-element; specificity is (0, 1, 2).</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-specificity", section: "4")]
    [SpecFact]
    public void Specificity_of_type_class_before_is_0_1_2()
    {
        var selector = SelectorParser.ParseSelectorList("div.foo::before").Selectors.Single();
        selector.Specificity.Should().Be(new Specificity(0, 1, 2));
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-specificity"/>
    /// <para>§4: <c>::first-letter</c> specificity is (0, 0, 1).</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-specificity", section: "4")]
    [SpecFact]
    public void Specificity_of_first_letter_is_0_0_1()
    {
        var selector = SelectorParser.ParseSelectorList("::first-letter").Selectors.Single();
        selector.Specificity.Should().Be(new Specificity(0, 0, 1));
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-specificity"/>
    /// <para>§4: pseudo-element specificity compares as lower than a class selector
    /// (<c>::before</c> = (0,0,1) &lt; <c>.foo</c> = (0,1,0)).</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-specificity", section: "4")]
    [SpecFact]
    public void Pseudo_element_specificity_lower_than_class()
    {
        var pe = SelectorParser.ParseSelectorList("::before").Selectors.Single().Specificity;
        var cls = SelectorParser.ParseSelectorList(".foo").Selectors.Single().Specificity;
        pe.CompareTo(cls).Should().BeLessThan(0);
    }

    // -------------------------------------------------------------------------
    // §2  Serialisation — legacy :before normalised to ::before
    // -------------------------------------------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: serialising a legacy <c>:before</c> selector produces the
    /// canonical <c>::before</c> form.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void Legacy_before_serialises_as_double_colon()
    {
        var list = SelectorParser.ParseSelectorList("p:before");
        var text = SelectorSerializer.Serialize(list);
        text.Should().Be("p::before");
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: serialising <c>p::before</c> round-trips correctly.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void Double_colon_before_serialises_correctly()
    {
        var list = SelectorParser.ParseSelectorList("p::before");
        var text = SelectorSerializer.Serialize(list);
        text.Should().Be("p::before");
    }

    // -------------------------------------------------------------------------
    // §2  Matching — pseudo-element context filter
    // -------------------------------------------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: a selector with <c>::before</c> matches an element only when
    /// the match context specifies <see cref="PseudoElement.Before"/>.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void Pseudo_element_selector_matches_with_matching_context()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var selector = SelectorParser.ParseSelectorList("p::before").Selectors.Single();
        var result = SelectorMatcher.MatchWithResult(
            selector, p, new SelectorMatchContext { PseudoElement = PseudoElement.Before });
        result.Matched.Should().BeTrue();
        result.Pseudo.Should().Be(PseudoElement.Before);
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: a selector with <c>::before</c> does NOT match without a pseudo-element context.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void Pseudo_element_selector_does_not_match_without_context()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var selector = SelectorParser.ParseSelectorList("p::before").Selectors.Single();
        SelectorMatcher.MatchWithResult(selector, p).Matched.Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: a selector with <c>::before</c> does NOT match when context
    /// specifies a different pseudo-element (<c>::after</c>).</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void Pseudo_element_selector_does_not_match_wrong_pseudo_context()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var selector = SelectorParser.ParseSelectorList("p::before").Selectors.Single();
        SelectorMatcher.MatchWithResult(
            selector, p, new SelectorMatchContext { PseudoElement = PseudoElement.After })
            .Matched.Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: a plain element selector does NOT match when the context
    /// specifies a pseudo-element.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void Plain_selector_does_not_match_with_pseudo_context()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var selector = SelectorParser.ParseSelectorList("p").Selectors.Single();
        SelectorMatcher.MatchWithResult(
            selector, p, new SelectorMatchContext { PseudoElement = PseudoElement.Before })
            .Matched.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // §2  Matching — ::after with matched type selector
    // -------------------------------------------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: <c>p::after</c> matches a <c>p</c> element given an After context
    /// and returns the <see cref="PseudoElement.After"/> in the result.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void After_selector_matches_p_with_after_context()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var selector = SelectorParser.ParseSelectorList("p::after").Selectors.Single();
        var result = SelectorMatcher.MatchWithResult(
            selector, p, new SelectorMatchContext { PseudoElement = PseudoElement.After });
        result.Matched.Should().BeTrue();
        result.Pseudo.Should().Be(PseudoElement.After);
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: <c>p::after</c> does NOT match a <c>div</c> element (wrong type).</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void After_selector_does_not_match_wrong_element_type()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        doc.AppendChild(div);
        var selector = SelectorParser.ParseSelectorList("p::after").Selectors.Single();
        SelectorMatcher.MatchWithResult(
            selector, div, new SelectorMatchContext { PseudoElement = PseudoElement.After })
            .Matched.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // §2  Matching — combinator before pseudo-element (descendant context)
    // -------------------------------------------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: <c>div p::before</c> — pseudo-element selector with a descendant combinator;
    /// the pseudo-element is the target, and the originating element must be a <c>p</c>
    /// descending from a <c>div</c>.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void Descendant_combinator_before_pseudo_element_matches_correctly()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        var p = doc.CreateElement("p");
        doc.AppendChild(div);
        div.AppendChild(p);
        var selector = SelectorParser.ParseSelectorList("div p::before").Selectors.Single();
        // Two complex parts.
        selector.Parts.Should().HaveCount(2);
        selector.TargetPseudoElement.Should().Be(PseudoElement.Before);
        var result = SelectorMatcher.MatchWithResult(
            selector, p, new SelectorMatchContext { PseudoElement = PseudoElement.Before });
        result.Matched.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // §3  ::placeholder matching via placeholder-shown state
    // -------------------------------------------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#placeholder-pseudo"/>
    /// <para>§3.8: <c>input::placeholder</c> targets the Placeholder pseudo-element;
    /// it matches when the Placeholder context is given for an input element.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#placeholder-pseudo", section: "3.8")]
    [SpecFact]
    public void Placeholder_selector_matches_input_with_placeholder_context()
    {
        var doc = new Document();
        var input = doc.CreateElement("input");
        input.SetAttribute("placeholder", "Type here");
        doc.AppendChild(input);
        var selector = SelectorParser.ParseSelectorList("input::placeholder").Selectors.Single();
        var result = SelectorMatcher.MatchWithResult(
            selector, input, new SelectorMatchContext { PseudoElement = PseudoElement.Placeholder });
        result.Matched.Should().BeTrue();
        result.Pseudo.Should().Be(PseudoElement.Placeholder);
    }

    // -------------------------------------------------------------------------
    // §3  ::marker matching
    // -------------------------------------------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#marker-pseudo"/>
    /// <para>§3.7: <c>li::marker</c> targets the Marker pseudo-element;
    /// it matches an <c>li</c> element given the Marker context.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#marker-pseudo", section: "3.7")]
    [SpecFact]
    public void Marker_selector_matches_li_with_marker_context()
    {
        var doc = new Document();
        var ul = doc.CreateElement("ul");
        var li = doc.CreateElement("li");
        doc.AppendChild(ul);
        ul.AppendChild(li);
        var selector = SelectorParser.ParseSelectorList("li::marker").Selectors.Single();
        var result = SelectorMatcher.MatchWithResult(
            selector, li, new SelectorMatchContext { PseudoElement = PseudoElement.Marker });
        result.Matched.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // §3  ::selection matching
    // -------------------------------------------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#highlight-selectors"/>
    /// <para>§3.5: <c>p::selection</c> matches a <c>p</c> element with the Selection context.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#highlight-selectors", section: "3.5")]
    [SpecFact]
    public void Selection_selector_matches_p_with_selection_context()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var selector = SelectorParser.ParseSelectorList("p::selection").Selectors.Single();
        var result = SelectorMatcher.MatchWithResult(
            selector, p, new SelectorMatchContext { PseudoElement = PseudoElement.Selection });
        result.Matched.Should().BeTrue();
        result.Pseudo.Should().Be(PseudoElement.Selection);
    }

    // -------------------------------------------------------------------------
    // Pending: functional pseudo-elements (e.g. ::part(), ::slotted())
    // -------------------------------------------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: functional pseudo-elements like <c>::part(foo)</c> parse without
    /// throwing. The implementation parses them as <see cref="PseudoElement.Unknown"/>
    /// but does not yet assign a dedicated <see cref="PseudoElement"/> kind.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void Functional_pseudo_element_part_parses_with_dedicated_kind()
    {
        // When implemented, ::part(foo) should parse to a distinct PseudoElement kind
        // (e.g. PseudoElement.Part), not PseudoElement.Unknown.
        var selector = SelectorParser.ParseSelectorList("x-comp::part(foo)").Selectors.Single();
        selector.TargetPseudoElement.Should().NotBe(PseudoElement.Unknown);
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax"/>
    /// <para>§2: <c>::slotted(div)</c> is a functional pseudo-element; not yet
    /// assigned a dedicated kind.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/#pseudo-element-syntax", section: "2")]
    [SpecFact]
    public void Functional_pseudo_element_slotted_parses_with_dedicated_kind()
    {
        var selector = SelectorParser.ParseSelectorList("slot::slotted(div)").Selectors.Single();
        selector.TargetPseudoElement.Should().NotBe(PseudoElement.Unknown);
    }

    // -------------------------------------------------------------------------
    // Pending: ::details-content and ::cue (non-pseudo-4 extensions parsed correctly)
    // -------------------------------------------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/"/>
    /// <para>Extension: <c>::details-content</c> is a Starling extension beyond
    /// CSS Pseudo-4; it parses as <see cref="PseudoElement.DetailsContent"/>.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/", section: "3")]
    [SpecFact]
    public void Details_content_pseudo_element_parses()
    {
        var selector = SelectorParser.ParseSelectorList("details::details-content").Selectors.Single();
        selector.TargetPseudoElement.Should().Be(PseudoElement.DetailsContent);
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-pseudo-4/"/>
    /// <para>Extension: <c>::cue</c> (WebVTT) parses as <see cref="PseudoElement.Cue"/>.</para>
    /// </summary>
    [Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/", section: "3")]
    [SpecFact]
    public void Cue_pseudo_element_parses()
    {
        var selector = SelectorParser.ParseSelectorList("video::cue").Selectors.Single();
        selector.TargetPseudoElement.Should().Be(PseudoElement.Cue);
    }
}
