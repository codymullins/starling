using FluentAssertions;
using Starling.Css.Cascade;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Selectors;
using Starling.Css.Values;
using Starling.Dom;
namespace Starling.Css.Tests;

[TestClass]
public sealed class StyleEngineTests
{
    [TestMethod]
    public void Computes_cascaded_values_by_origin_specificity_and_order()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        div.Id = "hero";
        div.ClassList.Add("card");
        doc.AppendChild(div);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            div { color: red; margin: 1px 2px; }
            .card { color: green; }
            #hero { color: blue; }
            """));

        var style = engine.Compute(div);

        style.GetColor(PropertyId.Color).Should().Be(new CssColor(0, 0, 255));
        style.GetLength(PropertyId.MarginTop).Should().Be(new CssLength(1, CssLengthUnit.Px));
        style.GetLength(PropertyId.MarginRight).Should().Be(new CssLength(2, CssLengthUnit.Px));
        style.GetPropertyValue("color").Should().Be("rgb(0, 0, 255)");
    }

    [TestMethod]
    public void Inline_style_overrides_author_rules_but_not_important_author_rules()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        p.SetAttribute("style", "color: red; background-color: #036");
        doc.AppendChild(p);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { color: blue !important; }"));

        var style = engine.Compute(p);

        style.GetColor(PropertyId.Color).Should().Be(new CssColor(0, 0, 255));
        style.GetColor(PropertyId.BackgroundColor).Should().Be(new CssColor(0, 51, 102));
    }

    [TestMethod]
    public void Important_user_styles_override_important_author_styles()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { color: blue !important; }"));
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { color: red !important; }", StyleOrigin.User));

        var style = engine.Compute(p);

        style.GetColor(PropertyId.Color).Should().Be(new CssColor(255, 0, 0));
    }

    [TestMethod]
    public void Inherits_values_and_resolves_custom_properties()
    {
        var doc = new Document();
        var root = doc.CreateElement("div");
        var child = doc.CreateElement("p");
        child.ClassList.Add("title");
        doc.AppendChild(root);
        root.AppendChild(child);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            div { color: #036; --space: 12px; }
            p.title { margin-left: var(--space); }
            """));

        var style = engine.Compute(child);

        style.GetColor(PropertyId.Color).Should().Be(new CssColor(0, 51, 102));
        style.GetLength(PropertyId.MarginLeft).Should().Be(new CssLength(12, CssLengthUnit.Px));
    }

    [TestMethod]
    public void User_agent_stylesheet_supplies_block_defaults()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var style = new StyleEngine().Compute(p);

        style.Get(PropertyId.Display).Should().Be(new CssKeyword("block"));
        // `p { margin: 1em 0 }` from the UA sheet — computed-value time resolves
        // the em against the element's 16px font-size, so the margin is 16px.
        style.GetLength(PropertyId.MarginTop).Should().Be(new CssLength(16, CssLengthUnit.Px));
    }

    [TestMethod]
    public void Var_in_shorthand_resolves_via_pending_substitution()
    {
        // CSS Variables L1 §3.7. Authored as a shorthand whose sole component
        // is `var(--name)` — the longhand expansion can't run at parse time
        // because the var()'s tokens haven't been substituted yet. The shape
        // matches netclaw.dev's `body { background: var(--ink); }`.
        var doc = new Document();
        var body = doc.CreateElement("body");
        doc.AppendChild(body);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            :root { --ink: #0f1117; }
            body  { background: var(--ink); }
            """));

        var style = engine.Compute(body);

        style.GetColor(PropertyId.BackgroundColor).Should().Be(new CssColor(0x0f, 0x11, 0x17));
    }

    [TestMethod]
    public void Var_in_shorthand_resets_other_longhands_to_initial()
    {
        // CSS Variables L1 §3.7 — using var() in a shorthand still resets every
        // longhand the shorthand maps to. Here `--ink` resolves to a color, so
        // BackgroundImage should be reset to its initial value (`none`) rather
        // than inheriting a stale earlier value.
        var doc = new Document();
        var body = doc.CreateElement("body");
        doc.AppendChild(body);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            :root { --ink: #0f1117; }
            body  { background-image: url(stale.png); background: var(--ink); }
            """));

        var style = engine.Compute(body);

        style.GetColor(PropertyId.BackgroundColor).Should().Be(new CssColor(0x0f, 0x11, 0x17));
        style.Get(PropertyId.BackgroundImage).Should().Be(new CssKeyword("none"));
    }

    [TestMethod]
    public void Var_with_multi_component_value_splices_into_shorthand()
    {
        // §3.7 substitution operates on tokens, so a multi-component custom
        // property splices into the surrounding shorthand context rather than
        // appearing as a single nested list. `border: var(--side)` where
        // `--side: 2px solid red` must populate all three border-* longhands.
        var doc = new Document();
        var div = doc.CreateElement("div");
        doc.AppendChild(div);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            :root { --side: 2px solid red; }
            div   { border: var(--side); }
            """));

        var style = engine.Compute(div);

        style.GetLength(PropertyId.BorderTopWidth).Should().Be(new CssLength(2, CssLengthUnit.Px));
        style.Get(PropertyId.BorderTopStyle).Should().Be(new CssKeyword("solid"));
        style.GetColor(PropertyId.BorderTopColor).Should().Be(new CssColor(255, 0, 0));
    }

    [TestMethod]
    public void Var_fallback_used_when_custom_property_undefined_in_shorthand()
    {
        // §3.7 with a fallback: `background: var(--missing, #abcdef)` — when
        // --missing isn't declared the fallback substitutes and re-expansion
        // sets background-color from it.
        var doc = new Document();
        var body = doc.CreateElement("body");
        doc.AppendChild(body);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("body { background: var(--missing, #abcdef); }"));

        var style = engine.Compute(body);

        style.GetColor(PropertyId.BackgroundColor).Should().Be(new CssColor(0xab, 0xcd, 0xef));
    }

    [TestMethod]
    public void Hover_match_context_recascades_styles_for_link_state()
    {
        var doc = new Document();
        var a = doc.CreateElement("a");
        a.SetAttribute("href", "#");
        doc.AppendChild(a);

        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            a { color: blue; }
            a:hover { color: red; }
            """));

        var resting = engine.Compute(a);
        var hovered = engine.Compute(a, new SelectorMatchContext { HoveredElement = a });

        resting.GetColor(PropertyId.Color).Should().Be(new CssColor(0, 0, 255));
        hovered.GetColor(PropertyId.Color).Should().Be(new CssColor(255, 0, 0),
            "the :hover rule should win when the element is the hovered element");
    }
}
