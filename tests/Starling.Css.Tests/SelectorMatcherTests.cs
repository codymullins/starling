using FluentAssertions;
using Starling.Css.Selectors;
using Starling.Dom;
using Starling.Spec;

namespace Starling.Css.Tests;

[Spec("selectors-4", "https://www.w3.org/TR/selectors-4/")]

[TestClass]
public sealed class SelectorMatcherTests
{
    [TestMethod]
    public void Matches_right_to_left_across_combinators()
    {
        var doc = new Document();
        var main = doc.CreateElement("main");
        var article = doc.CreateElement("article");
        article.ClassList.Add("card");
        var p = doc.CreateElement("p");
        var a = doc.CreateElement("a");
        doc.AppendChild(main);
        main.AppendChild(article);
        article.AppendChild(p);
        article.AppendChild(a);

        var selector = SelectorParser.ParseSelectorList("main > article.card p + a");

        SelectorMatcher.Matches(selector, a).Should().BeTrue();
        SelectorMatcher.Matches(selector, p).Should().BeFalse();
    }

    [TestMethod]
    public void Matches_attribute_operators_and_structural_pseudos()
    {
        var doc = new Document();
        var root = doc.CreateElement("div");
        var first = doc.CreateElement("span");
        var second = doc.CreateElement("span");
        second.SetAttribute("data-tags", "alpha beta");
        doc.AppendChild(root);
        root.AppendChild(first);
        root.AppendChild(second);

        SelectorMatcher.Matches(SelectorParser.ParseSelectorList("span:first-child"), first).Should().BeTrue();
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList("span:nth-child(2)[data-tags~=beta]"), second).Should().BeTrue();
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList("span:only-child"), second).Should().BeFalse();
    }

    [TestMethod]
    public void Matches_is_where_not_and_lang()
    {
        var doc = new Document();
        var root = doc.CreateElement("div");
        root.SetAttribute("lang", "en-US");
        var child = doc.CreateElement("p");
        child.ClassList.Add("intro");
        doc.AppendChild(root);
        root.AppendChild(child);

        SelectorMatcher.Matches(SelectorParser.ParseSelectorList(":is(p, a).intro:not(.hidden):lang(en)"), child)
            .Should().BeTrue();
    }
}
