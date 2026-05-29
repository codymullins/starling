using AwesomeAssertions;
using Starling.Css.Cascade;
using Starling.Css.Media;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Selectors;
using Starling.Dom;
using Color = global::Starling.Css.Values.CssColor;

namespace Starling.Css.Spec.Tests.CssNesting1;

/// <summary>
/// Conformance suite for <see href="https://www.w3.org/TR/css-nesting-1/">CSS Nesting 1</see>.
/// Tests assert what the existing implementation actually produces.
/// </summary>
[TestClass]
[Spec("css-nesting-1", "https://www.w3.org/TR/css-nesting-1/")]
public sealed class NestingTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static (Document doc, Element parent, Element child) BuildParentChild(
        string parentClass, string parentTag,
        string childClass, string childTag)
    {
        var doc = new Document();
        var parent = doc.CreateElement(parentTag);
        parent.ClassList.Add(parentClass);
        var child = doc.CreateElement(childTag);
        child.ClassList.Add(childClass);
        doc.AppendChild(parent);
        parent.AppendChild(child);
        return (doc, parent, child);
    }

    private static StyleEngine NoUa(string css)
    {
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet(css));
        return engine;
    }

    // ---------------------------------------------------------------------------
    // § 2 – The Nesting Selector (&)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Spec §2: <c>&amp;</c> as a descendant combinator — <c>.a { &amp; .b { } }</c>
    /// resolves to <c>.a .b</c> and applies to a descendant matching .b.
    /// </summary>
    [Spec("css-nesting-1", "https://www.w3.org/TR/css-nesting-1/#nest-selector", section: "2")]
    [SpecFact]
    public void Ampersand_descendant_applies_to_matching_child()
    {
        var (_, _, child) = BuildParentChild("a", "div", "b", "span");
        var engine = NoUa(".a { & .b { color: red; } }");
        engine.Compute(child).GetColor(PropertyId.Color).Should().Be(new Color(255, 0, 0));
    }

    /// <summary>
    /// Spec §2: <c>&amp;</c> in compound selector — <c>.a { &amp;.b { } }</c>
    /// resolves to <c>:is(.a).b</c> and matches an element with BOTH classes.
    /// </summary>
    [Spec("css-nesting-1", "https://www.w3.org/TR/css-nesting-1/#nest-selector", section: "2")]
    [SpecFact]
    public void Ampersand_in_compound_matches_element_with_both_classes()
    {
        var doc = new Document();
        var el = doc.CreateElement("div");
        el.ClassList.Add("a");
        el.ClassList.Add("b");
        doc.AppendChild(el);

        var engine = NoUa(".a { &.b { color: red; } }");
        engine.Compute(el).GetColor(PropertyId.Color).Should().Be(new Color(255, 0, 0));
    }

    /// <summary>
    /// Spec §2: <c>&amp;</c> in compound — element that only has class .a (not .b)
    /// must NOT be matched by <c>.a { &amp;.b {} }</c>.
    /// </summary>
    [Spec("css-nesting-1", "https://www.w3.org/TR/css-nesting-1/#nest-selector", section: "2")]
    [SpecFact]
    public void Ampersand_in_compound_does_not_match_parent_alone()
    {
        var doc = new Document();
        var el = doc.CreateElement("div");
        el.ClassList.Add("a");
        doc.AppendChild(el);

        var engine = NoUa(".a { &.b { color: red; } }");
        // el only has .a — the compound &.b needs both .a and .b.
        engine.Compute(el).GetColor(PropertyId.Color).Should().NotBe(new Color(255, 0, 0));
    }

    /// <summary>
    /// Spec §2: <c>&amp;</c> with pseudo-class — <c>a { &amp;:hover { color: red; } }</c>
    /// must apply only when the element is hovered.
    /// </summary>
    [Spec("css-nesting-1", "https://www.w3.org/TR/css-nesting-1/#nest-selector", section: "2")]
    [SpecFact]
    public void Ampersand_pseudo_applies_only_when_hovered()
    {
        var doc = new Document();
        var a = doc.CreateElement("a");
        doc.AppendChild(a);

        var engine = NoUa("a { color: blue; &:hover { color: red; } }");

        engine.Compute(a).GetColor(PropertyId.Color).Should().Be(new Color(0, 0, 255));
        engine.Compute(a, new SelectorMatchContext { HoveredElement = a })
              .GetColor(PropertyId.Color).Should().Be(new Color(255, 0, 0));
    }

    /// <summary>
    /// Spec §2: When the parent has a complex selector (.a .b), <c>&amp;</c> represents
    /// that full selector via <c>:is()</c>. A nested <c>&amp; .c</c> should match .c
    /// that is a descendant of .b which is a descendant of .a.
    /// </summary>
    [Spec("css-nesting-1", "https://www.w3.org/TR/css-nesting-1/#nest-selector", section: "2")]
    [SpecFact]
    public void Ampersand_in_is_semantics_for_complex_parent()
    {
        var doc = new Document();
        var a = doc.CreateElement("div");
        a.ClassList.Add("a");
        var b = doc.CreateElement("div");
        b.ClassList.Add("b");
        var c = doc.CreateElement("span");
        c.ClassList.Add("c");
        doc.AppendChild(a);
        a.AppendChild(b);
        b.AppendChild(c);

        var engine = NoUa(".a .b { & .c { color: red; } }");
        engine.Compute(c).GetColor(PropertyId.Color).Should().Be(new Color(255, 0, 0));
    }

    /// <summary>
    /// Spec §2: <c>&amp;</c> with multiple parent selectors — <c>.x, .y { &amp;:hover {} }</c>
    /// The nested rule must apply to both elements when hovered.
    /// </summary>
    [Spec("css-nesting-1", "https://www.w3.org/TR/css-nesting-1/#nest-selector", section: "2")]
    [SpecFact]
    public void Ampersand_with_comma_separated_parent_selectors()
    {
        var doc = new Document();
        var x = doc.CreateElement("div");
        x.ClassList.Add("x");
        var y = doc.CreateElement("div");
        y.ClassList.Add("y");
        doc.AppendChild(x);
        doc.AppendChild(y);

        var engine = NoUa(".x, .y { &:hover { color: red; } }");

        engine.Compute(x, new SelectorMatchContext { HoveredElement = x })
              .GetColor(PropertyId.Color).Should().Be(new Color(255, 0, 0));
        engine.Compute(y, new SelectorMatchContext { HoveredElement = y })
              .GetColor(PropertyId.Color).Should().Be(new Color(255, 0, 0));
    }

    // ---------------------------------------------------------------------------
    // § 3 – Direct Nesting (Implicit &)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Spec §3: Implicit nesting — a nested class selector without explicit <c>&amp;</c>
    /// is treated as <c>&amp; .child</c> (descendant). <c>.a { .b {} }</c> → <c>.a .b</c>.
    /// </summary>
    [Spec("css-nesting-1", "https://www.w3.org/TR/css-nesting-1/#direct", section: "3")]
    [SpecFact]
    public void Implicit_nesting_class_selector_as_descendant()
    {
        var (_, _, child) = BuildParentChild("a", "div", "b", "span");
        var engine = NoUa(".a { .b { color: red; } }");
        engine.Compute(child).GetColor(PropertyId.Color).Should().Be(new Color(255, 0, 0));
    }

    /// <summary>
    /// Spec §3: Implicit nesting with explicit child combinator —
    /// <c>.a { &gt; .b {} }</c> resolves to <c>.a &gt; .b</c>.
    /// The direct child .b gets the background-color (non-inherited); a .b
    /// nested inside that child does NOT — selector does not reach it.
    /// </summary>
    [Spec("css-nesting-1", "https://www.w3.org/TR/css-nesting-1/#direct", section: "3")]
    [SpecFact]
    public void Implicit_nesting_child_combinator_matches_direct_only()
    {
        var doc = new Document();
        var parent = doc.CreateElement("div");
        parent.ClassList.Add("a");
        var direct = doc.CreateElement("span");
        direct.ClassList.Add("b");
        var grandchild = doc.CreateElement("em");
        grandchild.ClassList.Add("b");
        doc.AppendChild(parent);
        parent.AppendChild(direct);
        direct.AppendChild(grandchild);

        // Use background-color (non-inherited) so inheritance cannot mask the result.
        var engine = NoUa(".a { > .b { background-color: red; } }");

        // Direct child matches .a > .b.
        engine.Compute(direct).GetColor(PropertyId.BackgroundColor).Should().Be(new Color(255, 0, 0));
        // Grandchild is NOT a direct child of .a, so the selector does not match it.
        // background-color is non-inherited, so it stays transparent.
        engine.Compute(grandchild).GetColor(PropertyId.BackgroundColor).Should().Be(Color.Transparent);
    }

    /// <summary>
    /// Spec §3: Implicit nesting with adjacent sibling combinator —
    /// <c>.a { .b + .c {} }</c> should match .c immediately following .b inside .a.
    /// </summary>
    [Spec("css-nesting-1", "https://www.w3.org/TR/css-nesting-1/#direct", section: "3")]
    [SpecFact]
    public void Implicit_nesting_adjacent_sibling_combinator()
    {
        var doc = new Document();
        var parent = doc.CreateElement("div");
        parent.ClassList.Add("a");
        var sib1 = doc.CreateElement("span");
        sib1.ClassList.Add("b");
        var sib2 = doc.CreateElement("span");
        sib2.ClassList.Add("c");
        doc.AppendChild(parent);
        parent.AppendChild(sib1);
        parent.AppendChild(sib2);

        var engine = NoUa(".a { .b + .c { color: red; } }");
        engine.Compute(sib2).GetColor(PropertyId.Color).Should().Be(new Color(255, 0, 0));
    }

    /// <summary>
    /// Spec §3: Implicit nesting with subsequent sibling combinator —
    /// <c>.a { .b ~ .c {} }</c> should match .c anywhere after .b inside .a.
    /// </summary>
    [Spec("css-nesting-1", "https://www.w3.org/TR/css-nesting-1/#direct", section: "3")]
    [SpecFact]
    public void Implicit_nesting_subsequent_sibling_combinator()
    {
        var doc = new Document();
        var parent = doc.CreateElement("div");
        parent.ClassList.Add("a");
        var sib1 = doc.CreateElement("span");
        sib1.ClassList.Add("b");
        var middle = doc.CreateElement("i");
        var sib2 = doc.CreateElement("span");
        sib2.ClassList.Add("c");
        doc.AppendChild(parent);
        parent.AppendChild(sib1);
        parent.AppendChild(middle);
        parent.AppendChild(sib2);

        var engine = NoUa(".a { .b ~ .c { color: red; } }");
        engine.Compute(sib2).GetColor(PropertyId.Color).Should().Be(new Color(255, 0, 0));
    }

    // ---------------------------------------------------------------------------
    // § 4 – Nesting with @media / @supports (Conditional nesting)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Spec §4: <c>@media</c> nested inside a style rule applies the declared
    /// property to the parent-matched element only when the query is true.
    /// </summary>
    [Spec("css-nesting-1", "https://www.w3.org/TR/css-nesting-1/#conditionals", section: "4")]
    [SpecFact]
    public void Nested_at_media_applies_when_query_matches()
    {
        var doc = new Document();
        var el = doc.CreateElement("p");
        el.ClassList.Add("box");
        doc.AppendChild(el);

        var engine = NoUa(".box { color: black; @media (min-width: 600px) { color: red; } }");

        engine.MediaContext = new MediaContext(ViewportWidthPx: 400);
        engine.Compute(el).GetColor(PropertyId.Color).Should().Be(Color.Black);

        engine.MediaContext = new MediaContext(ViewportWidthPx: 800);
        engine.Compute(el).GetColor(PropertyId.Color).Should().Be(new Color(255, 0, 0));
    }

    /// <summary>
    /// Spec §4: <c>@media screen</c> nested inside a style rule — screen media type
    /// always evaluates true in the default context, so the nested declaration applies.
    /// </summary>
    [Spec("css-nesting-1", "https://www.w3.org/TR/css-nesting-1/#conditionals", section: "4")]
    [SpecFact]
    public void Nested_at_media_screen_applies_to_matching_element()
    {
        var doc = new Document();
        var el = doc.CreateElement("div");
        el.ClassList.Add("a");
        doc.AppendChild(el);

        var engine = NoUa(".a { @media screen { color: red; } }");
        engine.Compute(el).GetColor(PropertyId.Color).Should().Be(new Color(255, 0, 0));
    }

    /// <summary>
    /// Spec §4: <c>@supports</c> nested inside a style rule applies declarations
    /// only when the feature query evaluates to true.
    /// </summary>
    [Spec("css-nesting-1", "https://www.w3.org/TR/css-nesting-1/#conditionals", section: "4")]
    [SpecFact]
    public void Nested_at_supports_applies_when_feature_supported()
    {
        var doc = new Document();
        var el = doc.CreateElement("div");
        el.ClassList.Add("a");
        doc.AppendChild(el);

        var engine = NoUa(".a { @supports (color: red) { color: red; } }");
        engine.Compute(el).GetColor(PropertyId.Color).Should().Be(new Color(255, 0, 0));
    }

    /// <summary>
    /// Spec §4: <c>@supports (unknown-prop: value)</c> nested inside a style rule
    /// must NOT apply declarations when the feature is unsupported.
    /// </summary>
    [Spec("css-nesting-1", "https://www.w3.org/TR/css-nesting-1/#conditionals", section: "4")]
    [SpecFact]
    public void Nested_at_supports_does_not_apply_when_unsupported()
    {
        var doc = new Document();
        var el = doc.CreateElement("div");
        el.ClassList.Add("a");
        doc.AppendChild(el);

        // color is the known default; the @supports guard is false.
        var engine = NoUa(".a { color: black; @supports (unknown-nesting-prop: 1) { color: red; } }");
        engine.Compute(el).GetColor(PropertyId.Color).Should().Be(Color.Black);
    }

    /// <summary>
    /// Spec §4: <c>@media</c> nested inside a style rule can itself contain nested
    /// child style rules that use <c>&amp;</c>.
    /// </summary>
    [Spec("css-nesting-1", "https://www.w3.org/TR/css-nesting-1/#conditionals", section: "4")]
    [SpecFact]
    public void Nested_at_media_containing_child_nesting_rule()
    {
        var doc = new Document();
        var parent = doc.CreateElement("div");
        parent.ClassList.Add("grid");
        var child = doc.CreateElement("span");
        child.ClassList.Add("item");
        doc.AppendChild(parent);
        parent.AppendChild(child);

        var engine = NoUa(".grid { @media (min-width: 600px) { & > .item { color: red; } } }");
        engine.MediaContext = new MediaContext(ViewportWidthPx: 800);
        engine.Compute(child).GetColor(PropertyId.Color).Should().Be(new Color(255, 0, 0));
    }

    // ---------------------------------------------------------------------------
    // § 5 – Multiple nesting levels
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Spec §3/§2: Three levels of nesting — <c>.a { .b { .c {} } }</c>
    /// resolves to <c>.a .b .c</c> and applies only to the deepest element.
    /// </summary>
    [Spec("css-nesting-1", "https://www.w3.org/TR/css-nesting-1/#direct", section: "3")]
    [SpecFact]
    public void Three_levels_of_nesting_resolves_to_three_part_selector()
    {
        var doc = new Document();
        var a = doc.CreateElement("div");
        a.ClassList.Add("a");
        var b = doc.CreateElement("div");
        b.ClassList.Add("b");
        var c = doc.CreateElement("span");
        c.ClassList.Add("c");
        doc.AppendChild(a);
        a.AppendChild(b);
        b.AppendChild(c);

        var engine = NoUa(".a { .b { .c { color: red; } } }");
        engine.Compute(c).GetColor(PropertyId.Color).Should().Be(new Color(255, 0, 0));
    }

    /// <summary>
    /// Spec §3/§2: Three levels of nesting — intermediate element (.b) must not
    /// receive the deepest rule's declarations.
    /// </summary>
    [Spec("css-nesting-1", "https://www.w3.org/TR/css-nesting-1/#direct", section: "3")]
    [SpecFact]
    public void Three_levels_of_nesting_does_not_apply_to_intermediate_element()
    {
        var doc = new Document();
        var a = doc.CreateElement("div");
        a.ClassList.Add("a");
        var b = doc.CreateElement("div");
        b.ClassList.Add("b");
        var c = doc.CreateElement("span");
        c.ClassList.Add("c");
        doc.AppendChild(a);
        a.AppendChild(b);
        b.AppendChild(c);

        var engine = NoUa(".a { .b { .c { color: red; } } }");
        engine.Compute(b).GetColor(PropertyId.Color).Should().NotBe(new Color(255, 0, 0));
    }

    /// <summary>
    /// Spec §2: Mixed explicit and implicit nesting across three levels —
    /// <c>.a { &amp; .b { &amp;.b .c { color: red; } } }</c>
    /// resolves the innermost to match .c inside .b inside .a.
    /// </summary>
    [Spec("css-nesting-1", "https://www.w3.org/TR/css-nesting-1/#nest-selector", section: "2")]
    [SpecFact]
    public void Mixed_explicit_implicit_nesting_three_levels()
    {
        var doc = new Document();
        var a = doc.CreateElement("div");
        a.ClassList.Add("a");
        var b = doc.CreateElement("div");
        b.ClassList.Add("b");
        var c = doc.CreateElement("span");
        c.ClassList.Add("c");
        doc.AppendChild(a);
        a.AppendChild(b);
        b.AppendChild(c);

        var engine = NoUa(".a { & .b { & .c { color: red; } } }");
        engine.Compute(c).GetColor(PropertyId.Color).Should().Be(new Color(255, 0, 0));
    }

    // ---------------------------------------------------------------------------
    // § 6 – Declarations after nested rules
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Spec §3: Parent-level declarations before a nested rule still apply
    /// to the parent-matched element.
    /// </summary>
    [Spec("css-nesting-1", "https://www.w3.org/TR/css-nesting-1/#direct", section: "3")]
    [SpecFact]
    public void Parent_declaration_before_nested_rule_applies_to_parent()
    {
        var doc = new Document();
        var parent = doc.CreateElement("div");
        parent.ClassList.Add("a");
        var child = doc.CreateElement("span");
        child.ClassList.Add("b");
        doc.AppendChild(parent);
        parent.AppendChild(child);

        var engine = NoUa(".a { color: red; .b { color: blue; } }");
        engine.Compute(parent).GetColor(PropertyId.Color).Should().Be(new Color(255, 0, 0));
        engine.Compute(child).GetColor(PropertyId.Color).Should().Be(new Color(0, 0, 255));
    }

    /// <summary>
    /// Spec §3: A declaration appearing after a nested rule in the same
    /// style block still applies to the parent-matched element (order of
    /// appearance does not suppress the declaration).
    /// </summary>
    [Spec("css-nesting-1", "https://www.w3.org/TR/css-nesting-1/#direct", section: "3")]
    [SpecFact]
    public void Parent_declaration_after_nested_rule_applies_to_parent()
    {
        var doc = new Document();
        var parent = doc.CreateElement("div");
        parent.ClassList.Add("a");
        var child = doc.CreateElement("span");
        child.ClassList.Add("b");
        doc.AppendChild(parent);
        parent.AppendChild(child);

        // Per CSS Nesting 1: declarations in a block are processed for the
        // parent regardless of their position relative to nested rules.
        var engine = NoUa(".a { .b { color: blue; } color: red; }");
        engine.Compute(parent).GetColor(PropertyId.Color).Should().Be(new Color(255, 0, 0));
    }

    // ---------------------------------------------------------------------------
    // § 7 – Specificity
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Spec §2: Specificity of a nested rule's selector is computed normally.
    /// A class selector has specificity (0,1,0). An outer rule with lower
    /// specificity must be overridden by a nested rule with higher specificity.
    /// </summary>
    [Spec("css-nesting-1", "https://www.w3.org/TR/css-nesting-1/#nest-selector", section: "2")]
    [SpecFact]
    public void Nested_rule_with_higher_specificity_wins()
    {
        var doc = new Document();
        var parent = doc.CreateElement("div");
        parent.ClassList.Add("a");
        var child = doc.CreateElement("span");
        child.ClassList.Add("b");
        doc.AppendChild(parent);
        parent.AppendChild(child);

        // outer: color red for any span (0,0,1)
        // nested: color blue for .a .b (0,2,0) — higher specificity, blue wins
        var engine = NoUa("span { color: red; } .a { .b { color: blue; } }");
        engine.Compute(child).GetColor(PropertyId.Color).Should().Be(new Color(0, 0, 255));
    }

    // ---------------------------------------------------------------------------
    // § 8 – Edge cases
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Spec §2: A nested rule that does not match (selector mismatch) must not
    /// apply its declarations to the parent element.
    /// </summary>
    [Spec("css-nesting-1", "https://www.w3.org/TR/css-nesting-1/#nest-selector", section: "2")]
    [SpecFact]
    public void Nested_rule_not_matching_does_not_apply()
    {
        var doc = new Document();
        var el = doc.CreateElement("div");
        el.ClassList.Add("a");
        doc.AppendChild(el);

        var engine = NoUa(".a { .b { color: red; } }");
        // .b nested rule should not bleed onto .a
        engine.Compute(el).GetColor(PropertyId.Color).Should().NotBe(new Color(255, 0, 0));
    }

    /// <summary>
    /// Spec §2: <c>&amp;</c> at the start of a nested comma list —
    /// <c>.a { &amp; .b, &amp; .c { color: red; } }</c> must match both .b and .c.
    /// </summary>
    [Spec("css-nesting-1", "https://www.w3.org/TR/css-nesting-1/#nest-selector", section: "2")]
    [SpecFact]
    public void Ampersand_comma_list_matches_both_selectors()
    {
        var doc = new Document();
        var parent = doc.CreateElement("div");
        parent.ClassList.Add("a");
        var b = doc.CreateElement("span");
        b.ClassList.Add("b");
        var c = doc.CreateElement("em");
        c.ClassList.Add("c");
        doc.AppendChild(parent);
        parent.AppendChild(b);
        parent.AppendChild(c);

        var engine = NoUa(".a { & .b, & .c { color: red; } }");
        engine.Compute(b).GetColor(PropertyId.Color).Should().Be(new Color(255, 0, 0));
        engine.Compute(c).GetColor(PropertyId.Color).Should().Be(new Color(255, 0, 0));
    }

    /// <summary>
    /// Spec §2: Id selector parent with <c>&amp;</c> nested class —
    /// <c>#foo { &amp; .bar {} }</c> resolves to <c>#foo .bar</c>.
    /// </summary>
    [Spec("css-nesting-1", "https://www.w3.org/TR/css-nesting-1/#nest-selector", section: "2")]
    [SpecFact]
    public void Ampersand_with_id_parent_selector()
    {
        var doc = new Document();
        var parent = doc.CreateElement("div");
        parent.SetAttribute("id", "foo");
        var child = doc.CreateElement("span");
        child.ClassList.Add("bar");
        doc.AppendChild(parent);
        parent.AppendChild(child);

        var engine = NoUa("#foo { & .bar { color: red; } }");
        engine.Compute(child).GetColor(PropertyId.Color).Should().Be(new Color(255, 0, 0));
    }

    /// <summary>
    /// Spec §2: Type selector parent with <c>&amp;</c> — <c>div { &amp; span {} }</c>
    /// resolves to <c>div span</c> and matches span descendants of any div.
    /// </summary>
    [Spec("css-nesting-1", "https://www.w3.org/TR/css-nesting-1/#nest-selector", section: "2")]
    [SpecFact]
    public void Ampersand_with_type_parent_selector()
    {
        var doc = new Document();
        var parent = doc.CreateElement("div");
        var child = doc.CreateElement("span");
        doc.AppendChild(parent);
        parent.AppendChild(child);

        var engine = NoUa("div { & span { color: red; } }");
        engine.Compute(child).GetColor(PropertyId.Color).Should().Be(new Color(255, 0, 0));
    }

    // ---------------------------------------------------------------------------
    // § 9 – Interaction with cascade layers (@layer)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Spec §4 + CSS Cascade: <c>@layer</c> at top level wrapping nested rules.
    /// Declarations inside the nested rule inside the layer must be applied at
    /// the layer's cascade position — lower than unlayered styles.
    /// </summary>
    [Spec("css-nesting-1", "https://www.w3.org/TR/css-nesting-1/#conditionals", section: "4")]
    [SpecFact]
    public void Layer_wrapping_nested_rule_unlayered_wins()
    {
        var doc = new Document();
        var parent = doc.CreateElement("div");
        parent.ClassList.Add("a");
        var child = doc.CreateElement("span");
        child.ClassList.Add("b");
        doc.AppendChild(parent);
        parent.AppendChild(child);

        // Layered rule is overridden by an unlayered rule of equal specificity.
        var engine = NoUa("""
            @layer base { .a { .b { color: blue; } } }
            .a .b { color: red; }
            """);
        engine.Compute(child).GetColor(PropertyId.Color).Should().Be(new Color(255, 0, 0));
    }

    /// <summary>
    /// End-to-end: layered + @supports + @media + CSS Nesting all combined.
    /// </summary>
    [Spec("css-nesting-1", "https://www.w3.org/TR/css-nesting-1/#conditionals", section: "4")]
    [SpecFact]
    public void End_to_end_layer_supports_media_nesting()
    {
        var doc = new Document();
        var grid = doc.CreateElement("div");
        grid.ClassList.Add("grid");
        var item = doc.CreateElement("span");
        item.ClassList.Add("item");
        doc.AppendChild(grid);
        grid.AppendChild(item);

        var engine = NoUa("""
            @layer reset, components;
            @layer reset { p { margin-top: 0; } }
            @media (min-width: 600px) {
              @supports (color: red) {
                .grid { color: green; & > .item { color: red; } }
              }
            }
            """);
        engine.MediaContext = new MediaContext(ViewportWidthPx: 800);
        engine.Compute(item).GetColor(PropertyId.Color).Should().Be(new Color(255, 0, 0));
    }
}
