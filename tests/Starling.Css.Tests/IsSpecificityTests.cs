using FluentAssertions;
using Tessera.Css.Selectors;
using Xunit;

namespace Tessera.Css.Tests;

public sealed class IsSpecificityTests
{
    [Fact]
    public void Is_takes_highest_specificity_of_its_arguments()
    {
        var s = SelectorParser.ParseSelectorList(":is(#id, .cls)").Selectors.Single();
        s.Specificity.Should().Be(new Specificity(1, 0, 0));
    }

    [Fact]
    public void Where_has_zero_specificity()
    {
        var s = SelectorParser.ParseSelectorList(":where(#id)").Selectors.Single();
        s.Specificity.Should().Be(Specificity.Zero);
    }

    [Fact]
    public void Not_takes_highest_specificity_of_its_arguments()
    {
        var s = SelectorParser.ParseSelectorList(":not(#id, .cls)").Selectors.Single();
        s.Specificity.Should().Be(new Specificity(1, 0, 0));
    }

    [Fact]
    public void Has_takes_highest_specificity_of_its_arguments()
    {
        var s = SelectorParser.ParseSelectorList(":has(#id)").Selectors.Single();
        s.Specificity.Should().Be(new Specificity(1, 0, 0));
    }

    [Fact]
    public void Nested_compound_specificity_sums_correctly()
    {
        // a.cls = (0,1,1); :is(#x) inside another compound = (1,0,0) + the rest.
        var s = SelectorParser.ParseSelectorList("a.cls:is(#x)").Selectors.Single();
        s.Specificity.Should().Be(new Specificity(1, 1, 1));
    }

    [Fact]
    public void Universal_does_not_contribute_to_specificity()
    {
        var s = SelectorParser.ParseSelectorList("*").Selectors.Single();
        s.Specificity.Should().Be(Specificity.Zero);
    }
}
