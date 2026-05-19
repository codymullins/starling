using FluentAssertions;
using Tessera.Css.Selectors;
using Tessera.Dom;
using Xunit;
using Starling.Spec;

namespace Tessera.Css.Tests;

[Spec("selectors-4", "https://www.w3.org/TR/selectors-4/")]

public sealed class HasSelectorTests
{
    [Fact]
    public void Has_with_child_combinator_matches_when_direct_child_exists()
    {
        var doc = new Document();
        var section = doc.CreateElement("section");
        var a = doc.CreateElement("a");
        doc.AppendChild(section);
        section.AppendChild(a);

        var selector = SelectorParser.ParseSelectorList("section:has(> a)");
        SelectorMatcher.Matches(selector, section).Should().BeTrue();
    }

    [Fact]
    public void Has_with_child_combinator_does_not_match_when_only_descendant()
    {
        var doc = new Document();
        var section = doc.CreateElement("section");
        var div = doc.CreateElement("div");
        var a = doc.CreateElement("a");
        doc.AppendChild(section);
        section.AppendChild(div);
        div.AppendChild(a);

        var selector = SelectorParser.ParseSelectorList("section:has(> a)");
        SelectorMatcher.Matches(selector, section).Should().BeFalse();

        // Without the > combinator, descendants count.
        var descendant = SelectorParser.ParseSelectorList("section:has(a)");
        SelectorMatcher.Matches(descendant, section).Should().BeTrue();
    }

    [Fact]
    public void Has_with_next_sibling_combinator_matches_following_sibling()
    {
        var doc = new Document();
        var parent = doc.CreateElement("div");
        var h1 = doc.CreateElement("h1");
        var p = doc.CreateElement("p");
        doc.AppendChild(parent);
        parent.AppendChild(h1);
        parent.AppendChild(p);

        var selector = SelectorParser.ParseSelectorList("h1:has(+ p)");
        SelectorMatcher.Matches(selector, h1).Should().BeTrue();
        SelectorMatcher.Matches(selector, p).Should().BeFalse();
    }

    [Fact]
    public void Has_matches_descendant_class()
    {
        var doc = new Document();
        var container = doc.CreateElement("article");
        var inner = doc.CreateElement("div");
        inner.ClassList.Add("foo");
        doc.AppendChild(container);
        container.AppendChild(inner);

        var selector = SelectorParser.ParseSelectorList("article:has(.foo)");
        SelectorMatcher.Matches(selector, container).Should().BeTrue();
    }

    [Fact]
    public void Has_returns_false_when_descendant_missing()
    {
        var doc = new Document();
        var container = doc.CreateElement("article");
        doc.AppendChild(container);

        var selector = SelectorParser.ParseSelectorList("article:has(.foo)");
        SelectorMatcher.Matches(selector, container).Should().BeFalse();
    }

    [Fact]
    public void Has_recursion_depth_is_bounded()
    {
        // Build a small chain :has(:has(...)) and ensure we don't stack-overflow.
        var doc = new Document();
        var root = doc.CreateElement("div");
        var a = doc.CreateElement("div");
        var b = doc.CreateElement("div");
        doc.AppendChild(root);
        root.AppendChild(a);
        a.AppendChild(b);

        var nested = string.Concat(Enumerable.Repeat("div:has(", 20)) + "div" + new string(')', 20);
        var selector = SelectorParser.ParseSelectorList(nested);
        // We just need it to terminate; the depth bound caps reentry.
        var act = () => SelectorMatcher.Matches(selector, root);
        act.Should().NotThrow();
    }

    [Fact]
    public void Has_argument_lists_are_disjunctive()
    {
        var doc = new Document();
        var section = doc.CreateElement("section");
        var img = doc.CreateElement("img");
        doc.AppendChild(section);
        section.AppendChild(img);

        var selector = SelectorParser.ParseSelectorList("section:has(.missing, img)");
        SelectorMatcher.Matches(selector, section).Should().BeTrue();
    }
}
