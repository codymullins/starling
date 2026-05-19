using FluentAssertions;
using Starling.Css.Selectors;
using Starling.Dom;
using Starling.Spec;

namespace Starling.Css.Tests;

[Spec("selectors-4", "https://www.w3.org/TR/selectors-4/")]

[TestClass]
public sealed class ModernPseudoClassTests
{
    [TestMethod]
    public void Dir_matches_rtl_when_element_has_dir_attribute()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        div.SetAttribute("dir", "rtl");
        doc.AppendChild(div);

        SelectorMatcher.Matches(SelectorParser.ParseSelectorList(":dir(rtl)"), div).Should().BeTrue();
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList(":dir(ltr)"), div).Should().BeFalse();
    }

    [TestMethod]
    public void Any_link_matches_anchor_with_href()
    {
        var doc = new Document();
        var a = doc.CreateElement("a");
        a.SetAttribute("href", "https://example.com/");
        doc.AppendChild(a);

        SelectorMatcher.Matches(SelectorParser.ParseSelectorList(":any-link"), a).Should().BeTrue();
    }

    [TestMethod]
    public void Any_link_does_not_match_anchor_without_href()
    {
        var doc = new Document();
        var a = doc.CreateElement("a");
        doc.AppendChild(a);

        SelectorMatcher.Matches(SelectorParser.ParseSelectorList(":any-link"), a).Should().BeFalse();
    }

    [TestMethod]
    public void Link_matches_anchor_with_href_and_not_visited()
    {
        var doc = new Document();
        var a = doc.CreateElement("a");
        a.SetAttribute("href", "https://example.com/");
        doc.AppendChild(a);

        SelectorMatcher.Matches(SelectorParser.ParseSelectorList(":link"), a).Should().BeTrue();
    }

    [TestMethod]
    public void Visited_never_matches_by_default()
    {
        var doc = new Document();
        var a = doc.CreateElement("a");
        a.SetAttribute("href", "https://example.com/");
        doc.AppendChild(a);

        SelectorMatcher.Matches(SelectorParser.ParseSelectorList(":visited"), a).Should().BeFalse();
    }

    [TestMethod]
    public void Scope_matches_scope_element_in_context()
    {
        var doc = new Document();
        var root = doc.CreateElement("section");
        var child = doc.CreateElement("p");
        doc.AppendChild(root);
        root.AppendChild(child);

        var ctx = new SelectorMatchContext { ScopeElement = root };
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList(":scope"), root, ctx).Should().BeTrue();
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList(":scope"), child, ctx).Should().BeFalse();
    }

    [TestMethod]
    public void Placeholder_shown_matches_input_with_placeholder_and_no_value()
    {
        var doc = new Document();
        var input = doc.CreateElement("input");
        input.SetAttribute("placeholder", "Type here");
        doc.AppendChild(input);

        SelectorMatcher.Matches(SelectorParser.ParseSelectorList(":placeholder-shown"), input).Should().BeTrue();
        input.SetAttribute("value", "abc");
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList(":placeholder-shown"), input).Should().BeFalse();
    }

    [TestMethod]
    [DataRow(":fullscreen")]
    [DataRow(":modal")]
    [DataRow(":picture-in-picture")]
    [DataRow(":user-valid")]
    [DataRow(":user-invalid")]
    [DataRow(":valid")]
    [DataRow(":invalid")]
    [DataRow(":in-range")]
    [DataRow(":out-of-range")]
    [DataRow(":blank")]
    [DataRow(":autofill")]
    public void Stubbed_pseudos_parse_without_throwing(string source)
    {
        var act = () => SelectorParser.ParseSelectorList(source);
        act.Should().NotThrow();
    }

    [TestMethod]
    public void Defined_returns_true_for_built_in_elements()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        doc.AppendChild(div);
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList(":defined"), div).Should().BeTrue();
    }

    [TestMethod]
    public void Required_and_optional_match_form_state()
    {
        var doc = new Document();
        var i1 = doc.CreateElement("input");
        i1.SetAttribute("required", "");
        var i2 = doc.CreateElement("input");
        doc.AppendChild(i1);
        doc.AppendChild(i2);

        SelectorMatcher.Matches(SelectorParser.ParseSelectorList(":required"), i1).Should().BeTrue();
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList(":optional"), i2).Should().BeTrue();
    }

    [TestMethod]
    public void Universal_selector_matches_any_element()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList("*"), p).Should().BeTrue();
    }

    [TestMethod]
    public void Namespace_prefix_parses_without_throwing()
    {
        // We don't enforce namespace semantics in v1; just ensure these parse and match local name.
        var doc = new Document();
        var circle = doc.CreateElement("circle");
        doc.AppendChild(circle);

        SelectorMatcher.Matches(SelectorParser.ParseSelectorList("svg|circle"), circle).Should().BeTrue();
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList("*|circle"), circle).Should().BeTrue();
    }
}
