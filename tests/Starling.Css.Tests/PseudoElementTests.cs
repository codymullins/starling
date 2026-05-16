using FluentAssertions;
using Tessera.Css.Selectors;
using Tessera.Dom;
using Xunit;

namespace Tessera.Css.Tests;

public sealed class PseudoElementTests
{
    [Fact]
    public void Double_colon_before_parses_and_carries_kind()
    {
        var selector = SelectorParser.ParseSelectorList("p::before").Selectors.Single();
        var simples = selector.RightmostCompound.SimpleSelectors;
        simples.Should().HaveCount(2);
        simples[1].Should().BeOfType<PseudoElementSelector>().Which.Kind.Should().Be(PseudoElement.Before);
        selector.TargetPseudoElement.Should().Be(PseudoElement.Before);
    }

    [Fact]
    public void Legacy_single_colon_pseudo_elements_become_pseudo_element()
    {
        var selector = SelectorParser.ParseSelectorList("p:before").Selectors.Single();
        var simples = selector.RightmostCompound.SimpleSelectors;
        simples[1].Should().BeOfType<PseudoElementSelector>().Which.Kind.Should().Be(PseudoElement.Before);
    }

    [Fact]
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

    [Fact]
    public void Rule_without_pseudo_element_does_not_match_with_pseudo_context()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);

        var selector = SelectorParser.ParseSelectorList("p").Selectors.Single();
        SelectorMatcher.MatchWithResult(selector, p,
            new SelectorMatchContext { PseudoElement = PseudoElement.Before }).Matched.Should().BeFalse();
    }

    [Fact]
    public void Pseudo_element_must_be_last_simple_selector()
    {
        // ::before.foo is illegal because .foo follows the pseudo-element.
        var act = () => SelectorParser.ParseSelectorList("p::before.foo");
        act.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("::marker", PseudoElement.Marker)]
    [InlineData("::placeholder", PseudoElement.Placeholder)]
    [InlineData("::first-line", PseudoElement.FirstLine)]
    [InlineData("::first-letter", PseudoElement.FirstLetter)]
    [InlineData("::selection", PseudoElement.Selection)]
    [InlineData("::backdrop", PseudoElement.Backdrop)]
    [InlineData("::file-selector-button", PseudoElement.FileSelectorButton)]
    [InlineData("::details-content", PseudoElement.DetailsContent)]
    [InlineData("::cue", PseudoElement.Cue)]
    public void Recognized_pseudo_elements_have_expected_kind(string source, PseudoElement expected)
    {
        var selector = SelectorParser.ParseSelectorList($"div{source}").Selectors.Single();
        selector.TargetPseudoElement.Should().Be(expected);
    }
}
