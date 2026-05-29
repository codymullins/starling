using AwesomeAssertions;
using Starling.Css.Cascade;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Dom;

namespace Starling.Css.Spec.Tests.CssDisplay3;

/// <summary>
/// Conformance suite for
/// <see href="https://www.w3.org/TR/css-display-3/">CSS Display Module Level 3</see>.
/// Covers §2 display values, §2.1 two-value syntax, §3 computed values, and
/// the UA stylesheet defaults for common HTML elements.
/// </summary>
[TestClass]
[Spec("css-display-3", "https://www.w3.org/TR/css-display-3/")]
public sealed class DisplayTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static PropertyDeclaration ParseDisplay(string value)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ display: {value}; }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).Single();
    }

    private static Element InDocument(string tag)
    {
        var doc = new Document();
        var el = doc.CreateElement(tag);
        doc.AppendChild(el);
        return el;
    }

    private static Element InDocumentChain(params string[] tags)
    {
        var doc = new Document();
        Node parent = doc;
        Element last = null!;
        foreach (var tag in tags)
        {
            last = doc.CreateElement(tag);
            parent.AppendChild(last);
            parent = last;
        }
        return last;
    }

    // ---------------------------------------------------------------------------
    // §2 — The display property: initial value
    // ---------------------------------------------------------------------------

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#propdef-display", section: "2")]
    [SpecFact]
    public void Initial_value_is_inline()
        => PropertyRegistry.InitialValue(PropertyId.Display).Should().Be(new CssKeyword("inline"));

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#propdef-display", section: "2")]
    [SpecFact]
    public void Display_is_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.Display).Should().BeFalse();

    // ---------------------------------------------------------------------------
    // §2 — Single-keyword display values: parse to PropertyId.Display
    // ---------------------------------------------------------------------------

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#valdef-display-block", section: "2")]
    [SpecFact]
    public void Parses_block()
    {
        var decl = ParseDisplay("block");
        decl.Id.Should().Be(PropertyId.Display);
        decl.Value.Should().Be(new CssKeyword("block"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#valdef-display-inline", section: "2")]
    [SpecFact]
    public void Parses_inline()
    {
        var decl = ParseDisplay("inline");
        decl.Id.Should().Be(PropertyId.Display);
        decl.Value.Should().Be(new CssKeyword("inline"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#valdef-display-inline-block", section: "2")]
    [SpecFact]
    public void Parses_inline_block()
    {
        var decl = ParseDisplay("inline-block");
        decl.Id.Should().Be(PropertyId.Display);
        decl.Value.Should().Be(new CssKeyword("inline-block"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#valdef-display-flex", section: "2")]
    [SpecFact]
    public void Parses_flex()
    {
        var decl = ParseDisplay("flex");
        decl.Id.Should().Be(PropertyId.Display);
        decl.Value.Should().Be(new CssKeyword("flex"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#valdef-display-inline-flex", section: "2")]
    [SpecFact]
    public void Parses_inline_flex()
    {
        var decl = ParseDisplay("inline-flex");
        decl.Id.Should().Be(PropertyId.Display);
        decl.Value.Should().Be(new CssKeyword("inline-flex"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#valdef-display-grid", section: "2")]
    [SpecFact]
    public void Parses_grid()
    {
        var decl = ParseDisplay("grid");
        decl.Id.Should().Be(PropertyId.Display);
        decl.Value.Should().Be(new CssKeyword("grid"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#valdef-display-inline-grid", section: "2")]
    [SpecFact]
    public void Parses_inline_grid()
    {
        var decl = ParseDisplay("inline-grid");
        decl.Id.Should().Be(PropertyId.Display);
        decl.Value.Should().Be(new CssKeyword("inline-grid"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#valdef-display-none", section: "2")]
    [SpecFact]
    public void Parses_none()
    {
        var decl = ParseDisplay("none");
        decl.Id.Should().Be(PropertyId.Display);
        decl.Value.Should().Be(new CssKeyword("none"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#valdef-display-contents", section: "2")]
    [SpecFact]
    public void Parses_contents()
    {
        var decl = ParseDisplay("contents");
        decl.Id.Should().Be(PropertyId.Display);
        decl.Value.Should().Be(new CssKeyword("contents"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#valdef-display-flow-root", section: "2")]
    [SpecFact]
    public void Parses_flow_root()
    {
        var decl = ParseDisplay("flow-root");
        decl.Id.Should().Be(PropertyId.Display);
        decl.Value.Should().Be(new CssKeyword("flow-root"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#valdef-display-list-item", section: "2")]
    [SpecFact]
    public void Parses_list_item()
    {
        var decl = ParseDisplay("list-item");
        decl.Id.Should().Be(PropertyId.Display);
        decl.Value.Should().Be(new CssKeyword("list-item"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#valdef-display-table", section: "2")]
    [SpecFact]
    public void Parses_table()
    {
        var decl = ParseDisplay("table");
        decl.Id.Should().Be(PropertyId.Display);
        decl.Value.Should().Be(new CssKeyword("table"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#valdef-display-table-row", section: "2")]
    [SpecFact]
    public void Parses_table_row()
    {
        var decl = ParseDisplay("table-row");
        decl.Id.Should().Be(PropertyId.Display);
        decl.Value.Should().Be(new CssKeyword("table-row"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#valdef-display-table-cell", section: "2")]
    [SpecFact]
    public void Parses_table_cell()
    {
        var decl = ParseDisplay("table-cell");
        decl.Id.Should().Be(PropertyId.Display);
        decl.Value.Should().Be(new CssKeyword("table-cell"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#valdef-display-table-row-group", section: "2")]
    [SpecFact]
    public void Parses_table_row_group()
    {
        var decl = ParseDisplay("table-row-group");
        decl.Id.Should().Be(PropertyId.Display);
        decl.Value.Should().Be(new CssKeyword("table-row-group"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#valdef-display-table-header-group", section: "2")]
    [SpecFact]
    public void Parses_table_header_group()
    {
        var decl = ParseDisplay("table-header-group");
        decl.Id.Should().Be(PropertyId.Display);
        decl.Value.Should().Be(new CssKeyword("table-header-group"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#valdef-display-table-footer-group", section: "2")]
    [SpecFact]
    public void Parses_table_footer_group()
    {
        var decl = ParseDisplay("table-footer-group");
        decl.Id.Should().Be(PropertyId.Display);
        decl.Value.Should().Be(new CssKeyword("table-footer-group"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#valdef-display-table-column", section: "2")]
    [SpecFact]
    public void Parses_table_column()
    {
        var decl = ParseDisplay("table-column");
        decl.Id.Should().Be(PropertyId.Display);
        decl.Value.Should().Be(new CssKeyword("table-column"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#valdef-display-table-column-group", section: "2")]
    [SpecFact]
    public void Parses_table_column_group()
    {
        var decl = ParseDisplay("table-column-group");
        decl.Id.Should().Be(PropertyId.Display);
        decl.Value.Should().Be(new CssKeyword("table-column-group"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#valdef-display-table-caption", section: "2")]
    [SpecFact]
    public void Parses_table_caption()
    {
        var decl = ParseDisplay("table-caption");
        decl.Id.Should().Be(PropertyId.Display);
        decl.Value.Should().Be(new CssKeyword("table-caption"));
    }

    // ---------------------------------------------------------------------------
    // §2 — run-in: a value the spec defines but few engines implement
    // ---------------------------------------------------------------------------

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#valdef-display-run-in", section: "2")]
    [SpecFact]    public void Parses_run_in()
    {
        var decl = ParseDisplay("run-in");
        decl.Id.Should().Be(PropertyId.Display);
        decl.Value.Should().Be(new CssKeyword("run-in"));
    }

    // ---------------------------------------------------------------------------
    // §2.1 — Two-value display syntax (CSS Display Level 3)
    // ---------------------------------------------------------------------------

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#typedef-display-outside", section: "2.1")]
    [SpecFact]    public void Two_value_block_flow_parses()
    {
        // CSS Display 3 §2.1: `block flow` is the canonical two-value form for `display: block`.
        var decl = ParseDisplay("block flow");
        decl.Id.Should().Be(PropertyId.Display);
        // Expected: engine normalises to the legacy single-keyword `block`.
        decl.Value.Should().Be(new CssKeyword("block"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#typedef-display-outside", section: "2.1")]
    [SpecFact]    public void Two_value_inline_flow_root_maps_to_inline_block()
    {
        // CSS Display 3 §2.1: `inline flow-root` ≡ legacy `inline-block`.
        var decl = ParseDisplay("inline flow-root");
        decl.Id.Should().Be(PropertyId.Display);
        decl.Value.Should().Be(new CssKeyword("inline-block"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#typedef-display-outside", section: "2.1")]
    [SpecFact]    public void Two_value_block_flex_maps_to_flex()
    {
        // CSS Display 3 §2.1: `block flex` ≡ legacy `flex`.
        var decl = ParseDisplay("block flex");
        decl.Id.Should().Be(PropertyId.Display);
        decl.Value.Should().Be(new CssKeyword("flex"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#typedef-display-outside", section: "2.1")]
    [SpecFact]    public void Two_value_inline_flex_maps_to_inline_flex()
    {
        // CSS Display 3 §2.1: `inline flex` ≡ legacy `inline-flex`.
        var decl = ParseDisplay("inline flex");
        decl.Id.Should().Be(PropertyId.Display);
        decl.Value.Should().Be(new CssKeyword("inline-flex"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#typedef-display-outside", section: "2.1")]
    [SpecFact]    public void Two_value_block_grid_maps_to_grid()
    {
        // CSS Display 3 §2.1: `block grid` ≡ legacy `grid`.
        var decl = ParseDisplay("block grid");
        decl.Id.Should().Be(PropertyId.Display);
        decl.Value.Should().Be(new CssKeyword("grid"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#typedef-display-outside", section: "2.1")]
    [SpecFact]    public void Two_value_inline_grid_maps_to_inline_grid()
    {
        // CSS Display 3 §2.1: `inline grid` ≡ legacy `inline-grid`.
        var decl = ParseDisplay("inline grid");
        decl.Id.Should().Be(PropertyId.Display);
        decl.Value.Should().Be(new CssKeyword("inline-grid"));
    }

    // ---------------------------------------------------------------------------
    // §3 — Computed values via StyleEngine.Compute
    // ---------------------------------------------------------------------------

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#propdef-display", section: "3")]
    [SpecFact]
    public void Computed_block_is_block()
    {
        var el = InDocument("div");
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        var sheet = CssParser.ParseStyleSheet("div { display: block; }");
        engine.AddStyleSheet(sheet);
        engine.Compute(el).Get(PropertyId.Display).Should().Be(new CssKeyword("block"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#propdef-display", section: "3")]
    [SpecFact]
    public void Computed_inline_is_inline()
    {
        var el = InDocument("span");
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        var sheet = CssParser.ParseStyleSheet("span { display: inline; }");
        engine.AddStyleSheet(sheet);
        engine.Compute(el).Get(PropertyId.Display).Should().Be(new CssKeyword("inline"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#propdef-display", section: "3")]
    [SpecFact]
    public void Computed_none_is_none()
    {
        var el = InDocument("div");
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        var sheet = CssParser.ParseStyleSheet("div { display: none; }");
        engine.AddStyleSheet(sheet);
        engine.Compute(el).Get(PropertyId.Display).Should().Be(new CssKeyword("none"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#propdef-display", section: "3")]
    [SpecFact]
    public void Computed_flex_is_flex()
    {
        var el = InDocument("div");
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        var sheet = CssParser.ParseStyleSheet("div { display: flex; }");
        engine.AddStyleSheet(sheet);
        engine.Compute(el).Get(PropertyId.Display).Should().Be(new CssKeyword("flex"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#propdef-display", section: "3")]
    [SpecFact]
    public void Computed_inline_flex_is_inline_flex()
    {
        var el = InDocument("div");
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        var sheet = CssParser.ParseStyleSheet("div { display: inline-flex; }");
        engine.AddStyleSheet(sheet);
        engine.Compute(el).Get(PropertyId.Display).Should().Be(new CssKeyword("inline-flex"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#propdef-display", section: "3")]
    [SpecFact]
    public void Computed_grid_is_grid()
    {
        var el = InDocument("div");
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        var sheet = CssParser.ParseStyleSheet("div { display: grid; }");
        engine.AddStyleSheet(sheet);
        engine.Compute(el).Get(PropertyId.Display).Should().Be(new CssKeyword("grid"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#propdef-display", section: "3")]
    [SpecFact]
    public void Computed_inline_grid_is_inline_grid()
    {
        var el = InDocument("div");
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        var sheet = CssParser.ParseStyleSheet("div { display: inline-grid; }");
        engine.AddStyleSheet(sheet);
        engine.Compute(el).Get(PropertyId.Display).Should().Be(new CssKeyword("inline-grid"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#propdef-display", section: "3")]
    [SpecFact]
    public void Computed_inline_block_is_inline_block()
    {
        var el = InDocument("div");
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        var sheet = CssParser.ParseStyleSheet("div { display: inline-block; }");
        engine.AddStyleSheet(sheet);
        engine.Compute(el).Get(PropertyId.Display).Should().Be(new CssKeyword("inline-block"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#propdef-display", section: "3")]
    [SpecFact]
    public void Computed_list_item_is_list_item()
    {
        var el = InDocument("div");
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        var sheet = CssParser.ParseStyleSheet("div { display: list-item; }");
        engine.AddStyleSheet(sheet);
        engine.Compute(el).Get(PropertyId.Display).Should().Be(new CssKeyword("list-item"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#propdef-display", section: "3")]
    [SpecFact]
    public void Computed_table_is_table()
    {
        var el = InDocument("div");
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        var sheet = CssParser.ParseStyleSheet("div { display: table; }");
        engine.AddStyleSheet(sheet);
        engine.Compute(el).Get(PropertyId.Display).Should().Be(new CssKeyword("table"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#propdef-display", section: "3")]
    [SpecFact]
    public void Computed_contents_is_contents()
    {
        // CSS Display 3 §3.3 — `display: contents` is a valid computed value.
        // The matrix notes that layout effects are not implemented; the parsed
        // and computed keyword value is nevertheless correct.
        var el = InDocument("div");
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        var sheet = CssParser.ParseStyleSheet("div { display: contents; }");
        engine.AddStyleSheet(sheet);
        engine.Compute(el).Get(PropertyId.Display).Should().Be(new CssKeyword("contents"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#propdef-display", section: "3")]
    [SpecFact]
    public void Computed_flow_root_is_flow_root()
    {
        // CSS Display 3 §3 — `display: flow-root` establishes a block formatting
        // context. The matrix notes layout effects are not implemented; the
        // computed keyword value is nevertheless correct.
        var el = InDocument("div");
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        var sheet = CssParser.ParseStyleSheet("div { display: flow-root; }");
        engine.AddStyleSheet(sheet);
        engine.Compute(el).Get(PropertyId.Display).Should().Be(new CssKeyword("flow-root"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#propdef-display", section: "3")]
    [SpecFact]
    public void Computed_table_row_is_table_row()
    {
        var el = InDocument("div");
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        var sheet = CssParser.ParseStyleSheet("div { display: table-row; }");
        engine.AddStyleSheet(sheet);
        engine.Compute(el).Get(PropertyId.Display).Should().Be(new CssKeyword("table-row"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#propdef-display", section: "3")]
    [SpecFact]
    public void Computed_table_cell_is_table_cell()
    {
        var el = InDocument("div");
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        var sheet = CssParser.ParseStyleSheet("div { display: table-cell; }");
        engine.AddStyleSheet(sheet);
        engine.Compute(el).Get(PropertyId.Display).Should().Be(new CssKeyword("table-cell"));
    }

    // ---------------------------------------------------------------------------
    // §3 — No cascade → initial value is inline
    // ---------------------------------------------------------------------------

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#propdef-display", section: "3")]
    [SpecFact]
    public void Element_with_no_rules_computes_initial_inline()
    {
        var el = InDocument("span");
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.Compute(el).Get(PropertyId.Display).Should().Be(new CssKeyword("inline"));
    }

    // ---------------------------------------------------------------------------
    // §3 — display is not inherited (child gets initial, not parent value)
    // ---------------------------------------------------------------------------

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#propdef-display", section: "3")]
    [SpecFact]
    public void Display_not_inherited_by_child()
    {
        // Parent is flex; child has no explicit display — should get initial `inline`,
        // not inherit `flex` from parent.
        var doc = new Document();
        var parent = doc.CreateElement("div");
        var child = doc.CreateElement("span");
        doc.AppendChild(parent);
        parent.AppendChild(child);

        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        var sheet = CssParser.ParseStyleSheet("div { display: flex; }");
        engine.AddStyleSheet(sheet);
        engine.Compute(child).Get(PropertyId.Display).Should().Be(new CssKeyword("inline"));
    }

    // ---------------------------------------------------------------------------
    // UA stylesheet defaults (§appendix-B / WHATWG HTML rendering §15)
    // ---------------------------------------------------------------------------

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#block-level", section: "2")]
    [SpecFact]
    public void Ua_div_is_block()
    {
        var el = InDocumentChain("html", "body", "div");
        var engine = new StyleEngine();
        engine.Compute(el).Get(PropertyId.Display).Should().Be(new CssKeyword("block"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#inline-level", section: "2")]
    [SpecFact]
    public void Ua_span_is_inline()
    {
        var doc = new Document();
        var span = doc.CreateElement("span");
        doc.AppendChild(span);
        var engine = new StyleEngine();
        // <span> has no UA rule, so it gets the initial value: inline.
        engine.Compute(span).Get(PropertyId.Display).Should().Be(new CssKeyword("inline"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#list-items", section: "2")]
    [SpecFact]
    public void Ua_li_is_list_item()
    {
        var el = InDocumentChain("html", "body", "ul", "li");
        var engine = new StyleEngine();
        engine.Compute(el).Get(PropertyId.Display).Should().Be(new CssKeyword("list-item"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#block-level", section: "2")]
    [SpecFact]
    public void Ua_p_is_block()
    {
        var el = InDocumentChain("html", "body", "p");
        var engine = new StyleEngine();
        engine.Compute(el).Get(PropertyId.Display).Should().Be(new CssKeyword("block"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#block-level", section: "2")]
    [SpecFact]
    public void Ua_h1_through_h6_are_block()
    {
        var engine = new StyleEngine();
        foreach (var tag in new[] { "h1", "h2", "h3", "h4", "h5", "h6" })
        {
            var el = InDocumentChain("html", "body", tag);
            engine.Compute(el).Get(PropertyId.Display)
                .Should().Be(new CssKeyword("block"), $"<{tag}> should be display:block per UA sheet");
        }
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#block-level", section: "2")]
    [SpecFact]
    public void Ua_section_article_header_footer_nav_are_block()
    {
        var engine = new StyleEngine();
        foreach (var tag in new[] { "section", "article", "header", "footer", "nav", "main", "aside" })
        {
            var el = InDocumentChain("html", "body", tag);
            engine.Compute(el).Get(PropertyId.Display)
                .Should().Be(new CssKeyword("block"), $"<{tag}> should be display:block per UA sheet");
        }
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#block-level", section: "2")]
    [SpecFact]
    public void Ua_blockquote_pre_address_figure_are_block()
    {
        var engine = new StyleEngine();
        foreach (var tag in new[] { "blockquote", "pre", "address", "figure", "figcaption" })
        {
            var el = InDocumentChain("html", "body", tag);
            engine.Compute(el).Get(PropertyId.Display)
                .Should().Be(new CssKeyword("block"), $"<{tag}> should be display:block per UA sheet");
        }
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#valdef-display-none", section: "2")]
    [SpecFact]
    public void Ua_script_style_head_are_none()
    {
        var engine = new StyleEngine();
        foreach (var tag in new[] { "script", "style", "head", "meta", "link", "title" })
        {
            var el = InDocumentChain("html", tag);
            engine.Compute(el).Get(PropertyId.Display)
                .Should().Be(new CssKeyword("none"), $"<{tag}> should be display:none per UA sheet");
        }
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#valdef-display-none", section: "2")]
    [SpecFact]
    public void Ua_noscript_is_none()
    {
        var el = InDocumentChain("html", "noscript");
        var engine = new StyleEngine();
        engine.Compute(el).Get(PropertyId.Display).Should().Be(new CssKeyword("none"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#valdef-display-table", section: "2")]
    [SpecFact]
    public void Ua_table_element_is_block_approximation()
    {
        // The UA sheet approximates `table` as `block` pending a full table
        // formatting context implementation. The computed value is `block`.
        var el = InDocumentChain("html", "body", "table");
        var engine = new StyleEngine();
        engine.Compute(el).Get(PropertyId.Display).Should().Be(new CssKeyword("block"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#valdef-display-table-row", section: "2")]
    [PendingFact("UA sheet maps <tr> to display:block (table layout approximation); spec requires display:table-row", trackingWp: "wp:spec-css-display-3")]
    public void Ua_tr_is_table_row()
    {
        // CSS Display 3 §appendix-B / WHATWG HTML §15.3.10 — <tr> should be table-row.
        // Current UA sheet sets `tr { display: block; }` as a layout approximation.
        var el = InDocumentChain("html", "body", "table", "tr");
        var engine = new StyleEngine();
        engine.Compute(el).Get(PropertyId.Display).Should().Be(new CssKeyword("table-row"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#valdef-display-table-cell", section: "2")]
    [PendingFact("UA sheet maps <td>/<th> to display:inline-block (table layout approximation); spec requires display:table-cell", trackingWp: "wp:spec-css-display-3")]
    public void Ua_td_th_are_table_cell()
    {
        // CSS Display 3 §appendix-B / WHATWG HTML §15.3.10 — <td>/<th> should be table-cell.
        // Current UA sheet sets `td, th { display: inline-block; }` as an approximation.
        var engine = new StyleEngine();
        foreach (var tag in new[] { "td", "th" })
        {
            var el = InDocumentChain("html", "body", "table", "tr", tag);
            engine.Compute(el).Get(PropertyId.Display)
                .Should().Be(new CssKeyword("table-cell"), $"<{tag}> should be display:table-cell per spec");
        }
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#valdef-display-table-row-group", section: "2")]
    [PendingFact("UA sheet maps <tbody>/<thead>/<tfoot> to display:block (table layout approximation); spec requires display:table-row-group/table-header-group/table-footer-group", trackingWp: "wp:spec-css-display-3")]
    public void Ua_tbody_thead_tfoot_are_table_row_groups()
    {
        // CSS Display 3 §appendix-B — tbody→table-row-group, thead→table-header-group,
        // tfoot→table-footer-group. UA sheet uses `display: block` as approximation.
        var engine = new StyleEngine();
        var expected = new Dictionary<string, string>
        {
            ["tbody"] = "table-row-group",
            ["thead"] = "table-header-group",
            ["tfoot"] = "table-footer-group",
        };
        foreach (var (tag, expectedValue) in expected)
        {
            var el = InDocumentChain("html", "body", "table", tag);
            engine.Compute(el).Get(PropertyId.Display)
                .Should().Be(new CssKeyword(expectedValue), $"<{tag}> should be display:{expectedValue} per spec");
        }
    }

    // ---------------------------------------------------------------------------
    // §3.3 — display:none — element generates no box
    // ---------------------------------------------------------------------------

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#valdef-display-none", section: "3.3")]
    [SpecFact]
    public void Computed_none_via_author_sheet()
    {
        var el = InDocumentChain("html", "body", "div");
        var engine = new StyleEngine();
        var sheet = CssParser.ParseStyleSheet("div { display: none; }");
        engine.AddStyleSheet(sheet);
        engine.Compute(el).Get(PropertyId.Display).Should().Be(new CssKeyword("none"));
    }

    // ---------------------------------------------------------------------------
    // §3.4 — display:contents — element generates no principal box
    // ---------------------------------------------------------------------------

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#valdef-display-contents", section: "3.4")]
    [SpecFact]
    public void Computed_contents_via_author_sheet()
    {
        // display:contents parses and computes correctly at the value level.
        // Layout treatment (skipping the principal box) is tracked separately.
        var el = InDocumentChain("html", "body", "div");
        var engine = new StyleEngine();
        var sheet = CssParser.ParseStyleSheet("div { display: contents; }");
        engine.AddStyleSheet(sheet);
        engine.Compute(el).Get(PropertyId.Display).Should().Be(new CssKeyword("contents"));
    }

    // ---------------------------------------------------------------------------
    // form controls — UA sheet gives inline-block
    // ---------------------------------------------------------------------------

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#block-level", section: "2")]
    [SpecFact]
    public void Ua_input_button_select_textarea_are_inline_block()
    {
        var engine = new StyleEngine();
        foreach (var tag in new[] { "input", "button", "select", "textarea" })
        {
            var el = InDocumentChain("html", "body", tag);
            engine.Compute(el).Get(PropertyId.Display)
                .Should().Be(new CssKeyword("inline-block"), $"<{tag}> should be display:inline-block per UA sheet");
        }
    }

    // ---------------------------------------------------------------------------
    // §2 — flow-root: block formatting context root
    // ---------------------------------------------------------------------------

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#valdef-display-flow-root", section: "2")]
    [SpecFact]
    public void Computed_flow_root_via_author_sheet()
    {
        // display:flow-root parses and computes correctly at the value level.
        // Full BFC establishment is tracked separately.
        var el = InDocumentChain("html", "body", "div");
        var engine = new StyleEngine();
        var sheet = CssParser.ParseStyleSheet("div { display: flow-root; }");
        engine.AddStyleSheet(sheet);
        engine.Compute(el).Get(PropertyId.Display).Should().Be(new CssKeyword("flow-root"));
    }

    // ---------------------------------------------------------------------------
    // Cascade: author display overrides UA default
    // ---------------------------------------------------------------------------

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#propdef-display", section: "3")]
    [SpecFact]
    public void Author_display_overrides_ua_default()
    {
        var el = InDocumentChain("html", "body", "div");
        var engine = new StyleEngine();
        var sheet = CssParser.ParseStyleSheet("div { display: flex; }");
        engine.AddStyleSheet(sheet);
        // UA sets div→block; author sets div→flex; author wins.
        engine.Compute(el).Get(PropertyId.Display).Should().Be(new CssKeyword("flex"));
    }

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#propdef-display", section: "3")]
    [SpecFact]
    public void Author_none_overrides_ua_block()
    {
        var el = InDocumentChain("html", "body", "p");
        var engine = new StyleEngine();
        var sheet = CssParser.ParseStyleSheet("p { display: none; }");
        engine.AddStyleSheet(sheet);
        engine.Compute(el).Get(PropertyId.Display).Should().Be(new CssKeyword("none"));
    }

    // ---------------------------------------------------------------------------
    // §2 — details / summary UA defaults
    // ---------------------------------------------------------------------------

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#block-level", section: "2")]
    [SpecFact]
    public void Ua_details_summary_are_block()
    {
        var engine = new StyleEngine();
        foreach (var tag in new[] { "details", "summary" })
        {
            var el = InDocumentChain("html", "body", tag);
            engine.Compute(el).Get(PropertyId.Display)
                .Should().Be(new CssKeyword("block"), $"<{tag}> should be display:block per UA sheet");
        }
    }

    // ---------------------------------------------------------------------------
    // §2 — ol / ul / dl list containers are block
    // ---------------------------------------------------------------------------

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#block-level", section: "2")]
    [SpecFact]
    public void Ua_list_containers_are_block()
    {
        var engine = new StyleEngine();
        foreach (var tag in new[] { "ul", "ol", "dl", "dt", "dd", "menu" })
        {
            var el = InDocumentChain("html", "body", tag);
            engine.Compute(el).Get(PropertyId.Display)
                .Should().Be(new CssKeyword("block"), $"<{tag}> should be display:block per UA sheet");
        }
    }

    // ---------------------------------------------------------------------------
    // §2 — hr is block
    // ---------------------------------------------------------------------------

    [Spec("css-display-3", "https://www.w3.org/TR/css-display-3/#block-level", section: "2")]
    [SpecFact]
    public void Ua_hr_is_block()
    {
        var el = InDocumentChain("html", "body", "hr");
        var engine = new StyleEngine();
        engine.Compute(el).Get(PropertyId.Display).Should().Be(new CssKeyword("block"));
    }
}
