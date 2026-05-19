using FluentAssertions;
using Starling.Css.Selectors;
using Starling.Spec;

namespace Starling.Css.Tests;

[Spec("selectors-4", "https://www.w3.org/TR/selectors-4/")]

[TestClass]
public sealed class IsSpecificityTests
{
    [TestMethod]
    public void Is_takes_highest_specificity_of_its_arguments()
    {
        var s = SelectorParser.ParseSelectorList(":is(#id, .cls)").Selectors.Single();
        s.Specificity.Should().Be(new Specificity(1, 0, 0));
    }

    [TestMethod]
    public void Where_has_zero_specificity()
    {
        var s = SelectorParser.ParseSelectorList(":where(#id)").Selectors.Single();
        s.Specificity.Should().Be(Specificity.Zero);
    }

    [TestMethod]
    public void Not_takes_highest_specificity_of_its_arguments()
    {
        var s = SelectorParser.ParseSelectorList(":not(#id, .cls)").Selectors.Single();
        s.Specificity.Should().Be(new Specificity(1, 0, 0));
    }

    [TestMethod]
    public void Has_takes_highest_specificity_of_its_arguments()
    {
        var s = SelectorParser.ParseSelectorList(":has(#id)").Selectors.Single();
        s.Specificity.Should().Be(new Specificity(1, 0, 0));
    }

    [TestMethod]
    public void Nested_compound_specificity_sums_correctly()
    {
        // a.cls = (0,1,1); :is(#x) inside another compound = (1,0,0) + the rest.
        var s = SelectorParser.ParseSelectorList("a.cls:is(#x)").Selectors.Single();
        s.Specificity.Should().Be(new Specificity(1, 1, 1));
    }

    [TestMethod]
    public void Universal_does_not_contribute_to_specificity()
    {
        var s = SelectorParser.ParseSelectorList("*").Selectors.Single();
        s.Specificity.Should().Be(Specificity.Zero);
    }
}
