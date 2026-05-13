using FluentAssertions;
using Tessera.Css.Cascade;
using Tessera.Css.Parser;
using Tessera.Css.Properties;
using Tessera.Css.Selectors;
using Tessera.Css.Values;
using Tessera.Dom;
using Xunit;

namespace Tessera.Css.Tests;

public sealed class StyleEngineTests
{
    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
    public void User_agent_stylesheet_supplies_block_defaults()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var style = new StyleEngine().Compute(p);

        style.Get(PropertyId.Display).Should().Be(new CssKeyword("block"));
        style.GetLength(PropertyId.MarginTop).Should().Be(new CssLength(1, CssLengthUnit.Em));
    }

    [Fact]
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
