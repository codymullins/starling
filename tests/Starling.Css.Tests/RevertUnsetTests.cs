using FluentAssertions;
using Starling.Css.Cascade;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Dom;
using Starling.Spec;

namespace Starling.Css.Tests;

[Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/")]

[TestClass]
public sealed class RevertUnsetTests
{
    [TestMethod]
    public void Unset_acts_as_inherit_for_inherited_properties()
    {
        var doc = new Document();
        var parent = doc.CreateElement("div");
        var child = doc.CreateElement("p");
        doc.AppendChild(parent);
        parent.AppendChild(child);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            div { color: red; }
            p { color: unset; }
            """));

        engine.Compute(child).GetColor(PropertyId.Color).Should().Be(new CssColor(255, 0, 0));
    }

    [TestMethod]
    public void Unset_acts_as_initial_for_non_inherited_properties()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            p { background-color: red; background-color: unset; }
            """));

        engine.Compute(p).GetColor(PropertyId.BackgroundColor).Should().Be(CssColor.Transparent);
    }

    [TestMethod]
    public void Revert_falls_back_to_previous_origin()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { color: blue; }", StyleOrigin.User));
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { color: red; }"));
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { color: revert; }"));

        // Author `revert` rolls back to user-origin's `blue`.
        engine.Compute(p).GetColor(PropertyId.Color).Should().Be(new CssColor(0, 0, 255));
    }

    [TestMethod]
    public void Revert_layer_falls_back_to_previous_layer_in_same_origin()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            @layer base, theme;
            @layer base { p { color: blue; } }
            @layer theme { p { color: revert-layer; } }
            """));

        engine.Compute(p).GetColor(PropertyId.Color).Should().Be(new CssColor(0, 0, 255));
    }

    [TestMethod]
    public void All_initial_resets_every_property()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            p { color: red; background-color: yellow; all: initial; }
            """));

        var style = engine.Compute(p);
        style.GetColor(PropertyId.Color).Should().Be(CssColor.Black);
        style.GetColor(PropertyId.BackgroundColor).Should().Be(CssColor.Transparent);
    }

    [TestMethod]
    public void All_revert_falls_back_to_previous_origin_for_every_property()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { color: blue; }", StyleOrigin.User));
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { color: red; all: revert; }"));

        engine.Compute(p).GetColor(PropertyId.Color).Should().Be(new CssColor(0, 0, 255));
    }
}
