using FluentAssertions;
using Tessera.Css.Cascade;
using Tessera.Css.Media;
using Tessera.Css.Parser;
using Tessera.Css.Properties;
using Tessera.Css.Values;
using Tessera.Dom;
using Xunit;

namespace Tessera.Css.Tests;

public sealed class NestingTests
{
    [Fact]
    public void Ampersand_child_selector_matches_child()
    {
        var doc = new Document();
        var parent = doc.CreateElement("div");
        parent.ClassList.Add("parent");
        var child = doc.CreateElement("span");
        child.ClassList.Add("child");
        doc.AppendChild(parent);
        parent.AppendChild(child);

        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            .parent {
              color: red;
              & .child { color: blue; }
            }
            """));

        engine.Compute(child).GetColor(PropertyId.Color).Should().Be(new CssColor(0, 0, 255));
    }

    [Fact]
    public void Implicit_ampersand_works_for_class_selectors()
    {
        var doc = new Document();
        var parent = doc.CreateElement("div");
        parent.ClassList.Add("parent");
        var child = doc.CreateElement("span");
        child.ClassList.Add("child");
        doc.AppendChild(parent);
        parent.AppendChild(child);

        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            .parent {
              .child { color: blue; }
            }
            """));

        engine.Compute(child).GetColor(PropertyId.Color).Should().Be(new CssColor(0, 0, 255));
    }

    [Fact]
    public void Ampersand_hover_combines_with_parent_for_pseudo()
    {
        var doc = new Document();
        var a = doc.CreateElement("a");
        doc.AppendChild(a);

        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            a {
              color: blue;
              &:hover { color: red; }
            }
            """));

        engine.Compute(a).GetColor(PropertyId.Color).Should().Be(new CssColor(0, 0, 255));
        engine.Compute(a, new Tessera.Css.Selectors.SelectorMatchContext { HoveredElement = a })
            .GetColor(PropertyId.Color).Should().Be(new CssColor(255, 0, 0));
    }

    [Fact]
    public void Ampersand_child_combinator_matches_direct_children_only()
    {
        var doc = new Document();
        var parent = doc.CreateElement("div");
        parent.ClassList.Add("parent");
        var direct = doc.CreateElement("span");
        direct.ClassList.Add("item");
        doc.AppendChild(parent);
        parent.AppendChild(direct);

        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            .parent {
              & > .item { color: red; }
            }
            """));

        engine.Compute(direct).GetColor(PropertyId.Color).Should().Be(new CssColor(255, 0, 0));
    }

    [Fact]
    public void Nested_media_query_applies_only_at_matching_viewport()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        p.ClassList.Add("box");
        doc.AppendChild(p);

        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            .box {
              color: black;
              @media (min-width: 600px) {
                color: red;
              }
            }
            """));

        engine.MediaContext = new MediaContext(ViewportWidthPx: 400);
        engine.Compute(p).GetColor(PropertyId.Color).Should().Be(CssColor.Black);

        engine.MediaContext = new MediaContext(ViewportWidthPx: 800);
        engine.Compute(p).GetColor(PropertyId.Color).Should().Be(new CssColor(255, 0, 0));
    }

    [Fact]
    public void End_to_end_layered_supports_media_nesting_example()
    {
        var doc = new Document();
        var grid = doc.CreateElement("div");
        grid.ClassList.Add("grid");
        var item = doc.CreateElement("span");
        item.ClassList.Add("item");
        doc.AppendChild(grid);
        grid.AppendChild(item);

        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.MediaContext = new MediaContext(ViewportWidthPx: 800);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            @layer reset, components;
            @layer reset { p { margin: 0; } }
            @media (min-width: 600px) {
              @supports (color: red) {
                .grid { color: green; & > .item { color: red; } }
              }
            }
            """));

        engine.Compute(item).GetColor(PropertyId.Color).Should().Be(new CssColor(255, 0, 0));
    }
}
