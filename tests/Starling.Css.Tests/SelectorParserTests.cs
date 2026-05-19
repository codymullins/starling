using FluentAssertions;
using Tessera.Css.Selectors;
using Xunit;
using Starling.Spec;

namespace Tessera.Css.Tests;

[Spec("selectors-4", "https://www.w3.org/TR/selectors-4/")]

public sealed class SelectorParserTests
{
    [Fact]
    public void Parses_compound_selector_with_id_class_attribute_and_pseudo()
    {
        var selector = SelectorParser.ParseSelectorList("article#main.card[data-kind='promo']:first-child")
            .Selectors.Should().ContainSingle().Subject;

        selector.Parts.Should().ContainSingle();
        var simpleSelectors = selector.RightmostCompound.SimpleSelectors;
        simpleSelectors[0].Should().BeOfType<TypeSelector>().Which.LocalName.Should().Be("article");
        simpleSelectors[1].Should().BeOfType<IdSelector>().Which.Id.Should().Be("main");
        simpleSelectors[2].Should().BeOfType<ClassSelector>().Which.ClassName.Should().Be("card");
        simpleSelectors[3].Should().BeOfType<AttributeSelector>().Which.Should().Match<AttributeSelector>(
            attr => attr.Name == "data-kind" &&
                    attr.Operator == AttributeOperator.Equals &&
                    attr.Value == "promo");
        simpleSelectors[4].Should().BeOfType<PseudoClassSelector>().Which.Name.Should().Be("first-child");
        selector.Specificity.Should().Be(new Specificity(1, 3, 1));
    }

    [Fact]
    public void Preserves_descendant_and_explicit_combinators()
    {
        var selector = SelectorParser.ParseSelectorList("main > article.card p + a")
            .Selectors.Should().ContainSingle().Subject;

        selector.Parts.Select(p => p.CombinatorFromPrevious).Should().ContainInOrder(
            SelectorCombinator.None,
            SelectorCombinator.Child,
            SelectorCombinator.Descendant,
            SelectorCombinator.NextSibling);
    }

    [Fact]
    public void Where_has_zero_specificity_and_is_uses_argument_specificity()
    {
        var where = SelectorParser.ParseSelectorList(":where(#hero)").Selectors.Single();
        var isSelector = SelectorParser.ParseSelectorList(":is(#hero, .card)").Selectors.Single();

        where.Specificity.Should().Be(Specificity.Zero);
        isSelector.Specificity.Should().Be(new Specificity(1, 0, 0));
    }

    [Fact]
    public void Pseudo_class_is_allowed_after_pseudo_element()
    {
        // ::-webkit-scrollbar-thumb:window-inactive — real selector encountered on mcmaster.com.
        var selector = SelectorParser.ParseSelectorList("::-webkit-scrollbar-thumb:window-inactive")
            .Selectors.Should().ContainSingle().Subject;

        var simples = selector.RightmostCompound.SimpleSelectors;
        simples.Should().HaveCount(2);
        simples[0].Should().BeOfType<PseudoElementSelector>()
            .Which.Name.Should().Be("-webkit-scrollbar-thumb");
        simples[1].Should().BeOfType<PseudoClassSelector>()
            .Which.Name.Should().Be("window-inactive");
    }

    [Fact]
    public void Pseudo_element_target_is_recognized_with_trailing_pseudo_class()
    {
        // ::before:hover must still be treated as a pseudo-element-targeting rule for the cascade,
        // not as a constraint on the element itself.
        var selector = SelectorParser.ParseSelectorList("a::before:hover").Selectors.Single();
        selector.TargetPseudoElement.Should().Be(PseudoElement.Before);
    }

    [Fact]
    public void Class_after_pseudo_element_is_rejected()
    {
        Action act = () => SelectorParser.ParseSelectorList("::before.foo");
        act.Should().Throw<FormatException>();
    }
}
