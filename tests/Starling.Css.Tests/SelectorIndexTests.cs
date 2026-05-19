using FluentAssertions;
using Starling.Css.Selectors;
using Starling.Dom;
using Starling.Spec;

namespace Starling.Css.Tests;

[Spec("selectors-4", "https://www.w3.org/TR/selectors-4/")]

[TestClass]
public sealed class SelectorIndexTests
{
    [TestMethod]
    public void Buckets_pseudo_element_rule_by_last_non_pseudo_compound()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);

        var index = new SelectorIndex<int>();
        index.Add(SelectorParser.ParseSelectorList("p::before"), 1);
        index.Add(SelectorParser.ParseSelectorList("div::after"), 2);

        var candidates = index.GetCandidates(p);
        candidates.Should().ContainSingle(e => e.Value == 1);
        candidates.Single().PseudoElementTarget.Should().Be(PseudoElement.Before);
    }

    [TestMethod]
    public void Buckets_id_class_and_tag_correctly()
    {
        var doc = new Document();
        var el = doc.CreateElement("article");
        el.Id = "hero";
        el.ClassList.Add("card");
        doc.AppendChild(el);

        var index = new SelectorIndex<string>();
        index.Add(SelectorParser.ParseSelectorList("#hero"), "id");
        index.Add(SelectorParser.ParseSelectorList(".card"), "class");
        index.Add(SelectorParser.ParseSelectorList("article"), "tag");
        index.Add(SelectorParser.ParseSelectorList("*"), "universal");

        var candidates = index.GetCandidates(el).Select(c => c.Value).ToList();
        candidates.Should().Contain(new[] { "id", "class", "tag", "universal" });
    }
}
