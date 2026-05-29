using AwesomeAssertions;
using Starling.Css.Selectors;
using Starling.Dom;

namespace Starling.Css.Spec.Tests.Selectors4;

/// <summary>
/// Comprehensive matching conformance for
/// <see href="https://www.w3.org/TR/selectors-4/">Selectors Level 4</see>.
/// Tests call <see cref="SelectorMatcher"/> directly, building minimal
/// <see cref="Document"/> trees as fixtures.
/// </summary>
[TestClass]
[Spec("selectors-4", "https://www.w3.org/TR/selectors-4/")]
public sealed class Selectors4Tests
{
    // ─── helpers ────────────────────────────────────────────────────────────

    private static bool Matches(string selector, Element element, SelectorMatchContext? ctx = null)
        => SelectorMatcher.Matches(SelectorParser.ParseSelectorList(selector), element, ctx);

    // ========================================================================
    // §4 — Simple selectors
    // ========================================================================

    // §4.1 Type selector
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#type-selectors", section: "4.1")]
    [SpecFact]
    public void Type_selector_matches_element_by_local_name()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        Matches("p", p).Should().BeTrue();
        Matches("div", p).Should().BeFalse();
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#type-selectors", section: "4.1")]
    [SpecFact]
    public void Type_selector_is_case_insensitive_for_html_elements()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        doc.AppendChild(div);
        Matches("DIV", div).Should().BeTrue();
        Matches("Div", div).Should().BeTrue();
    }

    // §4.2 Universal selector
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-universal-selector", section: "4.2")]
    [SpecFact]
    public void Universal_selector_matches_any_element()
    {
        var doc = new Document();
        var span = doc.CreateElement("span");
        var div = doc.CreateElement("div");
        doc.AppendChild(span);
        doc.AppendChild(div);
        Matches("*", span).Should().BeTrue();
        Matches("*", div).Should().BeTrue();
    }

    // §4.3 Attribute selectors — existence
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#attribute-selectors", section: "6.1")]
    [SpecFact]
    public void Attribute_existence_selector_matches_when_attribute_present()
    {
        var doc = new Document();
        var a = doc.CreateElement("a");
        a.SetAttribute("href", "https://example.com");
        doc.AppendChild(a);
        Matches("[href]", a).Should().BeTrue();
        Matches("[title]", a).Should().BeFalse();
    }

    // §6.1 Attribute equality
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#attribute-selectors", section: "6.1")]
    [SpecFact]
    public void Attribute_equality_selector_matches_exact_value()
    {
        var doc = new Document();
        var input = doc.CreateElement("input");
        input.SetAttribute("type", "text");
        doc.AppendChild(input);
        Matches("[type=text]", input).Should().BeTrue();
        Matches("[type=password]", input).Should().BeFalse();
    }

    // §6.1 Attribute includes (~=)
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#attribute-selectors", section: "6.1")]
    [SpecFact]
    public void Attribute_includes_selector_matches_whitespace_separated_word()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        div.SetAttribute("class", "foo bar baz");
        doc.AppendChild(div);
        Matches("[class~=bar]", div).Should().BeTrue();
        Matches("[class~=qux]", div).Should().BeFalse();
    }

    // §6.1 Attribute dash-match (|=)
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#attribute-selectors", section: "6.1")]
    [SpecFact]
    public void Attribute_dash_match_selector_matches_value_or_hyphen_prefix()
    {
        var doc = new Document();
        var span = doc.CreateElement("span");
        span.SetAttribute("lang", "en-US");
        doc.AppendChild(span);
        Matches("[lang|=en]", span).Should().BeTrue();
        var span2 = doc.CreateElement("span");
        span2.SetAttribute("lang", "en");
        doc.AppendChild(span2);
        Matches("[lang|=en]", span2).Should().BeTrue();
        var span3 = doc.CreateElement("span");
        span3.SetAttribute("lang", "fr");
        doc.AppendChild(span3);
        Matches("[lang|=en]", span3).Should().BeFalse();
    }

    // §6.1 Attribute prefix (^=)
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#attribute-selectors", section: "6.1")]
    [SpecFact]
    public void Attribute_prefix_selector_matches_value_starting_with()
    {
        var doc = new Document();
        var a = doc.CreateElement("a");
        a.SetAttribute("href", "https://example.com");
        doc.AppendChild(a);
        Matches("[href^=https]", a).Should().BeTrue();
        Matches("[href^=ftp]", a).Should().BeFalse();
        var b = doc.CreateElement("a");
        b.SetAttribute("data-id", "prefix-123");
        doc.AppendChild(b);
        Matches("[data-id^=prefix]", b).Should().BeTrue();
        Matches("[data-id^=suffix]", b).Should().BeFalse();
    }

    // §6.1 Attribute suffix ($=)
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#attribute-selectors", section: "6.1")]
    [SpecFact]
    public void Attribute_suffix_selector_matches_value_ending_with()
    {
        var doc = new Document();
        var img = doc.CreateElement("img");
        img.SetAttribute("src", "photo.png");
        doc.AppendChild(img);
        // Use quoted value for strings starting with '.' (which CSS parses as class selector otherwise)
        Matches("[src$=\".png\"]", img).Should().BeTrue();
        Matches("[src$=\".jpg\"]", img).Should().BeFalse();
        // Also verify with unambiguous suffix
        Matches("[src$=png]", img).Should().BeTrue();
        Matches("[src$=jpg]", img).Should().BeFalse();
    }

    // §6.1 Attribute substring (*=)
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#attribute-selectors", section: "6.1")]
    [SpecFact]
    public void Attribute_substring_selector_matches_value_containing()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        div.SetAttribute("data-value", "hello-world");
        doc.AppendChild(div);
        Matches("[data-value*=world]", div).Should().BeTrue();
        Matches("[data-value*=xyz]", div).Should().BeFalse();
    }

    // §6.1 Attribute case-insensitive flag (i)
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#attribute-case", section: "6.2")]
    [SpecFact]
    public void Attribute_equality_case_insensitive_flag_matches_regardless_of_case()
    {
        var doc = new Document();
        var input = doc.CreateElement("input");
        input.SetAttribute("type", "TEXT");
        doc.AppendChild(input);
        Matches("[type=text i]", input).Should().BeTrue();
        Matches("[type=text]", input).Should().BeFalse();
    }

    // §4.2 Class selector
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#class-html", section: "4.2")]
    [SpecFact]
    public void Class_selector_matches_element_with_that_class()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        div.ClassList.Add("active");
        doc.AppendChild(div);
        Matches(".active", div).Should().BeTrue();
        Matches(".hidden", div).Should().BeFalse();
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#class-html", section: "4.2")]
    [SpecFact]
    public void Multiple_class_selectors_require_all_classes_present()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        div.ClassList.Add("foo");
        div.ClassList.Add("bar");
        doc.AppendChild(div);
        Matches(".foo.bar", div).Should().BeTrue();
        Matches(".foo.baz", div).Should().BeFalse();
    }

    // §4.2 ID selector
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#id-selectors", section: "4.2")]
    [SpecFact]
    public void Id_selector_matches_element_with_that_id()
    {
        var doc = new Document();
        var h1 = doc.CreateElement("h1");
        h1.SetAttribute("id", "title");
        doc.AppendChild(h1);
        Matches("#title", h1).Should().BeTrue();
        Matches("#subtitle", h1).Should().BeFalse();
    }

    // ========================================================================
    // §13 — Combinators
    // ========================================================================

    // §13.1 Descendant combinator (space)
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#descendant-combinators", section: "13.1")]
    [SpecFact]
    public void Descendant_combinator_matches_any_descendant()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        var section = doc.CreateElement("section");
        var p = doc.CreateElement("p");
        doc.AppendChild(div);
        div.AppendChild(section);
        section.AppendChild(p);
        // p is a descendant of div (through section)
        Matches("div p", p).Should().BeTrue();
        Matches("section p", p).Should().BeTrue();
        Matches("div p", section).Should().BeFalse();
    }

    // §13.2 Child combinator (>)
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#child-combinators", section: "13.2")]
    [SpecFact]
    public void Child_combinator_matches_only_direct_child()
    {
        var doc = new Document();
        var ul = doc.CreateElement("ul");
        var li = doc.CreateElement("li");
        var span = doc.CreateElement("span");
        doc.AppendChild(ul);
        ul.AppendChild(li);
        li.AppendChild(span);
        Matches("ul > li", li).Should().BeTrue();
        // span is a grandchild, not direct child of ul
        Matches("ul > span", span).Should().BeFalse();
    }

    // §13.3 Next-sibling combinator (+)
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#adjacent-sibling-combinators", section: "13.3")]
    [SpecFact]
    public void Next_sibling_combinator_matches_immediately_following_sibling()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        var h1 = doc.CreateElement("h1");
        var p = doc.CreateElement("p");
        var em = doc.CreateElement("em");
        doc.AppendChild(div);
        div.AppendChild(h1);
        div.AppendChild(p);
        div.AppendChild(em);
        Matches("h1 + p", p).Should().BeTrue();
        // em follows p, not h1 directly
        Matches("h1 + em", em).Should().BeFalse();
        Matches("p + em", em).Should().BeTrue();
    }

    // §13.4 Subsequent-sibling combinator (~)
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#general-sibling-combinators", section: "13.4")]
    [SpecFact]
    public void Subsequent_sibling_combinator_matches_any_following_sibling()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        var h1 = doc.CreateElement("h1");
        var p = doc.CreateElement("p");
        var em = doc.CreateElement("em");
        doc.AppendChild(div);
        div.AppendChild(h1);
        div.AppendChild(p);
        div.AppendChild(em);
        Matches("h1 ~ p", p).Should().BeTrue();
        Matches("h1 ~ em", em).Should().BeTrue();
        // h1 is before p, not after
        Matches("p ~ h1", h1).Should().BeFalse();
    }

    // ========================================================================
    // §15 — Structural pseudo-classes
    // ========================================================================

    // §15.1 :root
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-root-pseudo", section: "15.1")]
    [SpecFact]
    public void Root_pseudo_matches_document_element()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        doc.AppendChild(html);
        html.AppendChild(body);
        Matches(":root", html).Should().BeTrue();
        Matches(":root", body).Should().BeFalse();
    }

    // §15.2 :empty
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-empty-pseudo", section: "15.2")]
    [SpecFact]
    public void Empty_pseudo_matches_element_with_no_content()
    {
        var doc = new Document();
        var empty = doc.CreateElement("p");
        var nonempty = doc.CreateElement("p");
        var child = doc.CreateElement("span");
        doc.AppendChild(empty);
        doc.AppendChild(nonempty);
        nonempty.AppendChild(child);
        Matches(":empty", empty).Should().BeTrue();
        Matches(":empty", nonempty).Should().BeFalse();
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-empty-pseudo", section: "15.2")]
    [SpecFact]
    public void Empty_pseudo_does_not_match_element_with_text_content()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        p.AppendChild(doc.CreateTextNode("hello"));
        Matches(":empty", p).Should().BeFalse();
    }

    // §15.3 :first-child
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-first-child-pseudo", section: "15.3")]
    [SpecFact]
    public void First_child_pseudo_matches_first_element_child()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        var a = doc.CreateElement("span");
        var b = doc.CreateElement("span");
        doc.AppendChild(div);
        div.AppendChild(a);
        div.AppendChild(b);
        Matches(":first-child", a).Should().BeTrue();
        Matches(":first-child", b).Should().BeFalse();
    }

    // §15.4 :last-child
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-last-child-pseudo", section: "15.4")]
    [SpecFact]
    public void Last_child_pseudo_matches_last_element_child()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        var a = doc.CreateElement("span");
        var b = doc.CreateElement("span");
        doc.AppendChild(div);
        div.AppendChild(a);
        div.AppendChild(b);
        Matches(":last-child", b).Should().BeTrue();
        Matches(":last-child", a).Should().BeFalse();
    }

    // §15.5 :only-child
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-only-child-pseudo", section: "15.5")]
    [SpecFact]
    public void Only_child_pseudo_matches_sole_element_child()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        var only = doc.CreateElement("p");
        doc.AppendChild(div);
        div.AppendChild(only);
        Matches(":only-child", only).Should().BeTrue();
        var sibling = doc.CreateElement("p");
        div.AppendChild(sibling);
        Matches(":only-child", only).Should().BeFalse();
    }

    // §15.6 :first-of-type
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-first-of-type-pseudo", section: "15.6")]
    [SpecFact]
    public void First_of_type_pseudo_matches_first_of_its_tag_among_siblings()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        var p1 = doc.CreateElement("p");
        var span = doc.CreateElement("span");
        var p2 = doc.CreateElement("p");
        doc.AppendChild(div);
        div.AppendChild(p1);
        div.AppendChild(span);
        div.AppendChild(p2);
        Matches("p:first-of-type", p1).Should().BeTrue();
        Matches("p:first-of-type", p2).Should().BeFalse();
        Matches("span:first-of-type", span).Should().BeTrue();
    }

    // §15.7 :last-of-type
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-last-of-type-pseudo", section: "15.7")]
    [SpecFact]
    public void Last_of_type_pseudo_matches_last_of_its_tag_among_siblings()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        var p1 = doc.CreateElement("p");
        var p2 = doc.CreateElement("p");
        doc.AppendChild(div);
        div.AppendChild(p1);
        div.AppendChild(p2);
        Matches("p:last-of-type", p2).Should().BeTrue();
        Matches("p:last-of-type", p1).Should().BeFalse();
    }

    // §15.8 :only-of-type
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-only-of-type-pseudo", section: "15.8")]
    [SpecFact]
    public void Only_of_type_pseudo_matches_sole_element_of_that_tag()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        var span = doc.CreateElement("span");
        var p = doc.CreateElement("p");
        doc.AppendChild(div);
        div.AppendChild(span);
        div.AppendChild(p);
        // p is the only <p>, span is the only <span>
        Matches("p:only-of-type", p).Should().BeTrue();
        Matches("span:only-of-type", span).Should().BeTrue();
        var p2 = doc.CreateElement("p");
        div.AppendChild(p2);
        // Now there are two <p> — neither is only-of-type
        Matches("p:only-of-type", p).Should().BeFalse();
        Matches("p:only-of-type", p2).Should().BeFalse();
    }

    // §15.9 :nth-child — keyword "odd"
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-nth-child-pseudo", section: "15.9")]
    [SpecFact]
    public void Nth_child_odd_matches_1st_3rd_5th_children()
    {
        var doc = new Document();
        var ul = doc.CreateElement("ul");
        doc.AppendChild(ul);
        var items = Enumerable.Range(0, 5)
            .Select(_ => { var li = doc.CreateElement("li"); ul.AppendChild(li); return li; })
            .ToArray();
        // 1-based: odd = 1, 3, 5
        Matches("li:nth-child(odd)", items[0]).Should().BeTrue();
        Matches("li:nth-child(odd)", items[1]).Should().BeFalse();
        Matches("li:nth-child(odd)", items[2]).Should().BeTrue();
        Matches("li:nth-child(odd)", items[3]).Should().BeFalse();
        Matches("li:nth-child(odd)", items[4]).Should().BeTrue();
    }

    // §15.9 :nth-child — keyword "even"
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-nth-child-pseudo", section: "15.9")]
    [SpecFact]
    public void Nth_child_even_matches_2nd_4th_children()
    {
        var doc = new Document();
        var ul = doc.CreateElement("ul");
        doc.AppendChild(ul);
        var items = Enumerable.Range(0, 4)
            .Select(_ => { var li = doc.CreateElement("li"); ul.AppendChild(li); return li; })
            .ToArray();
        Matches("li:nth-child(even)", items[0]).Should().BeFalse();
        Matches("li:nth-child(even)", items[1]).Should().BeTrue();
        Matches("li:nth-child(even)", items[2]).Should().BeFalse();
        Matches("li:nth-child(even)", items[3]).Should().BeTrue();
    }

    // §15.9 :nth-child — An+B form "2n+1"
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-nth-child-pseudo", section: "15.9")]
    [SpecFact]
    public void Nth_child_2n_plus_1_matches_odd_children()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        doc.AppendChild(div);
        var items = Enumerable.Range(0, 4)
            .Select(_ => { var p = doc.CreateElement("p"); div.AppendChild(p); return p; })
            .ToArray();
        Matches("p:nth-child(2n+1)", items[0]).Should().BeTrue();  // 1
        Matches("p:nth-child(2n+1)", items[1]).Should().BeFalse(); // 2
        Matches("p:nth-child(2n+1)", items[2]).Should().BeTrue();  // 3
        Matches("p:nth-child(2n+1)", items[3]).Should().BeFalse(); // 4
    }

    // §15.9 :nth-child — exact integer "3"
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-nth-child-pseudo", section: "15.9")]
    [SpecFact]
    public void Nth_child_3_matches_only_third_child()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        doc.AppendChild(div);
        var items = Enumerable.Range(0, 4)
            .Select(_ => { var p = doc.CreateElement("p"); div.AppendChild(p); return p; })
            .ToArray();
        Matches("p:nth-child(3)", items[0]).Should().BeFalse();
        Matches("p:nth-child(3)", items[1]).Should().BeFalse();
        Matches("p:nth-child(3)", items[2]).Should().BeTrue();
        Matches("p:nth-child(3)", items[3]).Should().BeFalse();
    }

    // §15.9 :nth-child — "2n" (even alias)
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-nth-child-pseudo", section: "15.9")]
    [SpecFact]
    public void Nth_child_2n_matches_even_children()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        doc.AppendChild(div);
        var items = Enumerable.Range(0, 4)
            .Select(_ => { var p = doc.CreateElement("p"); div.AppendChild(p); return p; })
            .ToArray();
        Matches("p:nth-child(2n)", items[0]).Should().BeFalse(); // 1
        Matches("p:nth-child(2n)", items[1]).Should().BeTrue();  // 2
        Matches("p:nth-child(2n)", items[2]).Should().BeFalse(); // 3
        Matches("p:nth-child(2n)", items[3]).Should().BeTrue();  // 4
    }

    // §15.10 :nth-last-child
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-nth-last-child-pseudo", section: "15.10")]
    [SpecFact]
    public void Nth_last_child_1_matches_last_child()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        doc.AppendChild(div);
        var a = doc.CreateElement("p"); div.AppendChild(a);
        var b = doc.CreateElement("p"); div.AppendChild(b);
        var c = doc.CreateElement("p"); div.AppendChild(c);
        Matches("p:nth-last-child(1)", c).Should().BeTrue();
        Matches("p:nth-last-child(1)", b).Should().BeFalse();
        Matches("p:nth-last-child(1)", a).Should().BeFalse();
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-nth-last-child-pseudo", section: "15.10")]
    [SpecFact]
    public void Nth_last_child_odd_matches_from_end()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        doc.AppendChild(div);
        var items = Enumerable.Range(0, 4)
            .Select(_ => { var p = doc.CreateElement("p"); div.AppendChild(p); return p; })
            .ToArray();
        // From end: item[3]=1st-from-end (odd), item[2]=2nd-from-end (even), etc.
        Matches("p:nth-last-child(odd)", items[3]).Should().BeTrue();
        Matches("p:nth-last-child(odd)", items[2]).Should().BeFalse();
        Matches("p:nth-last-child(odd)", items[1]).Should().BeTrue();
        Matches("p:nth-last-child(odd)", items[0]).Should().BeFalse();
    }

    // §15.11 :nth-of-type
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-nth-of-type-pseudo", section: "15.11")]
    [SpecFact]
    public void Nth_of_type_matches_nth_of_same_tag_among_siblings()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        doc.AppendChild(div);
        var p1 = doc.CreateElement("p"); div.AppendChild(p1);
        var span = doc.CreateElement("span"); div.AppendChild(span);
        var p2 = doc.CreateElement("p"); div.AppendChild(p2);
        var p3 = doc.CreateElement("p"); div.AppendChild(p3);
        // p2 is the 2nd <p>
        Matches("p:nth-of-type(2)", p1).Should().BeFalse();
        Matches("p:nth-of-type(2)", p2).Should().BeTrue();
        Matches("p:nth-of-type(2)", p3).Should().BeFalse();
        // span is the 1st (and only) <span>
        Matches("span:nth-of-type(1)", span).Should().BeTrue();
    }

    // §15.12 :nth-last-of-type
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-nth-last-of-type-pseudo", section: "15.12")]
    [SpecFact]
    public void Nth_last_of_type_counts_from_end()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        doc.AppendChild(div);
        var p1 = doc.CreateElement("p"); div.AppendChild(p1);
        var p2 = doc.CreateElement("p"); div.AppendChild(p2);
        var p3 = doc.CreateElement("p"); div.AppendChild(p3);
        // p3 is 1st from end, p2 is 2nd, p1 is 3rd
        Matches("p:nth-last-of-type(1)", p3).Should().BeTrue();
        Matches("p:nth-last-of-type(1)", p2).Should().BeFalse();
        Matches("p:nth-last-of-type(2)", p2).Should().BeTrue();
        Matches("p:nth-last-of-type(3)", p1).Should().BeTrue();
    }

    // ========================================================================
    // §16 — Logical combinators
    // ========================================================================

    // §16.1 :is()
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#matches-pseudo", section: "16.1")]
    [SpecFact]
    public void Is_pseudo_matches_element_matching_any_argument_selector()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        var p = doc.CreateElement("p");
        doc.AppendChild(div);
        div.AppendChild(p);
        Matches(":is(p, span)", p).Should().BeTrue();
        Matches(":is(div, span)", p).Should().BeFalse();
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#matches-pseudo", section: "16.1")]
    [SpecFact]
    public void Is_pseudo_combined_with_type_selector()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        p.ClassList.Add("intro");
        doc.AppendChild(p);
        Matches("p:is(.intro)", p).Should().BeTrue();
        Matches("div:is(.intro)", p).Should().BeFalse();
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#matches-pseudo", section: "16.1")]
    [SpecFact]
    public void Is_pseudo_specificity_takes_most_specific_argument()
    {
        // :is(#id, .class) → specificity (1,0,0)
        var sel = SelectorParser.ParseSelectorList(":is(#hero, .card)").Selectors.Single();
        sel.Specificity.Should().Be(new Specificity(1, 0, 0));
    }

    // §16.2 :not()
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#negation-pseudo", section: "16.2")]
    [SpecFact]
    public void Not_pseudo_excludes_elements_matching_argument()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        var p = doc.CreateElement("p");
        p.ClassList.Add("hidden");
        doc.AppendChild(div);
        div.AppendChild(p);
        Matches("p:not(.hidden)", p).Should().BeFalse();
        p.ClassList.Remove("hidden");
        Matches("p:not(.hidden)", p).Should().BeTrue();
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#negation-pseudo", section: "16.2")]
    [SpecFact]
    public void Not_pseudo_with_type_selector_argument()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        var p = doc.CreateElement("p");
        doc.AppendChild(div);
        Matches("*:not(p)", div).Should().BeTrue();
        Matches("*:not(p)", p).Should().BeFalse();
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#negation-pseudo", section: "16.2")]
    [SpecFact]
    public void Not_pseudo_with_id_argument()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        div.SetAttribute("id", "main");
        doc.AppendChild(div);
        Matches("div:not(#sidebar)", div).Should().BeTrue();
        Matches("div:not(#main)", div).Should().BeFalse();
    }

    // §16.3 :where()
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#where-pseudo", section: "16.3")]
    [SpecFact]
    public void Where_pseudo_matches_same_as_is_but_with_zero_specificity()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        p.ClassList.Add("active");
        doc.AppendChild(p);
        Matches(":where(p, span)", p).Should().BeTrue();
        Matches(":where(div, span)", p).Should().BeFalse();
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#where-pseudo", section: "16.3")]
    [SpecFact]
    public void Where_pseudo_has_zero_specificity_even_with_id_argument()
    {
        var sel = SelectorParser.ParseSelectorList(":where(#hero)").Selectors.Single();
        sel.Specificity.Should().Be(Specificity.Zero);
    }

    // §16.4 :has()
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#has-pseudo", section: "16.4")]
    [SpecFact]
    public void Has_pseudo_matches_when_descendant_matches_argument()
    {
        var doc = new Document();
        var section = doc.CreateElement("section");
        var p = doc.CreateElement("p");
        doc.AppendChild(section);
        section.AppendChild(p);
        Matches("section:has(p)", section).Should().BeTrue();
        Matches("section:has(img)", section).Should().BeFalse();
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#has-pseudo", section: "16.4")]
    [SpecFact]
    public void Has_pseudo_child_combinator_matches_only_direct_child()
    {
        var doc = new Document();
        var section = doc.CreateElement("section");
        var a = doc.CreateElement("a");
        doc.AppendChild(section);
        section.AppendChild(a);
        Matches("section:has(> a)", section).Should().BeTrue();
        Matches("section:has(> img)", section).Should().BeFalse();
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#has-pseudo", section: "16.4")]
    [SpecFact]
    public void Has_pseudo_does_not_match_self()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        doc.AppendChild(div);
        // div has no children, so :has(div) should not match
        Matches("div:has(div)", div).Should().BeFalse();
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#has-pseudo", section: "16.4")]
    [SpecFact]
    public void Has_pseudo_with_next_sibling_combinator()
    {
        // section:has(+ p) matches section if a <p> immediately follows it
        var doc = new Document();
        var div = doc.CreateElement("div");
        var section = doc.CreateElement("section");
        var p = doc.CreateElement("p");
        doc.AppendChild(div);
        div.AppendChild(section);
        div.AppendChild(p);
        Matches("section:has(+ p)", section).Should().BeTrue();
        Matches("section:has(+ div)", section).Should().BeFalse();
    }

    // ========================================================================
    // §17 — Specificity
    // ========================================================================

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#specificity-rules", section: "17")]
    [SpecFact]
    public void Type_selector_has_specificity_0_0_1()
    {
        var sel = SelectorParser.ParseSelectorList("div").Selectors.Single();
        sel.Specificity.Should().Be(new Specificity(0, 0, 1));
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#specificity-rules", section: "17")]
    [SpecFact]
    public void Class_selector_has_specificity_0_1_0()
    {
        var sel = SelectorParser.ParseSelectorList(".foo").Selectors.Single();
        sel.Specificity.Should().Be(new Specificity(0, 1, 0));
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#specificity-rules", section: "17")]
    [SpecFact]
    public void Id_selector_has_specificity_1_0_0()
    {
        var sel = SelectorParser.ParseSelectorList("#foo").Selectors.Single();
        sel.Specificity.Should().Be(new Specificity(1, 0, 0));
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#specificity-rules", section: "17")]
    [SpecFact]
    public void Combined_selector_adds_specificities()
    {
        // div.foo#bar → (1,1,1)
        var sel = SelectorParser.ParseSelectorList("div.foo#bar").Selectors.Single();
        sel.Specificity.Should().Be(new Specificity(1, 1, 1));
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#specificity-rules", section: "17")]
    [SpecFact]
    public void Universal_selector_has_zero_specificity()
    {
        var sel = SelectorParser.ParseSelectorList("*").Selectors.Single();
        sel.Specificity.Should().Be(Specificity.Zero);
    }

    // ========================================================================
    // §17 — Pseudo-class interactive (non-structural)
    // ========================================================================

    // :hover
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-hover-pseudo", section: "6.1")]
    [SpecFact]
    public void Hover_pseudo_matches_hovered_element()
    {
        var doc = new Document();
        var btn = doc.CreateElement("button");
        doc.AppendChild(btn);
        var ctx = new SelectorMatchContext { HoveredElement = btn };
        Matches(":hover", btn, ctx).Should().BeTrue();
        Matches(":hover", btn).Should().BeFalse();
    }

    // :focus
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-focus-pseudo", section: "6.4")]
    [SpecFact]
    public void Focus_pseudo_matches_focused_element()
    {
        var doc = new Document();
        var input = doc.CreateElement("input");
        doc.AppendChild(input);
        var ctx = new SelectorMatchContext { FocusedElement = input };
        Matches(":focus", input, ctx).Should().BeTrue();
        Matches(":focus", input).Should().BeFalse();
    }

    // :focus-within
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-focus-within-pseudo", section: "6.4")]
    [SpecFact]
    public void Focus_within_pseudo_matches_ancestor_of_focused_element()
    {
        var doc = new Document();
        var form = doc.CreateElement("form");
        var input = doc.CreateElement("input");
        doc.AppendChild(form);
        form.AppendChild(input);
        var ctx = new SelectorMatchContext { FocusedElement = input };
        Matches(":focus-within", form, ctx).Should().BeTrue();
        Matches(":focus-within", input, ctx).Should().BeTrue();
    }

    // :active
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-active-pseudo", section: "6.2")]
    [SpecFact]
    public void Active_pseudo_matches_active_element()
    {
        var doc = new Document();
        var btn = doc.CreateElement("button");
        doc.AppendChild(btn);
        var ctx = new SelectorMatchContext { ActiveElement = btn };
        Matches(":active", btn, ctx).Should().BeTrue();
        Matches(":active", btn).Should().BeFalse();
    }

    // :target
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-target-pseudo", section: "6.5")]
    [SpecFact]
    public void Target_pseudo_matches_target_element()
    {
        var doc = new Document();
        var section = doc.CreateElement("section");
        doc.AppendChild(section);
        var ctx = new SelectorMatchContext { TargetElement = section };
        Matches(":target", section, ctx).Should().BeTrue();
        Matches(":target", section).Should().BeFalse();
    }

    // :checked
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-checked-pseudo", section: "6.6")]
    [SpecFact]
    public void Checked_pseudo_matches_element_with_checked_attribute()
    {
        var doc = new Document();
        var checkbox = doc.CreateElement("input");
        checkbox.SetAttribute("checked", "");
        doc.AppendChild(checkbox);
        Matches(":checked", checkbox).Should().BeTrue();
        var notChecked = doc.CreateElement("input");
        doc.AppendChild(notChecked);
        Matches(":checked", notChecked).Should().BeFalse();
    }

    // :disabled
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-disabled-pseudo", section: "6.6")]
    [SpecFact]
    public void Disabled_pseudo_matches_element_with_disabled_attribute()
    {
        var doc = new Document();
        var btn = doc.CreateElement("button");
        btn.SetAttribute("disabled", "");
        doc.AppendChild(btn);
        Matches(":disabled", btn).Should().BeTrue();
        var enabled = doc.CreateElement("button");
        doc.AppendChild(enabled);
        Matches(":disabled", enabled).Should().BeFalse();
    }

    // :enabled
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-enabled-pseudo", section: "6.6")]
    [SpecFact]
    public void Enabled_pseudo_matches_form_element_without_disabled()
    {
        var doc = new Document();
        var input = doc.CreateElement("input");
        doc.AppendChild(input);
        Matches(":enabled", input).Should().BeTrue();
        input.SetAttribute("disabled", "");
        Matches(":enabled", input).Should().BeFalse();
    }

    // :required
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-required-pseudo", section: "6.6")]
    [SpecFact]
    public void Required_pseudo_matches_input_with_required_attribute()
    {
        var doc = new Document();
        var input = doc.CreateElement("input");
        input.SetAttribute("required", "");
        doc.AppendChild(input);
        Matches(":required", input).Should().BeTrue();
    }

    // :optional
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-optional-pseudo", section: "6.6")]
    [SpecFact]
    public void Optional_pseudo_matches_input_without_required_attribute()
    {
        var doc = new Document();
        var input = doc.CreateElement("input");
        doc.AppendChild(input);
        Matches(":optional", input).Should().BeTrue();
    }

    // :read-only / :read-write
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-read-write-pseudo", section: "6.6")]
    [SpecFact]
    public void Read_only_pseudo_matches_readonly_input()
    {
        var doc = new Document();
        var input = doc.CreateElement("input");
        input.SetAttribute("readonly", "");
        doc.AppendChild(input);
        Matches(":read-only", input).Should().BeTrue();
        Matches(":read-write", input).Should().BeFalse();
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-read-write-pseudo", section: "6.6")]
    [SpecFact]
    public void Read_write_pseudo_matches_editable_input()
    {
        var doc = new Document();
        var input = doc.CreateElement("input");
        doc.AppendChild(input);
        Matches(":read-write", input).Should().BeTrue();
        Matches(":read-only", input).Should().BeFalse();
    }

    // :placeholder-shown
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-placeholder-shown-pseudo", section: "6.6")]
    [SpecFact]
    public void Placeholder_shown_pseudo_matches_empty_input_with_placeholder()
    {
        var doc = new Document();
        var input = doc.CreateElement("input");
        input.SetAttribute("placeholder", "Enter name");
        doc.AppendChild(input);
        Matches(":placeholder-shown", input).Should().BeTrue();
        input.SetAttribute("value", "text");
        Matches(":placeholder-shown", input).Should().BeFalse();
    }

    // :any-link
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#any-link-pseudo", section: "6.1")]
    [SpecFact]
    public void Any_link_pseudo_matches_anchor_with_href()
    {
        var doc = new Document();
        var a = doc.CreateElement("a");
        a.SetAttribute("href", "https://example.com");
        doc.AppendChild(a);
        Matches(":any-link", a).Should().BeTrue();
        var noHref = doc.CreateElement("a");
        doc.AppendChild(noHref);
        Matches(":any-link", noHref).Should().BeFalse();
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#any-link-pseudo", section: "6.1")]
    [SpecFact]
    public void Any_link_pseudo_matches_area_with_href()
    {
        var doc = new Document();
        var area = doc.CreateElement("area");
        area.SetAttribute("href", "/page");
        doc.AppendChild(area);
        Matches(":any-link", area).Should().BeTrue();
    }

    // :link (unvisited)
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#link-pseudo", section: "6.1")]
    [SpecFact]
    public void Link_pseudo_matches_unvisited_link()
    {
        var doc = new Document();
        var a = doc.CreateElement("a");
        a.SetAttribute("href", "https://example.com");
        doc.AppendChild(a);
        Matches(":link", a).Should().BeTrue();
    }

    // :visited privacy default
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#visited-pseudo", section: "6.1")]
    [SpecFact]
    public void Visited_pseudo_never_matches_by_default_for_privacy()
    {
        var doc = new Document();
        var a = doc.CreateElement("a");
        a.SetAttribute("href", "https://example.com");
        doc.AppendChild(a);
        Matches(":visited", a).Should().BeFalse();
    }

    // :defined
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-defined-pseudo", section: "6.4")]
    [SpecFact]
    public void Defined_pseudo_matches_all_built_in_elements()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        doc.AppendChild(div);
        Matches(":defined", div).Should().BeTrue();
    }

    // :lang
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-lang-pseudo", section: "6.2")]
    [SpecFact]
    public void Lang_pseudo_matches_element_with_matching_lang_attribute()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        div.SetAttribute("lang", "fr");
        doc.AppendChild(div);
        Matches(":lang(fr)", div).Should().BeTrue();
        Matches(":lang(en)", div).Should().BeFalse();
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-lang-pseudo", section: "6.2")]
    [SpecFact]
    public void Lang_pseudo_matches_subtag_prefix()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        div.SetAttribute("lang", "en-US");
        doc.AppendChild(div);
        Matches(":lang(en)", div).Should().BeTrue();
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-lang-pseudo", section: "6.2")]
    [SpecFact]
    public void Lang_pseudo_inherits_from_ancestor()
    {
        var doc = new Document();
        var root = doc.CreateElement("html");
        root.SetAttribute("lang", "de");
        var p = doc.CreateElement("p");
        doc.AppendChild(root);
        root.AppendChild(p);
        Matches(":lang(de)", p).Should().BeTrue();
    }

    // :dir
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-dir-pseudo", section: "6.3")]
    [SpecFact]
    public void Dir_pseudo_matches_element_with_matching_dir_attribute()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        div.SetAttribute("dir", "rtl");
        doc.AppendChild(div);
        Matches(":dir(rtl)", div).Should().BeTrue();
        Matches(":dir(ltr)", div).Should().BeFalse();
    }

    // :indeterminate
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-indeterminate-pseudo", section: "6.6")]
    [SpecFact]
    public void Indeterminate_pseudo_matches_element_with_indeterminate_attribute()
    {
        var doc = new Document();
        var checkbox = doc.CreateElement("input");
        checkbox.SetAttribute("indeterminate", "");
        doc.AppendChild(checkbox);
        Matches(":indeterminate", checkbox).Should().BeTrue();
        var normal = doc.CreateElement("input");
        doc.AppendChild(normal);
        Matches(":indeterminate", normal).Should().BeFalse();
    }

    // :default
    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-default-pseudo", section: "6.6")]
    [SpecFact]
    public void Default_pseudo_matches_element_with_checked_or_selected()
    {
        var doc = new Document();
        var opt = doc.CreateElement("option");
        opt.SetAttribute("selected", "");
        doc.AppendChild(opt);
        Matches(":default", opt).Should().BeTrue();
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-default-pseudo", section: "6.6")]
    [SpecFact]
    public void Default_pseudo_matches_submit_button()
    {
        var doc = new Document();
        var btn = doc.CreateElement("button");
        btn.SetAttribute("type", "submit");
        doc.AppendChild(btn);
        Matches(":default", btn).Should().BeTrue();
    }

    // ========================================================================
    // Edge cases / negative matching
    // ========================================================================

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#selector-list", section: "3")]
    [SpecFact]
    public void Selector_list_matches_element_matching_any_selector()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        var p = doc.CreateElement("p");
        doc.AppendChild(div);
        doc.AppendChild(p);
        Matches("div, p", div).Should().BeTrue();
        Matches("div, p", p).Should().BeTrue();
        var span = doc.CreateElement("span");
        doc.AppendChild(span);
        Matches("div, p", span).Should().BeFalse();
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#type-selectors", section: "4.1")]
    [SpecFact]
    public void Type_selector_does_not_match_wrong_element()
    {
        var doc = new Document();
        var span = doc.CreateElement("span");
        doc.AppendChild(span);
        Matches("div", span).Should().BeFalse();
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#descendant-combinators", section: "13.1")]
    [SpecFact]
    public void Descendant_combinator_does_not_match_sibling()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        var p = doc.CreateElement("p");
        var span = doc.CreateElement("span");
        doc.AppendChild(div);
        div.AppendChild(p);
        div.AppendChild(span);
        // span is sibling of p, not descendant
        Matches("p span", span).Should().BeFalse();
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#child-combinators", section: "13.2")]
    [SpecFact]
    public void Child_combinator_does_not_match_grandchild()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        var p = doc.CreateElement("p");
        var span = doc.CreateElement("span");
        doc.AppendChild(div);
        div.AppendChild(p);
        p.AppendChild(span);
        // span is grandchild — div > span should not match
        Matches("div > span", span).Should().BeFalse();
        Matches("div > p", p).Should().BeTrue();
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#adjacent-sibling-combinators", section: "13.3")]
    [SpecFact]
    public void Next_sibling_combinator_does_not_match_non_adjacent_sibling()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        var h1 = doc.CreateElement("h1");
        var p = doc.CreateElement("p");
        var em = doc.CreateElement("em");
        doc.AppendChild(div);
        div.AppendChild(h1);
        div.AppendChild(p);
        div.AppendChild(em);
        // em is not immediately after h1
        Matches("h1 + em", em).Should().BeFalse();
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-empty-pseudo", section: "15.2")]
    [SpecFact]
    public void Empty_pseudo_does_not_match_element_with_child_element()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        var span = doc.CreateElement("span");
        doc.AppendChild(div);
        div.AppendChild(span);
        Matches(":empty", div).Should().BeFalse();
    }

    // ========================================================================
    // :nth-child "of S" form (Selectors 4 extension)
    // ========================================================================

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-nth-child-pseudo", section: "15.9")]
    [SpecFact]
    public void Nth_child_of_S_counts_only_siblings_matching_selector()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        doc.AppendChild(div);
        var p1 = doc.CreateElement("p"); p1.ClassList.Add("highlighted"); div.AppendChild(p1);
        var p2 = doc.CreateElement("p"); div.AppendChild(p2);
        var p3 = doc.CreateElement("p"); p3.ClassList.Add("highlighted"); div.AppendChild(p3);
        // :nth-child(1 of .highlighted) matches p1 (1st among .highlighted)
        // :nth-child(2 of .highlighted) matches p3 (2nd among .highlighted)
        Matches(":nth-child(1 of .highlighted)", p1).Should().BeTrue();
        Matches(":nth-child(1 of .highlighted)", p3).Should().BeFalse();
        Matches(":nth-child(2 of .highlighted)", p3).Should().BeTrue();
        Matches(":nth-child(2 of .highlighted)", p1).Should().BeFalse();
        // p2 has no .highlighted, so it can't match :nth-child(n of .highlighted)
        Matches(":nth-child(1 of .highlighted)", p2).Should().BeFalse();
    }

    // ========================================================================
    // :scope
    // ========================================================================

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-scope-pseudo", section: "13.4")]
    [SpecFact]
    public void Scope_pseudo_matches_the_scope_element()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        var p = doc.CreateElement("p");
        doc.AppendChild(div);
        div.AppendChild(p);
        var ctx = new SelectorMatchContext { ScopeElement = div };
        Matches(":scope", div, ctx).Should().BeTrue();
        Matches(":scope", p, ctx).Should().BeFalse();
    }

    // ========================================================================
    // Chained / complex combinations
    // ========================================================================

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/", section: "3")]
    [SpecFact]
    public void Compound_type_plus_class_plus_attribute_all_required()
    {
        var doc = new Document();
        var input = doc.CreateElement("input");
        input.ClassList.Add("field");
        input.SetAttribute("type", "text");
        doc.AppendChild(input);
        Matches("input.field[type=text]", input).Should().BeTrue();
        Matches("input.field[type=password]", input).Should().BeFalse();
        Matches("button.field[type=text]", input).Should().BeFalse();
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/", section: "13")]
    [SpecFact]
    public void Deep_descendant_chain_resolves_correctly()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        var main = doc.CreateElement("main");
        var article = doc.CreateElement("article");
        var p = doc.CreateElement("p");
        doc.AppendChild(html);
        html.AppendChild(body);
        body.AppendChild(main);
        main.AppendChild(article);
        article.AppendChild(p);
        Matches("html body main article p", p).Should().BeTrue();
        Matches("html body article p", p).Should().BeTrue();
        Matches("html body main > p", p).Should().BeFalse();
        Matches("html body main > article > p", p).Should().BeTrue();
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/", section: "16")]
    [SpecFact]
    public void Is_and_not_combined_in_one_rule()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        var p = doc.CreateElement("p");
        p.ClassList.Add("note");
        doc.AppendChild(div);
        div.AppendChild(p);
        // :is(p,span):not(.hidden) — p.note has no .hidden, matches
        Matches(":is(p, span):not(.hidden)", p).Should().BeTrue();
        p.ClassList.Add("hidden");
        Matches(":is(p, span):not(.hidden)", p).Should().BeFalse();
    }

    // ========================================================================
    // Known gaps — PendingFact
    // ========================================================================

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#valid-invalid", section: "6.6")]
    [PendingFact(":valid / :invalid depend on constraint validation state not yet tracked", trackingWp: "wp:spec-selectors-4")]
    public void Valid_pseudo_matches_valid_form_element()
    {
        var doc = new Document();
        var input = doc.CreateElement("input");
        doc.AppendChild(input);
        Matches(":valid", input).Should().BeTrue();
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#valid-invalid", section: "6.6")]
    [PendingFact(":invalid depends on constraint validation state not yet tracked", trackingWp: "wp:spec-selectors-4")]
    public void Invalid_pseudo_matches_invalid_form_element()
    {
        var doc = new Document();
        var input = doc.CreateElement("input");
        input.SetAttribute("required", "");
        doc.AppendChild(input);
        Matches(":invalid", input).Should().BeTrue();
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#in-range-pseudo", section: "6.6")]
    [PendingFact(":in-range / :out-of-range depend on min/max validation not yet implemented", trackingWp: "wp:spec-selectors-4")]
    public void In_range_pseudo_matches_input_within_min_max()
    {
        var doc = new Document();
        var input = doc.CreateElement("input");
        input.SetAttribute("type", "number");
        input.SetAttribute("min", "1");
        input.SetAttribute("max", "10");
        input.SetAttribute("value", "5");
        doc.AppendChild(input);
        Matches(":in-range", input).Should().BeTrue();
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#autofill", section: "6.6")]
    [PendingFact(":autofill matches browser-autofilled inputs — autofill state not yet modeled", trackingWp: "wp:spec-selectors-4")]
    public void Autofill_pseudo_matches_autofilled_input()
    {
        var doc = new Document();
        var input = doc.CreateElement("input");
        doc.AppendChild(input);
        Matches(":autofill", input).Should().BeTrue();
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-user-valid-pseudo", section: "6.6")]
    [PendingFact(":user-valid / :user-invalid require interaction tracking not yet implemented", trackingWp: "wp:spec-selectors-4")]
    public void User_valid_pseudo_matches_after_user_interaction()
    {
        var doc = new Document();
        var input = doc.CreateElement("input");
        doc.AppendChild(input);
        Matches(":user-valid", input).Should().BeTrue();
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-lang-pseudo", section: "6.2")]
    [PendingFact(":lang() with comma-separated list (e.g. :lang(en,fr)) is a Selectors 4 extension not yet parsed", trackingWp: "wp:spec-selectors-4")]
    public void Lang_pseudo_with_comma_list_matches_any_listed_language()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        div.SetAttribute("lang", "fr");
        doc.AppendChild(div);
        // Selectors 4 §6.2: :lang(en, fr) should match either
        Matches(":lang(en, fr)", div).Should().BeTrue();
    }

    [Spec("selectors-4", "https://www.w3.org/TR/selectors-4/#the-focus-visible-pseudo", section: "6.4")]
    [PendingFact(":focus-visible requires heuristic keyboard-focus detection not yet modeled beyond focus equality", trackingWp: "wp:spec-selectors-4")]
    public void Focus_visible_matches_keyboard_focused_element()
    {
        // Selectors 4 §6.4: :focus-visible must use heuristics (keyboard vs pointer)
        // The current implementation simply equates :focus-visible with :focus.
        var doc = new Document();
        var btn = doc.CreateElement("button");
        doc.AppendChild(btn);
        var pointerCtx = new SelectorMatchContext { FocusedElement = btn };
        // A pointer-focused element should NOT match :focus-visible per spec, but current impl returns true.
        Matches(":focus-visible", btn, pointerCtx).Should().BeFalse();
    }
}
