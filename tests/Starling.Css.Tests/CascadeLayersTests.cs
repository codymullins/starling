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
public sealed class CascadeLayersTests
{
    [TestMethod]
    public void Unlayered_styles_beat_layered_styles_for_non_important()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            @layer base { p { color: red; } }
            p { color: blue; }
            """));

        engine.Compute(p).GetColor(PropertyId.Color).Should().Be(new CssColor(0, 0, 255));
    }

    [TestMethod]
    public void Layer_order_is_declaration_order()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            @layer reset, components;
            @layer components { p { color: red; } }
            @layer reset { p { color: blue; } }
            """));

        // components is declared after reset → components wins.
        engine.Compute(p).GetColor(PropertyId.Color).Should().Be(new CssColor(255, 0, 0));
    }

    [TestMethod]
    public void Important_inverts_layer_order()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            @layer reset, components;
            @layer components { p { color: red !important; } }
            @layer reset { p { color: blue !important; } }
            """));

        // For !important, earliest layer wins → reset (blue).
        engine.Compute(p).GetColor(PropertyId.Color).Should().Be(new CssColor(0, 0, 255));
    }

    [TestMethod]
    public void Important_layered_beats_important_unlayered()
    {
        // CSS Cascade 5: among !important, layered styles win over unlayered.
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            @layer base { p { color: red !important; } }
            p { color: blue !important; }
            """));

        engine.Compute(p).GetColor(PropertyId.Color).Should().Be(new CssColor(255, 0, 0));
    }

    [TestMethod]
    public void Nested_layer_names_resolve_to_dotted_paths()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            @layer reset.tailwind { p { color: red; } }
            """));

        engine.GetLayersForOrigin(StyleOrigin.Author).Should()
            .ContainKey("reset.tailwind");
    }

    [TestMethod]
    public void Statement_form_pre_registers_layer_order()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            @layer reset, base, components, utilities;
            """));

        var layers = engine.GetLayersForOrigin(StyleOrigin.Author);
        layers.Should().ContainKeys("reset", "base", "components", "utilities");
        layers["reset"].Should().BeLessThan(layers["utilities"]);
    }
}
