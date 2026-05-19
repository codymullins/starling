using FluentAssertions;
using Starling.Css.Selectors;
using Starling.Dom;
using Starling.Spec;

namespace Starling.Css.Tests;

[Spec("css-pseudo-4", "https://www.w3.org/TR/css-pseudo-4/")]

[TestClass]
public sealed class PseudoElementTests
{
    [TestMethod]
    public void Double_colon_before_parses_and_carries_kind()
    {
        var selector = SelectorParser.ParseSelectorList("p::before").Selectors.Single();
        var simples = selector.RightmostCompound.SimpleSelectors;
        simples.Should().HaveCount(2);
        simples[1].Should().BeOfType<PseudoElementSelector>().Which.Kind.Should().Be(PseudoElement.Before);
        selector.TargetPseudoElement.Should().Be(PseudoElement.Before);
    }

    [TestMethod]
    public void Legacy_single_colon_pseudo_elements_become_pseudo_element()
    {
        var selector = SelectorParser.ParseSelectorList("p:before").Selectors.Single();
        var simples = selector.RightmostCompound.SimpleSelectors;
        simples[1].Should().BeOfType<PseudoElementSelector>().Which.Kind.Should().Be(PseudoElement.Before);
    }

    [TestMethod]
    public void Pseudo_element_matches_on_element_with_pseudo_filter()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);

        var selector = SelectorParser.ParseSelectorList("p::before").Selectors.Single();
        // Default context (no pseudo): rule with pseudo-element target should NOT match.
        SelectorMatcher.MatchWithResult(selector, p).Matched.Should().BeFalse();
        // With pseudo context: it should match and bubble up Before.
        var result = SelectorMatcher.MatchWithResult(selector, p,
            new SelectorMatchContext { PseudoElement = PseudoElement.Before });
        result.Matched.Should().BeTrue();
        result.Pseudo.Should().Be(PseudoElement.Before);
    }

    [TestMethod]
    public void Rule_without_pseudo_element_does_not_match_with_pseudo_context()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);

        var selector = SelectorParser.ParseSelectorList("p").Selectors.Single();
        SelectorMatcher.MatchWithResult(selector, p,
            new SelectorMatchContext { PseudoElement = PseudoElement.Before }).Matched.Should().BeFalse();
    }

    [TestMethod]
    public void Pseudo_element_must_be_last_simple_selector()
    {
        // ::before.foo is illegal because .foo follows the pseudo-element.
        var act = () => SelectorParser.ParseSelectorList("p::before.foo");
        act.Should().Throw<FormatException>();
    }

    [TestMethod]
    [DataRow("::marker", PseudoElement.Marker)]
    [DataRow("::placeholder", PseudoElement.Placeholder)]
    [DataRow("::first-line", PseudoElement.FirstLine)]
    [DataRow("::first-letter", PseudoElement.FirstLetter)]
    [DataRow("::selection", PseudoElement.Selection)]
    [DataRow("::backdrop", PseudoElement.Backdrop)]
    [DataRow("::file-selector-button", PseudoElement.FileSelectorButton)]
    [DataRow("::details-content", PseudoElement.DetailsContent)]
    [DataRow("::cue", PseudoElement.Cue)]
    public void Recognized_pseudo_elements_have_expected_kind(string source, PseudoElement expected)
    {
        var selector = SelectorParser.ParseSelectorList($"div{source}").Selectors.Single();
        selector.TargetPseudoElement.Should().Be(expected);
    }
}
