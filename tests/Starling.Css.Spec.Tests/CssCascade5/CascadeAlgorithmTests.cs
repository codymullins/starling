using AwesomeAssertions;
using Starling.Css.Cascade;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Dom;

namespace Starling.Css.Spec.Tests.CssCascade5;

/// <summary>
/// Behavioral cascade-algorithm conformance for
/// <see href="https://www.w3.org/TR/css-cascade-5/">CSS Cascading and Inheritance Level 5</see>.
/// Covers §6 (specificity + origin), §7 (@layer), §3 (CSS-wide keywords), §2.2 (all),
/// and inheritance semantics via <c>StyleEngine.Compute</c>.
/// </summary>
[TestClass]
[Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/")]
public sealed class CascadeAlgorithmTests
{
    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static (Document doc, Element el) MakeElement(string tag = "p")
    {
        var doc = new Document();
        var el = doc.CreateElement(tag);
        doc.AppendChild(el);
        return (doc, el);
    }

    private static (Document doc, Element parent, Element child) MakeParentChild(
        string parentTag = "div", string childTag = "p")
    {
        var doc = new Document();
        var parent = doc.CreateElement(parentTag);
        var child = doc.CreateElement(childTag);
        doc.AppendChild(parent);
        parent.AppendChild(child);
        return (doc, parent, child);
    }

    private static StyleEngine NoUa() => new StyleEngine(includeUserAgentStyleSheet: false);

    // ------------------------------------------------------------------
    // §6.1 Specificity
    // ------------------------------------------------------------------

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#cascade-specificity", section: "6.1")]
    [SpecFact]
    public void Id_beats_class_beats_type_selector()
    {
        var (_, el) = MakeElement("p");
        el.SetAttribute("id", "main");
        el.SetAttribute("class", "box");

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            p      { color: red;   }
            .box   { color: green; }
            #main  { color: blue;  }
            """));

        var style = engine.Compute(el);
        style.GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 0, 255), "id (#main) has highest specificity");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#cascade-specificity", section: "6.1")]
    [SpecFact]
    public void Class_beats_type_selector()
    {
        var (_, el) = MakeElement("p");
        el.SetAttribute("class", "box");

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            p    { color: red;   }
            .box { color: green; }
            """));

        var style = engine.Compute(el);
        style.GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 128, 0), "class (.box) beats type (p)");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#cascade-specificity", section: "6.1")]
    [SpecFact]
    public void Later_rule_wins_on_equal_specificity()
    {
        var (_, el) = MakeElement("p");

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            p { color: red;  }
            p { color: blue; }
            """));

        var style = engine.Compute(el);
        style.GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 0, 255), "later rule wins when specificity is equal");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#cascade-specificity", section: "6.1")]
    [SpecFact]
    public void Two_classes_beat_one_class()
    {
        var (_, el) = MakeElement("p");
        el.SetAttribute("class", "a b");

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            .a    { color: red;   }
            .a.b  { color: green; }
            """));

        var style = engine.Compute(el);
        style.GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 128, 0), ".a.b (0,2,0) beats .a (0,1,0)");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#cascade-specificity", section: "6.1")]
    [SpecFact]
    public void Inline_style_beats_author_sheet()
    {
        var (_, el) = MakeElement("p");
        el.SetAttribute("style", "color: green");

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            p { color: red; }
            """));

        var style = engine.Compute(el);
        style.GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 128, 0), "inline style beats any author rule");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#cascade-specificity", section: "6.1")]
    [SpecFact]
    public void Id_plus_class_has_higher_specificity_than_id_alone()
    {
        var (_, el) = MakeElement("div");
        el.SetAttribute("id", "foo");
        el.SetAttribute("class", "bar");

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            #foo      { color: red;   }
            #foo.bar  { color: blue;  }
            """));

        var style = engine.Compute(el);
        style.GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 0, 255), "#foo.bar (1,1,0) > #foo (1,0,0)");
    }

    // ------------------------------------------------------------------
    // §6.3 Origin + importance order
    // ------------------------------------------------------------------

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#cascade-origin", section: "6.3")]
    [SpecFact]
    public void Author_beats_user_agent()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        var p = doc.CreateElement("p");
        doc.AppendChild(html);
        html.AppendChild(body);
        body.AppendChild(p);

        // UA sheet sets display:block for p; author overrides color.
        var engine = new StyleEngine(includeUserAgentStyleSheet: true);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            p { color: green; }
            """, StyleOrigin.Author));

        var style = engine.Compute(p);
        style.GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 128, 0), "Author > UA for normal declarations");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#cascade-origin", section: "6.3")]
    [SpecFact]
    public void User_beats_user_agent()
    {
        var (_, el) = MakeElement("p");

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { color: red; }", StyleOrigin.UserAgent));
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { color: blue; }", StyleOrigin.User));

        engine.Compute(el).GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 0, 255), "User > UA");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#cascade-origin", section: "6.3")]
    [SpecFact]
    public void Author_beats_user()
    {
        var (_, el) = MakeElement("p");

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { color: red; }", StyleOrigin.User));
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { color: blue; }", StyleOrigin.Author));

        engine.Compute(el).GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 0, 255), "Author > User");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#cascade-origin", section: "6.3")]
    [SpecFact]
    public void Author_important_beats_author_normal()
    {
        var (_, el) = MakeElement("p");

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            p { color: green !important; }
            p { color: red; }
            """));

        engine.Compute(el).GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 128, 0), "!important wins over normal in same origin");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#cascade-origin", section: "6.3")]
    [SpecFact]
    public void Author_important_beats_inline_style()
    {
        var (_, el) = MakeElement("p");
        el.SetAttribute("style", "color: red");

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { color: green !important; }"));

        engine.Compute(el).GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 128, 0), "author !important beats inline");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#cascade-origin", section: "6.3")]
    [SpecFact]
    public void User_important_beats_author_important()
    {
        var (_, el) = MakeElement("p");

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { color: red !important; }", StyleOrigin.Author));
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { color: blue !important; }", StyleOrigin.User));

        engine.Compute(el).GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 0, 255), "User!important > Author!important");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#cascade-origin", section: "6.3")]
    [SpecFact]
    public void Ua_important_beats_user_important()
    {
        var (_, el) = MakeElement("p");

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { color: blue !important; }", StyleOrigin.User));
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { color: green !important; }", StyleOrigin.UserAgent));

        engine.Compute(el).GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 128, 0), "UA!important > User!important");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#cascade-origin", section: "6.3")]
    [SpecFact]
    public void Full_origin_stack_ua_normal_loses_to_all()
    {
        // Origin rank (low→high): UA < User < Author < Author! < User! < UA!
        // A single UA-normal declaration loses to Author, User, and all !importants.
        var (_, el) = MakeElement("p");

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { color: purple; }", StyleOrigin.UserAgent));
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { color: green;  }", StyleOrigin.Author));

        engine.Compute(el).GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 128, 0), "Author beats UA");
    }

    // ------------------------------------------------------------------
    // §7 @layer
    // ------------------------------------------------------------------

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#layering", section: "7")]
    [SpecFact]
    public void Unlayered_beats_layered()
    {
        var (_, el) = MakeElement("p");

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            @layer base { p { color: red; } }
            p { color: blue; }
            """));

        engine.Compute(el).GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 0, 255), "unlayered beats layered");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#layering", section: "7")]
    [SpecFact]
    public void Later_layer_beats_earlier_layer()
    {
        var (_, el) = MakeElement("p");

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            @layer base    { p { color: red;   } }
            @layer override { p { color: green; } }
            """));

        engine.Compute(el).GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 128, 0), "later layer wins");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#layering", section: "7")]
    [SpecFact]
    public void Layer_statement_establishes_order()
    {
        // `@layer a, b;` declares a before b, so when both have same-specificity
        // rules, b wins (later layer).
        var (_, el) = MakeElement("p");

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            @layer a, b;
            @layer b { p { color: green; } }
            @layer a { p { color: red;   } }
            """));

        engine.Compute(el).GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 128, 0), "b declared after a wins");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#layering", section: "7")]
    [SpecFact]
    public void Important_reverses_layer_order()
    {
        // For !important, earlier-declared layer beats later-declared.
        var (_, el) = MakeElement("p");

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            @layer first  { p { color: green !important; } }
            @layer second { p { color: red   !important; } }
            """));

        engine.Compute(el).GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 128, 0), "!important reverses layer order: first beats second");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#layering", section: "7")]
    [SpecFact]
    public void Unlayered_important_loses_to_layered_important()
    {
        // !important layered beats !important unlayered (reversed from normal).
        var (_, el) = MakeElement("p");

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            @layer base { p { color: green !important; } }
            p { color: red !important; }
            """));

        engine.Compute(el).GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 128, 0), "layered !important beats unlayered !important");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#layering", section: "7")]
    [SpecFact]
    public void Multiple_layer_blocks_same_name_merge()
    {
        // Re-opening a layer appends to it — first-declared order is the layer's position.
        var (_, el) = MakeElement("p");

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            @layer a { p { color: red;  } }
            @layer b { p { color: blue; } }
            @layer a { p { color: green; } }
            """));

        // Layer a was declared first, then b. The second @layer a block appends to a,
        // so a's color becomes green. But b (later layer) should still win over a.
        engine.Compute(el).GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 0, 255), "b (later layer) beats a");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#layering", section: "7")]
    [SpecFact]
    public void Nested_layers_are_sub_layers()
    {
        var (_, el) = MakeElement("p");

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            @layer outer {
                @layer inner { p { color: red; } }
                p { color: green; }
            }
            """));

        // outer.inner is a layer within outer; unlayered-within-outer beats inner.
        engine.Compute(el).GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 128, 0), "unlayered-within-outer wins over outer.inner");
    }

    // ------------------------------------------------------------------
    // §3 CSS-wide keywords
    // ------------------------------------------------------------------

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#inherit", section: "3")]
    [SpecFact]
    public void Inherit_takes_parents_computed_value()
    {
        var (_, parent, child) = MakeParentChild();

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            div { color: green; }
            p   { color: inherit; }
            """));

        var childStyle = engine.Compute(child);
        childStyle.GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 128, 0), "inherit takes parent computed color");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#initial", section: "3")]
    [SpecFact]
    public void Initial_resets_to_property_initial_value()
    {
        var (_, el) = MakeElement("p");

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { color: initial; }"));

        // color initial is black (#000000).
        var style = engine.Compute(el);
        style.GetColor(PropertyId.Color).Should().Be(Starling.Css.Values.CssColor.Black, "initial resolves to the property's initial value");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#inherit-initial", section: "3")]
    [SpecFact]
    public void Unset_on_inherited_property_acts_as_inherit()
    {
        // color is inherited — `unset` should resolve to parent's value.
        var (_, parent, child) = MakeParentChild();

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            div { color: green; }
            p   { color: unset; }
            """));

        var childStyle = engine.Compute(child);
        childStyle.GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 128, 0), "unset on inherited prop = inherit");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#inherit-initial", section: "3")]
    [SpecFact]
    public void Unset_on_non_inherited_property_acts_as_initial()
    {
        // background-color is not inherited — `unset` should give the initial value (transparent).
        var (_, parent, child) = MakeParentChild();

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            div { background-color: green; }
            p   { background-color: unset; }
            """));

        var childStyle = engine.Compute(child);
        childStyle.GetColor(PropertyId.BackgroundColor).Should().Be(Starling.Css.Values.CssColor.Transparent, "unset on non-inherited = initial (transparent)");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#revert", section: "3")]
    [SpecFact]
    public void Revert_rolls_back_to_previous_origin()
    {
        // Author sets color; `revert` in a later rule should fall back to user origin,
        // then UA origin (or inherited/initial if neither exists).
        var (_, el) = MakeElement("p");

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { color: blue; }", StyleOrigin.User));
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { color: revert; }", StyleOrigin.Author));

        // With revert: author-origin rule is reverted to the User origin rule.
        engine.Compute(el).GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 0, 255), "revert in author falls back to user origin");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#revert", section: "3")]
    [SpecFact]
    public void Revert_with_no_previous_origin_yields_initial_for_non_inherited()
    {
        // No UA/User origin rule for background-color; revert yields initial.
        var (_, el) = MakeElement("p");

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { background-color: revert; }", StyleOrigin.Author));

        engine.Compute(el).GetColor(PropertyId.BackgroundColor).Should().Be(Starling.Css.Values.CssColor.Transparent, "revert with no earlier origin = initial");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#revert", section: "3")]
    [SpecFact]
    public void Revert_with_no_previous_origin_yields_inherited_for_inherited_props()
    {
        // color is inherited; revert with no earlier origin should inherit from parent.
        var (_, parent, child) = MakeParentChild();

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("div { color: green; }"));
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { color: revert; }", StyleOrigin.Author));

        // Author rule is `revert` — falls back to no-author origin. Since there is
        // no user/UA rule for p, it uses the inherit/initial default for the property,
        // which for the inherited `color` property means parent (green).
        engine.Compute(child).GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 128, 0), "revert on inherited prop with no prior origin inherits from parent");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#revert-layer", section: "3")]
    [SpecFact]
    public void Revert_layer_rolls_back_to_previous_layer()
    {
        var (_, el) = MakeElement("p");

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            @layer base     { p { color: green; } }
            @layer override { p { color: revert-layer; } }
            """));

        // revert-layer in 'override' falls back to the value from earlier layer 'base'.
        engine.Compute(el).GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 128, 0), "revert-layer falls back to base layer");
    }

    // ------------------------------------------------------------------
    // §2.2 all property
    // ------------------------------------------------------------------

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#all-shorthand", section: "2.2")]
    [SpecFact]
    public void All_initial_resets_color_to_initial()
    {
        var (_, parent, child) = MakeParentChild();

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            div { color: green; }
            p   { all: initial; }
            """));

        // `all: initial` resets color to its initial value (black), ignoring parent.
        engine.Compute(child).GetColor(PropertyId.Color).Should().Be(Starling.Css.Values.CssColor.Black, "all:initial resets color to initial (ignores inheritance)");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#all-shorthand", section: "2.2")]
    [SpecFact]
    public void All_inherit_makes_every_property_inherit()
    {
        var (_, parent, child) = MakeParentChild();

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            div { color: green; }
            p   { all: inherit; }
            """));

        // `all: inherit` forces every property to inherit from parent.
        engine.Compute(child).GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 128, 0), "all:inherit makes color inherit from parent");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#all-shorthand", section: "2.2")]
    [SpecFact]
    public void All_unset_resets_non_inherited_to_initial()
    {
        var (_, parent, child) = MakeParentChild();

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            div { background-color: green; }
            p   { all: unset; }
            """));

        // background-color is non-inherited; `all: unset` acts as initial → transparent.
        engine.Compute(child).GetColor(PropertyId.BackgroundColor).Should().Be(Starling.Css.Values.CssColor.Transparent, "all:unset resets non-inherited background-color to initial");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#all-shorthand", section: "2.2")]
    [SpecFact]
    public void All_unset_lets_inherited_props_inherit()
    {
        var (_, parent, child) = MakeParentChild();

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            div { color: green; }
            p   { all: unset; }
            """));

        // color is inherited; `all: unset` acts as inherit → parent's green.
        engine.Compute(child).GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 128, 0), "all:unset on inherited color inherits from parent");
    }

    // ------------------------------------------------------------------
    // Inheritance §3 + §4
    // ------------------------------------------------------------------

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#inheritance", section: "3")]
    [SpecFact]
    public void Inherited_property_flows_to_child()
    {
        var (_, parent, child) = MakeParentChild();

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("div { color: green; }"));

        // No color rule for the child — should inherit parent's green.
        engine.Compute(child).GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 128, 0), "color is inherited");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#inheritance", section: "3")]
    [SpecFact]
    public void Non_inherited_property_does_not_flow_to_child()
    {
        var (_, parent, child) = MakeParentChild();

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("div { background-color: green; }"));

        // background-color is not inherited; child should get the initial value (transparent).
        engine.Compute(child).GetColor(PropertyId.BackgroundColor).Should().Be(Starling.Css.Values.CssColor.Transparent, "background-color is not inherited");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#inheritance", section: "3")]
    [SpecFact]
    public void Inherited_property_travels_multiple_levels()
    {
        var doc = new Document();
        var grandparent = doc.CreateElement("div");
        var parent2 = doc.CreateElement("span");
        var child2 = doc.CreateElement("p");
        doc.AppendChild(grandparent);
        grandparent.AppendChild(parent2);
        parent2.AppendChild(child2);

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("div { color: green; }"));

        engine.Compute(child2).GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 128, 0), "color inherits across multiple levels");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#inheritance", section: "3")]
    [SpecFact]
    public void Child_rule_overrides_inherited_value()
    {
        var (_, parent, child) = MakeParentChild();

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            div { color: green; }
            p   { color: red;   }
            """));

        engine.Compute(child).GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(255, 0, 0), "child explicit rule overrides inherited value");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#inheritance", section: "3")]
    [SpecFact]
    public void Color_is_marked_inherited()
        => PropertyRegistry.Inherits(PropertyId.Color).Should().BeTrue();

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#inheritance", section: "3")]
    [SpecFact]
    public void Background_color_is_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.BackgroundColor).Should().BeFalse();

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#inheritance", section: "3")]
    [SpecFact]
    public void Font_size_is_inherited()
        => PropertyRegistry.Inherits(PropertyId.FontSize).Should().BeTrue();

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#inheritance", section: "3")]
    [SpecFact]
    public void Border_top_style_is_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.BorderTopStyle).Should().BeFalse();

    // ------------------------------------------------------------------
    // §6.3 Origin ordering — comprehensive explicit check
    // ------------------------------------------------------------------

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#cascade-origin", section: "6.3")]
    [SpecFact]
    public void All_six_origin_tiers_correct_ordering()
    {
        // Verify that when all six origin-importance combinations are present,
        // UA!important wins (rank 5).
        var (_, el) = MakeElement("p");

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { color: red;    }", StyleOrigin.UserAgent));           // rank 0
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { color: orange; }", StyleOrigin.User));                 // rank 1
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { color: yellow; }", StyleOrigin.Author));               // rank 2
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { color: green  !important; }", StyleOrigin.Author));    // rank 3
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { color: blue   !important; }", StyleOrigin.User));      // rank 4
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { color: purple !important; }", StyleOrigin.UserAgent)); // rank 5

        engine.Compute(el).GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(128, 0, 128), "UA!important (rank 5) wins over all");
    }

    // ------------------------------------------------------------------
    // §6 No-cascade-winner: property gets initial or inherited
    // ------------------------------------------------------------------

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#defaulting", section: "6")]
    [SpecFact]
    public void No_rule_for_inherited_prop_gives_initial_at_root()
    {
        var (doc, el) = MakeElement("p");
        // p is a direct child of document root with no parent element.

        var engine = NoUa();
        // No color rule at all.
        engine.Compute(el).GetColor(PropertyId.Color).Should().Be(Starling.Css.Values.CssColor.Black, "color initial is black at root");
    }

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#defaulting", section: "6")]
    [SpecFact]
    public void No_rule_for_non_inherited_prop_gives_initial()
    {
        var (_, el) = MakeElement("p");

        var engine = NoUa();
        engine.Compute(el).GetColor(PropertyId.BackgroundColor).Should().Be(Starling.Css.Values.CssColor.Transparent, "background-color initial is transparent");
    }

    // ------------------------------------------------------------------
    // Specificity: attribute selector counts as class (0,1,0)
    // ------------------------------------------------------------------

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#cascade-specificity", section: "6.1")]
    [SpecFact]
    public void Attribute_selector_has_class_level_specificity()
    {
        var (_, el) = MakeElement("p");
        el.SetAttribute("data-x", "y");

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            p           { color: red;   }
            p[data-x]   { color: green; }
            """));

        engine.Compute(el).GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 128, 0), "attribute selector (0,1,1) > type selector (0,0,1)");
    }

    // ------------------------------------------------------------------
    // @layer with specificity
    // ------------------------------------------------------------------

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#layering", section: "7")]
    [SpecFact]
    public void Layered_high_specificity_loses_to_unlayered_low_specificity()
    {
        // Even a high-specificity rule in a layer loses to an unlayered low-specificity rule.
        var (_, el) = MakeElement("p");
        el.SetAttribute("id", "foo");
        el.SetAttribute("class", "bar");

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            @layer base { #foo.bar { color: red; } }
            p { color: green; }
            """));

        engine.Compute(el).GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 128, 0), "unlayered p wins over layered #foo.bar");
    }

    // ------------------------------------------------------------------
    // revert-layer: same-origin earlier-layer fallback
    // ------------------------------------------------------------------

    [Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/#revert-layer", section: "3")]
    [SpecFact]
    public void Revert_layer_with_no_prior_layer_falls_to_initial_or_inherit()
    {
        var (_, parent, child) = MakeParentChild();

        var engine = NoUa();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("div { color: green; }"));
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            @layer only { p { color: revert-layer; } }
            """));

        // revert-layer in 'only' — no earlier layer. For an inherited prop, falls back
        // to inherited/initial default.
        engine.Compute(child).GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 128, 0), "revert-layer with no prior layer inherits from parent for inherited color");
    }
}
