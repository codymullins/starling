using AwesomeAssertions;
using Starling.Css.Cascade;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Dom;
using CssColorValue = Starling.Css.Values.CssColor;

namespace Starling.Css.Spec.Tests.CssLogical1;

/// <summary>
/// Conformance suite for
/// <see href="https://www.w3.org/TR/css-logical-1/">CSS Logical Properties and Values Level 1</see>.
/// Tests the logical→physical mapping produced by <see cref="StyleEngine"/>
/// across all four writing-mode/direction combinations.
/// </summary>
[TestClass]
[Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/")]
public sealed class LogicalMappingTests
{
    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static ComputedStyle Compute(string css)
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        doc.AppendChild(div);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet($"div {{ {css} }}"));
        return engine.Compute(div);
    }

    private static ComputedStyle ComputeWithParent(string parentCss, string childCss)
    {
        var doc = new Document();
        var parent = doc.CreateElement("div");
        var child = doc.CreateElement("div");
        doc.AppendChild(parent);
        parent.AppendChild(child);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet(
            $"div.parent {{ {parentCss} }} div.child {{ {childCss} }}"));
        parent.ClassList.Add("parent");
        child.ClassList.Add("child");
        return engine.Compute(child);
    }

    private static readonly CssLength Px10 = new(10, CssLengthUnit.Px);
    private static readonly CssLength Px20 = new(20, CssLengthUnit.Px);
    private static readonly CssLength Px5 = new(5, CssLengthUnit.Px);
    private static readonly CssLength Px100 = new(100, CssLengthUnit.Px);
    private static readonly CssLength Px200 = new(200, CssLengthUnit.Px);
    private static readonly CssLength Px50 = new(50, CssLengthUnit.Px);

    // =================================================================
    // §4 — Flow-relative Margins (horizontal-tb + ltr, baseline)
    // CSS Logical 1 §4: https://www.w3.org/TR/css-logical-1/#margin-properties
    // =================================================================

    /// <summary>
    /// §4 horizontal-tb + ltr: inline-start maps to left.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#margin-properties", section: "4")]
    [SpecFact]
    public void Margin_inline_start_maps_to_left_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; margin-inline-start: 10px;");
        style.GetLength(PropertyId.MarginLeft).Should().Be(Px10);
    }

    /// <summary>
    /// §4 horizontal-tb + ltr: inline-end maps to right.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#margin-properties", section: "4")]
    [SpecFact]
    public void Margin_inline_end_maps_to_right_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; margin-inline-end: 10px;");
        style.GetLength(PropertyId.MarginRight).Should().Be(Px10);
    }

    /// <summary>
    /// §4 horizontal-tb + ltr: block-start maps to top.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#margin-properties", section: "4")]
    [SpecFact]
    public void Margin_block_start_maps_to_top_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; margin-block-start: 10px;");
        style.GetLength(PropertyId.MarginTop).Should().Be(Px10);
    }

    /// <summary>
    /// §4 horizontal-tb + ltr: block-end maps to bottom.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#margin-properties", section: "4")]
    [SpecFact]
    public void Margin_block_end_maps_to_bottom_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; margin-block-end: 10px;");
        style.GetLength(PropertyId.MarginBottom).Should().Be(Px10);
    }

    // =================================================================
    // §4 — Flow-relative Margins (horizontal-tb + rtl)
    // =================================================================

    /// <summary>
    /// §4 horizontal-tb + rtl: inline-start should map to right (rtl flips inline axis).
    /// The engine currently always uses the LTR mapping, so this is pending.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#margin-properties", section: "4")]
    [SpecFact]    public void Margin_inline_start_maps_to_right_in_horizontal_tb_rtl()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: rtl; margin-inline-start: 10px;");
        style.GetLength(PropertyId.MarginRight).Should().Be(Px10);
        style.GetLength(PropertyId.MarginLeft).Should().Be(CssLength.Zero);
    }

    /// <summary>
    /// §4 horizontal-tb + rtl: inline-end should map to left.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#margin-properties", section: "4")]
    [SpecFact]    public void Margin_inline_end_maps_to_left_in_horizontal_tb_rtl()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: rtl; margin-inline-end: 10px;");
        style.GetLength(PropertyId.MarginLeft).Should().Be(Px10);
        style.GetLength(PropertyId.MarginRight).Should().Be(CssLength.Zero);
    }

    /// <summary>
    /// §4 horizontal-tb + rtl: block-start still maps to top (block axis unchanged by direction).
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#margin-properties", section: "4")]
    [SpecFact]
    public void Margin_block_start_maps_to_top_in_horizontal_tb_rtl()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: rtl; margin-block-start: 10px;");
        style.GetLength(PropertyId.MarginTop).Should().Be(Px10);
    }

    /// <summary>
    /// §4 horizontal-tb + rtl: block-end still maps to bottom.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#margin-properties", section: "4")]
    [SpecFact]
    public void Margin_block_end_maps_to_bottom_in_horizontal_tb_rtl()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: rtl; margin-block-end: 10px;");
        style.GetLength(PropertyId.MarginBottom).Should().Be(Px10);
    }

    // =================================================================
    // §4 — Flow-relative Margins (vertical-rl)
    // In vertical-rl: block axis runs right→left, inline axis runs top→bottom.
    // block-start → right, block-end → left, inline-start → top, inline-end → bottom.
    // =================================================================

    /// <summary>
    /// §4 vertical-rl: block-start should map to right.
    /// Pending: engine ignores writing-mode in logical→physical resolution.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#margin-properties", section: "4")]
    [SpecFact]    public void Margin_block_start_maps_to_right_in_vertical_rl()
    {
        var style = Compute("writing-mode: vertical-rl; direction: ltr; margin-block-start: 10px;");
        style.GetLength(PropertyId.MarginRight).Should().Be(Px10);
        style.GetLength(PropertyId.MarginTop).Should().Be(CssLength.Zero);
    }

    /// <summary>
    /// §4 vertical-rl: block-end should map to left.
    /// Pending: engine ignores writing-mode.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#margin-properties", section: "4")]
    [SpecFact]    public void Margin_block_end_maps_to_left_in_vertical_rl()
    {
        var style = Compute("writing-mode: vertical-rl; direction: ltr; margin-block-end: 10px;");
        style.GetLength(PropertyId.MarginLeft).Should().Be(Px10);
        style.GetLength(PropertyId.MarginBottom).Should().Be(CssLength.Zero);
    }

    /// <summary>
    /// §4 vertical-rl: inline-start should map to top.
    /// Pending: engine ignores writing-mode.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#margin-properties", section: "4")]
    [SpecFact]    public void Margin_inline_start_maps_to_top_in_vertical_rl()
    {
        var style = Compute("writing-mode: vertical-rl; direction: ltr; margin-inline-start: 10px;");
        style.GetLength(PropertyId.MarginTop).Should().Be(Px10);
        style.GetLength(PropertyId.MarginLeft).Should().Be(CssLength.Zero);
    }

    /// <summary>
    /// §4 vertical-rl: inline-end should map to bottom.
    /// Pending: engine ignores writing-mode.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#margin-properties", section: "4")]
    [SpecFact]    public void Margin_inline_end_maps_to_bottom_in_vertical_rl()
    {
        var style = Compute("writing-mode: vertical-rl; direction: ltr; margin-inline-end: 10px;");
        style.GetLength(PropertyId.MarginBottom).Should().Be(Px10);
        style.GetLength(PropertyId.MarginRight).Should().Be(CssLength.Zero);
    }

    // =================================================================
    // §4 — Flow-relative Margins (vertical-lr)
    // In vertical-lr: block axis runs left→right, inline axis runs top→bottom.
    // block-start → left, block-end → right, inline-start → top, inline-end → bottom.
    // =================================================================

    /// <summary>
    /// §4 vertical-lr: block-start should map to left.
    /// Pending: engine ignores writing-mode.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#margin-properties", section: "4")]
    [SpecFact]    public void Margin_block_start_maps_to_left_in_vertical_lr()
    {
        var style = Compute("writing-mode: vertical-lr; direction: ltr; margin-block-start: 10px;");
        style.GetLength(PropertyId.MarginLeft).Should().Be(Px10);
        style.GetLength(PropertyId.MarginTop).Should().Be(CssLength.Zero);
    }

    /// <summary>
    /// §4 vertical-lr: block-end should map to right.
    /// Pending: engine ignores writing-mode.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#margin-properties", section: "4")]
    [SpecFact]    public void Margin_block_end_maps_to_right_in_vertical_lr()
    {
        var style = Compute("writing-mode: vertical-lr; direction: ltr; margin-block-end: 10px;");
        style.GetLength(PropertyId.MarginRight).Should().Be(Px10);
        style.GetLength(PropertyId.MarginBottom).Should().Be(CssLength.Zero);
    }

    // =================================================================
    // §4 — Flow-relative Padding (horizontal-tb + ltr)
    // =================================================================

    /// <summary>
    /// §4 horizontal-tb + ltr: padding-inline-start maps to padding-left.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#padding-properties", section: "4")]
    [SpecFact]
    public void Padding_inline_start_maps_to_left_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; padding-inline-start: 10px;");
        style.GetLength(PropertyId.PaddingLeft).Should().Be(Px10);
    }

    /// <summary>
    /// §4 horizontal-tb + ltr: padding-inline-end maps to padding-right.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#padding-properties", section: "4")]
    [SpecFact]
    public void Padding_inline_end_maps_to_right_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; padding-inline-end: 10px;");
        style.GetLength(PropertyId.PaddingRight).Should().Be(Px10);
    }

    /// <summary>
    /// §4 horizontal-tb + ltr: padding-block-start maps to padding-top.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#padding-properties", section: "4")]
    [SpecFact]
    public void Padding_block_start_maps_to_top_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; padding-block-start: 10px;");
        style.GetLength(PropertyId.PaddingTop).Should().Be(Px10);
    }

    /// <summary>
    /// §4 horizontal-tb + ltr: padding-block-end maps to padding-bottom.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#padding-properties", section: "4")]
    [SpecFact]
    public void Padding_block_end_maps_to_bottom_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; padding-block-end: 10px;");
        style.GetLength(PropertyId.PaddingBottom).Should().Be(Px10);
    }

    // =================================================================
    // §4 — Flow-relative Padding (horizontal-tb + rtl)
    // =================================================================

    /// <summary>
    /// §4 horizontal-tb + rtl: padding-inline-start should map to padding-right.
    /// Pending: engine does not honour direction.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#padding-properties", section: "4")]
    [SpecFact]    public void Padding_inline_start_maps_to_right_in_horizontal_tb_rtl()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: rtl; padding-inline-start: 10px;");
        style.GetLength(PropertyId.PaddingRight).Should().Be(Px10);
        style.GetLength(PropertyId.PaddingLeft).Should().Be(CssLength.Zero);
    }

    /// <summary>
    /// §4 horizontal-tb + rtl: padding-inline-end should map to padding-left.
    /// Pending: engine does not honour direction.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#padding-properties", section: "4")]
    [SpecFact]    public void Padding_inline_end_maps_to_left_in_horizontal_tb_rtl()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: rtl; padding-inline-end: 10px;");
        style.GetLength(PropertyId.PaddingLeft).Should().Be(Px10);
        style.GetLength(PropertyId.PaddingRight).Should().Be(CssLength.Zero);
    }

    // =================================================================
    // §4 — Flow-relative Padding (vertical-rl)
    // =================================================================

    /// <summary>
    /// §4 vertical-rl: padding-block-start should map to padding-right.
    /// Pending: engine does not honour writing-mode.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#padding-properties", section: "4")]
    [SpecFact]    public void Padding_block_start_maps_to_right_in_vertical_rl()
    {
        var style = Compute("writing-mode: vertical-rl; padding-block-start: 10px;");
        style.GetLength(PropertyId.PaddingRight).Should().Be(Px10);
        style.GetLength(PropertyId.PaddingTop).Should().Be(CssLength.Zero);
    }

    /// <summary>
    /// §4 vertical-rl: padding-inline-start should map to padding-top.
    /// Pending: engine does not honour writing-mode.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#padding-properties", section: "4")]
    [SpecFact]    public void Padding_inline_start_maps_to_top_in_vertical_rl()
    {
        var style = Compute("writing-mode: vertical-rl; padding-inline-start: 10px;");
        style.GetLength(PropertyId.PaddingTop).Should().Be(Px10);
        style.GetLength(PropertyId.PaddingLeft).Should().Be(CssLength.Zero);
    }

    // =================================================================
    // §4 — Flow-relative Padding (vertical-lr)
    // =================================================================

    /// <summary>
    /// §4 vertical-lr: padding-block-start should map to padding-left.
    /// Pending: engine does not honour writing-mode.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#padding-properties", section: "4")]
    [SpecFact]    public void Padding_block_start_maps_to_left_in_vertical_lr()
    {
        var style = Compute("writing-mode: vertical-lr; padding-block-start: 10px;");
        style.GetLength(PropertyId.PaddingLeft).Should().Be(Px10);
        style.GetLength(PropertyId.PaddingTop).Should().Be(CssLength.Zero);
    }

    // =================================================================
    // §4 — Flow-relative Border Width (horizontal-tb + ltr)
    // =================================================================

    /// <summary>
    /// §4 horizontal-tb + ltr: border-inline-start-width maps to border-left-width.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#border-width", section: "4")]
    [SpecFact]
    public void Border_inline_start_width_maps_to_left_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; border-inline-start-width: 5px; border-inline-start-style: solid;");
        style.GetLength(PropertyId.BorderLeftWidth).Should().Be(Px5);
    }

    /// <summary>
    /// §4 horizontal-tb + ltr: border-inline-end-width maps to border-right-width.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#border-width", section: "4")]
    [SpecFact]
    public void Border_inline_end_width_maps_to_right_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; border-inline-end-width: 5px; border-inline-end-style: solid;");
        style.GetLength(PropertyId.BorderRightWidth).Should().Be(Px5);
    }

    /// <summary>
    /// §4 horizontal-tb + ltr: border-block-start-width maps to border-top-width.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#border-width", section: "4")]
    [SpecFact]
    public void Border_block_start_width_maps_to_top_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; border-block-start-width: 5px; border-block-start-style: solid;");
        style.GetLength(PropertyId.BorderTopWidth).Should().Be(Px5);
    }

    /// <summary>
    /// §4 horizontal-tb + ltr: border-block-end-width maps to border-bottom-width.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#border-width", section: "4")]
    [SpecFact]
    public void Border_block_end_width_maps_to_bottom_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; border-block-end-width: 5px; border-block-end-style: solid;");
        style.GetLength(PropertyId.BorderBottomWidth).Should().Be(Px5);
    }

    // =================================================================
    // §4 — Flow-relative Border Style (horizontal-tb + ltr)
    // =================================================================

    /// <summary>
    /// §4 horizontal-tb + ltr: border-inline-start-style maps to border-left-style.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#border-style", section: "4")]
    [SpecFact]
    public void Border_inline_start_style_maps_to_left_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; border-inline-start-style: dashed;");
        style.Get(PropertyId.BorderLeftStyle).Should().Be(new CssKeyword("dashed"));
    }

    /// <summary>
    /// §4 horizontal-tb + ltr: border-block-start-style maps to border-top-style.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#border-style", section: "4")]
    [SpecFact]
    public void Border_block_start_style_maps_to_top_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; border-block-start-style: dotted;");
        style.Get(PropertyId.BorderTopStyle).Should().Be(new CssKeyword("dotted"));
    }

    /// <summary>
    /// §4 horizontal-tb + ltr: border-inline-end-style maps to border-right-style.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#border-style", section: "4")]
    [SpecFact]
    public void Border_inline_end_style_maps_to_right_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; border-inline-end-style: solid;");
        style.Get(PropertyId.BorderRightStyle).Should().Be(new CssKeyword("solid"));
    }

    /// <summary>
    /// §4 horizontal-tb + ltr: border-block-end-style maps to border-bottom-style.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#border-style", section: "4")]
    [SpecFact]
    public void Border_block_end_style_maps_to_bottom_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; border-block-end-style: groove;");
        style.Get(PropertyId.BorderBottomStyle).Should().Be(new CssKeyword("groove"));
    }

    // =================================================================
    // §4 — Flow-relative Border Style (vertical-rl, pending)
    // =================================================================

    /// <summary>
    /// §4 vertical-rl: border-block-start-style should map to border-right-style.
    /// Pending: engine does not honour writing-mode.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#border-style", section: "4")]
    [SpecFact]    public void Border_block_start_style_maps_to_right_in_vertical_rl()
    {
        var style = Compute("writing-mode: vertical-rl; border-block-start-style: solid;");
        style.Get(PropertyId.BorderRightStyle).Should().Be(new CssKeyword("solid"));
        style.Get(PropertyId.BorderTopStyle).Should().Be(new CssKeyword("none"));
    }

    /// <summary>
    /// §4 vertical-rl: border-inline-start-style should map to border-top-style.
    /// Pending: engine does not honour writing-mode.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#border-style", section: "4")]
    [SpecFact]    public void Border_inline_start_style_maps_to_top_in_vertical_rl()
    {
        var style = Compute("writing-mode: vertical-rl; border-inline-start-style: dashed;");
        style.Get(PropertyId.BorderTopStyle).Should().Be(new CssKeyword("dashed"));
        style.Get(PropertyId.BorderLeftStyle).Should().Be(new CssKeyword("none"));
    }

    // =================================================================
    // §4 — Flow-relative Border Color (horizontal-tb + ltr)
    // =================================================================

    /// <summary>
    /// §4 horizontal-tb + ltr: border-inline-start-color maps to border-left-color.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#border-color", section: "4")]
    [SpecFact]
    public void Border_inline_start_color_maps_to_left_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; border-inline-start-color: red;");
        style.Get(PropertyId.BorderLeftColor).Should().Be(new CssColorValue(255, 0, 0));
    }

    /// <summary>
    /// §4 horizontal-tb + ltr: border-block-start-color maps to border-top-color.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#border-color", section: "4")]
    [SpecFact]
    public void Border_block_start_color_maps_to_top_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; border-block-start-color: blue;");
        style.Get(PropertyId.BorderTopColor).Should().Be(new CssColorValue(0, 0, 255));
    }

    // =================================================================
    // §4 — Logical Sizing (horizontal-tb: inline-size→width, block-size→height)
    // =================================================================

    /// <summary>
    /// §4 horizontal-tb: inline-size maps to width.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#dimension-properties", section: "4")]
    [SpecFact]
    public void Inline_size_maps_to_width_in_horizontal_tb()
    {
        var style = Compute("writing-mode: horizontal-tb; inline-size: 100px;");
        style.Get(PropertyId.Width).Should().Be(Px100);
    }

    /// <summary>
    /// §4 horizontal-tb: block-size maps to height.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#dimension-properties", section: "4")]
    [SpecFact]
    public void Block_size_maps_to_height_in_horizontal_tb()
    {
        var style = Compute("writing-mode: horizontal-tb; block-size: 200px;");
        style.Get(PropertyId.Height).Should().Be(Px200);
    }

    /// <summary>
    /// §4 horizontal-tb: min-inline-size maps to min-width.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#dimension-properties", section: "4")]
    [SpecFact]
    public void Min_inline_size_maps_to_min_width_in_horizontal_tb()
    {
        var style = Compute("writing-mode: horizontal-tb; min-inline-size: 50px;");
        style.Get(PropertyId.MinWidth).Should().Be(Px50);
    }

    /// <summary>
    /// §4 horizontal-tb: min-block-size maps to min-height.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#dimension-properties", section: "4")]
    [SpecFact]
    public void Min_block_size_maps_to_min_height_in_horizontal_tb()
    {
        var style = Compute("writing-mode: horizontal-tb; min-block-size: 50px;");
        style.Get(PropertyId.MinHeight).Should().Be(Px50);
    }

    /// <summary>
    /// §4 horizontal-tb: max-inline-size maps to max-width.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#dimension-properties", section: "4")]
    [SpecFact]
    public void Max_inline_size_maps_to_max_width_in_horizontal_tb()
    {
        var style = Compute("writing-mode: horizontal-tb; max-inline-size: 200px;");
        style.Get(PropertyId.MaxWidth).Should().Be(Px200);
    }

    /// <summary>
    /// §4 horizontal-tb: max-block-size maps to max-height.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#dimension-properties", section: "4")]
    [SpecFact]
    public void Max_block_size_maps_to_max_height_in_horizontal_tb()
    {
        var style = Compute("writing-mode: horizontal-tb; max-block-size: 200px;");
        style.Get(PropertyId.MaxHeight).Should().Be(Px200);
    }

    // =================================================================
    // §4 — Logical Sizing (vertical-rl/lr: inline-size→height, block-size→width)
    // =================================================================

    /// <summary>
    /// §4 vertical-rl: inline-size should map to height (axes swapped).
    /// Pending: engine always maps inline-size→width regardless of writing-mode.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#dimension-properties", section: "4")]
    [SpecFact]    public void Inline_size_maps_to_height_in_vertical_rl()
    {
        var style = Compute("writing-mode: vertical-rl; inline-size: 100px;");
        style.Get(PropertyId.Height).Should().Be(Px100);
        style.Get(PropertyId.Width).Should().Be(new CssKeyword("auto"));
    }

    /// <summary>
    /// §4 vertical-rl: block-size should map to width (axes swapped).
    /// Pending: engine always maps block-size→height regardless of writing-mode.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#dimension-properties", section: "4")]
    [SpecFact]    public void Block_size_maps_to_width_in_vertical_rl()
    {
        var style = Compute("writing-mode: vertical-rl; block-size: 200px;");
        style.Get(PropertyId.Width).Should().Be(Px200);
        style.Get(PropertyId.Height).Should().Be(new CssKeyword("auto"));
    }

    /// <summary>
    /// §4 vertical-lr: inline-size should map to height.
    /// Pending: engine always maps inline-size→width.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#dimension-properties", section: "4")]
    [SpecFact]    public void Inline_size_maps_to_height_in_vertical_lr()
    {
        var style = Compute("writing-mode: vertical-lr; inline-size: 100px;");
        style.Get(PropertyId.Height).Should().Be(Px100);
        style.Get(PropertyId.Width).Should().Be(new CssKeyword("auto"));
    }

    /// <summary>
    /// §4 vertical-lr: block-size should map to width.
    /// Pending: engine always maps block-size→height.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#dimension-properties", section: "4")]
    [SpecFact]    public void Block_size_maps_to_width_in_vertical_lr()
    {
        var style = Compute("writing-mode: vertical-lr; block-size: 200px;");
        style.Get(PropertyId.Width).Should().Be(Px200);
        style.Get(PropertyId.Height).Should().Be(new CssKeyword("auto"));
    }

    // =================================================================
    // §4 — Logical Insets (horizontal-tb + ltr)
    // =================================================================

    /// <summary>
    /// §4 horizontal-tb + ltr: inset-inline-start maps to left.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#inset-properties", section: "4")]
    [SpecFact]
    public void Inset_inline_start_maps_to_left_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; position: absolute; inset-inline-start: 10px;");
        style.Get(PropertyId.Left).Should().Be(Px10);
    }

    /// <summary>
    /// §4 horizontal-tb + ltr: inset-inline-end maps to right.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#inset-properties", section: "4")]
    [SpecFact]
    public void Inset_inline_end_maps_to_right_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; position: absolute; inset-inline-end: 10px;");
        style.Get(PropertyId.Right).Should().Be(Px10);
    }

    /// <summary>
    /// §4 horizontal-tb + ltr: inset-block-start maps to top.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#inset-properties", section: "4")]
    [SpecFact]
    public void Inset_block_start_maps_to_top_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; position: absolute; inset-block-start: 10px;");
        style.Get(PropertyId.Top).Should().Be(Px10);
    }

    /// <summary>
    /// §4 horizontal-tb + ltr: inset-block-end maps to bottom.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#inset-properties", section: "4")]
    [SpecFact]
    public void Inset_block_end_maps_to_bottom_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; position: absolute; inset-block-end: 10px;");
        style.Get(PropertyId.Bottom).Should().Be(Px10);
    }

    // =================================================================
    // §4 — Logical Insets (horizontal-tb + rtl, pending)
    // =================================================================

    /// <summary>
    /// §4 horizontal-tb + rtl: inset-inline-start should map to right.
    /// Pending: engine does not honour direction.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#inset-properties", section: "4")]
    [SpecFact]    public void Inset_inline_start_maps_to_right_in_horizontal_tb_rtl()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: rtl; position: absolute; inset-inline-start: 10px;");
        style.Get(PropertyId.Right).Should().Be(Px10);
        style.Get(PropertyId.Left).Should().Be(new CssKeyword("auto"));
    }

    /// <summary>
    /// §4 horizontal-tb + rtl: inset-inline-end should map to left.
    /// Pending: engine does not honour direction.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#inset-properties", section: "4")]
    [SpecFact]    public void Inset_inline_end_maps_to_left_in_horizontal_tb_rtl()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: rtl; position: absolute; inset-inline-end: 10px;");
        style.Get(PropertyId.Left).Should().Be(Px10);
        style.Get(PropertyId.Right).Should().Be(new CssKeyword("auto"));
    }

    // =================================================================
    // §4 — Logical Insets (vertical-rl, pending)
    // =================================================================

    /// <summary>
    /// §4 vertical-rl: inset-block-start should map to right.
    /// Pending: engine does not honour writing-mode.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#inset-properties", section: "4")]
    [SpecFact]    public void Inset_block_start_maps_to_right_in_vertical_rl()
    {
        var style = Compute("writing-mode: vertical-rl; position: absolute; inset-block-start: 10px;");
        style.Get(PropertyId.Right).Should().Be(Px10);
        style.Get(PropertyId.Top).Should().Be(new CssKeyword("auto"));
    }

    /// <summary>
    /// §4 vertical-rl: inset-inline-start should map to top.
    /// Pending: engine does not honour writing-mode.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#inset-properties", section: "4")]
    [SpecFact]    public void Inset_inline_start_maps_to_top_in_vertical_rl()
    {
        var style = Compute("writing-mode: vertical-rl; position: absolute; inset-inline-start: 10px;");
        style.Get(PropertyId.Top).Should().Be(Px10);
        style.Get(PropertyId.Left).Should().Be(new CssKeyword("auto"));
    }

    // =================================================================
    // §3 — Logical border shorthands (border-inline, border-block)
    // =================================================================

    /// <summary>
    /// §3 border-inline shorthand sets both inline start and end border longhands.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#propdef-border-inline", section: "3")]
    [SpecFact]
    public void Border_inline_shorthand_sets_start_and_end_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; border-inline: 3px solid;");
        style.GetLength(PropertyId.BorderLeftWidth).Should().Be(new CssLength(3, CssLengthUnit.Px));
        style.GetLength(PropertyId.BorderRightWidth).Should().Be(new CssLength(3, CssLengthUnit.Px));
        style.Get(PropertyId.BorderLeftStyle).Should().Be(new CssKeyword("solid"));
        style.Get(PropertyId.BorderRightStyle).Should().Be(new CssKeyword("solid"));
    }

    /// <summary>
    /// §3 border-block shorthand sets both block start and end border longhands.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#propdef-border-block", section: "3")]
    [SpecFact]
    public void Border_block_shorthand_sets_start_and_end_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; border-block: 2px dashed;");
        style.GetLength(PropertyId.BorderTopWidth).Should().Be(new CssLength(2, CssLengthUnit.Px));
        style.GetLength(PropertyId.BorderBottomWidth).Should().Be(new CssLength(2, CssLengthUnit.Px));
        style.Get(PropertyId.BorderTopStyle).Should().Be(new CssKeyword("dashed"));
        style.Get(PropertyId.BorderBottomStyle).Should().Be(new CssKeyword("dashed"));
    }

    /// <summary>
    /// §3 border-inline-start shorthand sets width/style/color for the logical inline-start edge.
    /// In horizontal-tb + ltr this resolves to the left physical border.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#propdef-border-inline-start", section: "3")]
    [SpecFact]
    public void Border_inline_start_shorthand_maps_to_left_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; border-inline-start: 4px dotted blue;");
        style.GetLength(PropertyId.BorderLeftWidth).Should().Be(new CssLength(4, CssLengthUnit.Px));
        style.Get(PropertyId.BorderLeftStyle).Should().Be(new CssKeyword("dotted"));
        style.Get(PropertyId.BorderLeftColor).Should().Be(new CssColorValue(0, 0, 255));
    }

    /// <summary>
    /// §3 border-block-start shorthand maps to top in horizontal-tb.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#propdef-border-block-start", section: "3")]
    [SpecFact]
    public void Border_block_start_shorthand_maps_to_top_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; border-block-start: 1px solid red;");
        style.GetLength(PropertyId.BorderTopWidth).Should().Be(new CssLength(1, CssLengthUnit.Px));
        style.Get(PropertyId.BorderTopStyle).Should().Be(new CssKeyword("solid"));
        style.Get(PropertyId.BorderTopColor).Should().Be(new CssColorValue(255, 0, 0));
    }

    // =================================================================
    // §4 — Two-value flow-relative shorthands
    // =================================================================

    /// <summary>
    /// §4 margin-inline two-value shorthand: start and end get different values.
    /// In horizontal-tb + ltr: start→left, end→right.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#propdef-margin-inline", section: "4")]
    [SpecFact]
    public void Margin_inline_two_value_shorthand_maps_to_left_and_right_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; margin-inline: 10px 20px;");
        style.GetLength(PropertyId.MarginLeft).Should().Be(Px10);
        style.GetLength(PropertyId.MarginRight).Should().Be(Px20);
    }

    /// <summary>
    /// §4 margin-block two-value shorthand: start and end get different values.
    /// In horizontal-tb: start→top, end→bottom.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#propdef-margin-block", section: "4")]
    [SpecFact]
    public void Margin_block_two_value_shorthand_maps_to_top_and_bottom_in_horizontal_tb()
    {
        var style = Compute("writing-mode: horizontal-tb; margin-block: 10px 20px;");
        style.GetLength(PropertyId.MarginTop).Should().Be(Px10);
        style.GetLength(PropertyId.MarginBottom).Should().Be(Px20);
    }

    /// <summary>
    /// §4 padding-inline two-value shorthand: start and end values map to left and right.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#propdef-padding-inline", section: "4")]
    [SpecFact]
    public void Padding_inline_two_value_shorthand_maps_to_left_and_right_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; padding-inline: 10px 20px;");
        style.GetLength(PropertyId.PaddingLeft).Should().Be(Px10);
        style.GetLength(PropertyId.PaddingRight).Should().Be(Px20);
    }

    /// <summary>
    /// §4 padding-block two-value shorthand: start and end values map to top and bottom.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#propdef-padding-block", section: "4")]
    [SpecFact]
    public void Padding_block_two_value_shorthand_maps_to_top_and_bottom_in_horizontal_tb()
    {
        var style = Compute("writing-mode: horizontal-tb; padding-block: 10px 20px;");
        style.GetLength(PropertyId.PaddingTop).Should().Be(Px10);
        style.GetLength(PropertyId.PaddingBottom).Should().Be(Px20);
    }

    /// <summary>
    /// §4 inset-inline two-value shorthand: start and end map to left and right in ltr.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#propdef-inset-inline", section: "4")]
    [SpecFact]
    public void Inset_inline_two_value_shorthand_maps_to_left_and_right_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; position: absolute; inset-inline: 10px 20px;");
        style.Get(PropertyId.Left).Should().Be(Px10);
        style.Get(PropertyId.Right).Should().Be(Px20);
    }

    /// <summary>
    /// §4 inset-block two-value shorthand: start and end map to top and bottom.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#propdef-inset-block", section: "4")]
    [SpecFact]
    public void Inset_block_two_value_shorthand_maps_to_top_and_bottom_in_horizontal_tb()
    {
        var style = Compute("writing-mode: horizontal-tb; position: absolute; inset-block: 10px 20px;");
        style.Get(PropertyId.Top).Should().Be(Px10);
        style.Get(PropertyId.Bottom).Should().Be(Px20);
    }

    // =================================================================
    // §4 — Two-value shorthands (rtl, pending)
    // =================================================================

    /// <summary>
    /// §4 margin-inline two-value + rtl: start→right, end→left.
    /// Pending: engine does not honour direction.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#propdef-margin-inline", section: "4")]
    [SpecFact]    public void Margin_inline_two_value_shorthand_maps_start_to_right_in_rtl()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: rtl; margin-inline: 10px 20px;");
        style.GetLength(PropertyId.MarginRight).Should().Be(Px10);
        style.GetLength(PropertyId.MarginLeft).Should().Be(Px20);
    }

    // =================================================================
    // §4 — Cascade interaction: logical wins when declared later
    // =================================================================

    /// <summary>
    /// §4 cascade: when logical and physical properties have the same specificity,
    /// declaration order determines the winner. The later declaration wins.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#logical-physical-longhands", section: "4")]
    [SpecFact]
    public void Logical_declared_after_physical_wins_cascade()
    {
        var style = Compute("margin-left: 5px; margin-inline-start: 10px;");
        style.GetLength(PropertyId.MarginLeft).Should().Be(Px10);
    }

    /// <summary>
    /// §4 cascade: when physical is declared after logical, physical wins.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#logical-physical-longhands", section: "4")]
    [SpecFact]
    public void Physical_declared_after_logical_wins_cascade()
    {
        var style = Compute("margin-inline-start: 10px; margin-left: 5px;");
        style.GetLength(PropertyId.MarginLeft).Should().Be(Px5);
    }

    // =================================================================
    // §4 — Initial values for logical properties
    // =================================================================

    /// <summary>
    /// §4 logical properties have initial value 0 (margins and padding).
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#margin-properties", section: "4")]
    [SpecFact]
    public void Logical_margin_initial_values_are_zero()
    {
        var style = Compute("color: red;");
        PropertyRegistry.InitialValue(PropertyId.MarginInlineStart).Should().Be(CssLength.Zero);
        PropertyRegistry.InitialValue(PropertyId.MarginInlineEnd).Should().Be(CssLength.Zero);
        PropertyRegistry.InitialValue(PropertyId.MarginBlockStart).Should().Be(CssLength.Zero);
        PropertyRegistry.InitialValue(PropertyId.MarginBlockEnd).Should().Be(CssLength.Zero);
    }

    /// <summary>
    /// §4 logical padding initial values are zero.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#padding-properties", section: "4")]
    [SpecFact]
    public void Logical_padding_initial_values_are_zero()
    {
        PropertyRegistry.InitialValue(PropertyId.PaddingInlineStart).Should().Be(CssLength.Zero);
        PropertyRegistry.InitialValue(PropertyId.PaddingInlineEnd).Should().Be(CssLength.Zero);
        PropertyRegistry.InitialValue(PropertyId.PaddingBlockStart).Should().Be(CssLength.Zero);
        PropertyRegistry.InitialValue(PropertyId.PaddingBlockEnd).Should().Be(CssLength.Zero);
    }

    /// <summary>
    /// §4 logical inset initial values are auto.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#inset-properties", section: "4")]
    [SpecFact]
    public void Logical_inset_initial_values_are_auto()
    {
        PropertyRegistry.InitialValue(PropertyId.InsetInlineStart).Should().Be(new CssKeyword("auto"));
        PropertyRegistry.InitialValue(PropertyId.InsetInlineEnd).Should().Be(new CssKeyword("auto"));
        PropertyRegistry.InitialValue(PropertyId.InsetBlockStart).Should().Be(new CssKeyword("auto"));
        PropertyRegistry.InitialValue(PropertyId.InsetBlockEnd).Should().Be(new CssKeyword("auto"));
    }

    /// <summary>
    /// §4 logical size initial values are auto.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#dimension-properties", section: "4")]
    [SpecFact]
    public void Logical_size_initial_values_are_auto()
    {
        PropertyRegistry.InitialValue(PropertyId.InlineSize).Should().Be(new CssKeyword("auto"));
        PropertyRegistry.InitialValue(PropertyId.BlockSize).Should().Be(new CssKeyword("auto"));
    }

    /// <summary>
    /// §4 min-inline-size and min-block-size initial values are 0.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#dimension-properties", section: "4")]
    [SpecFact]
    public void Logical_min_size_initial_values_are_zero()
    {
        PropertyRegistry.InitialValue(PropertyId.MinInlineSize).Should().Be(CssLength.Zero);
        PropertyRegistry.InitialValue(PropertyId.MinBlockSize).Should().Be(CssLength.Zero);
    }

    /// <summary>
    /// §4 max-inline-size and max-block-size initial values are none.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#dimension-properties", section: "4")]
    [SpecFact]
    public void Logical_max_size_initial_values_are_none()
    {
        PropertyRegistry.InitialValue(PropertyId.MaxInlineSize).Should().Be(new CssKeyword("none"));
        PropertyRegistry.InitialValue(PropertyId.MaxBlockSize).Should().Be(new CssKeyword("none"));
    }

    // =================================================================
    // §4 — Corner radius mapping (horizontal-tb + ltr)
    // border-start-start-radius → border-top-left-radius, etc.
    // =================================================================

    /// <summary>
    /// §4 horizontal-tb + ltr: border-start-start-radius maps to border-top-left-radius.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#border-radius-properties", section: "4")]
    [SpecFact]
    public void Border_start_start_radius_maps_to_top_left_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; border-start-start-radius: 8px;");
        style.GetLength(PropertyId.BorderTopLeftRadius).Should().Be(new CssLength(8, CssLengthUnit.Px));
    }

    /// <summary>
    /// §4 horizontal-tb + ltr: border-start-end-radius maps to border-top-right-radius.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#border-radius-properties", section: "4")]
    [SpecFact]
    public void Border_start_end_radius_maps_to_top_right_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; border-start-end-radius: 8px;");
        style.GetLength(PropertyId.BorderTopRightRadius).Should().Be(new CssLength(8, CssLengthUnit.Px));
    }

    /// <summary>
    /// §4 horizontal-tb + ltr: border-end-start-radius maps to border-bottom-left-radius.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#border-radius-properties", section: "4")]
    [SpecFact]
    public void Border_end_start_radius_maps_to_bottom_left_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; border-end-start-radius: 8px;");
        style.GetLength(PropertyId.BorderBottomLeftRadius).Should().Be(new CssLength(8, CssLengthUnit.Px));
    }

    /// <summary>
    /// §4 horizontal-tb + ltr: border-end-end-radius maps to border-bottom-right-radius.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#border-radius-properties", section: "4")]
    [SpecFact]
    public void Border_end_end_radius_maps_to_bottom_right_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; border-end-end-radius: 8px;");
        style.GetLength(PropertyId.BorderBottomRightRadius).Should().Be(new CssLength(8, CssLengthUnit.Px));
    }

    // =================================================================
    // §4 — Corner radius mapping (vertical-rl, pending)
    // In vertical-rl + ltr: block-start=right, inline-start=top.
    // border-start-start-radius → border-top-right-radius.
    // =================================================================

    /// <summary>
    /// §4 vertical-rl + ltr: border-start-start-radius should map to border-top-right-radius.
    /// Pending: engine does not honour writing-mode for corner radii.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#border-radius-properties", section: "4")]
    [SpecFact]    public void Border_start_start_radius_maps_to_top_right_in_vertical_rl_ltr()
    {
        var style = Compute("writing-mode: vertical-rl; direction: ltr; border-start-start-radius: 8px;");
        style.GetLength(PropertyId.BorderTopRightRadius).Should().Be(new CssLength(8, CssLengthUnit.Px));
        style.GetLength(PropertyId.BorderTopLeftRadius).Should().Be(CssLength.Zero);
    }

    // =================================================================
    // §4 — border-inline-width / border-block-width shorthands
    // =================================================================

    /// <summary>
    /// §4 border-inline-width two-value shorthand sets inline start and end widths.
    /// In horizontal-tb + ltr: start→left, end→right.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#propdef-border-inline-width", section: "4")]
    [SpecFact]
    public void Border_inline_width_shorthand_maps_to_left_and_right_in_horizontal_tb_ltr()
    {
        var style = Compute("writing-mode: horizontal-tb; direction: ltr; border-inline-width: 3px 5px;");
        style.GetLength(PropertyId.BorderLeftWidth).Should().Be(new CssLength(3, CssLengthUnit.Px));
        style.GetLength(PropertyId.BorderRightWidth).Should().Be(new CssLength(5, CssLengthUnit.Px));
    }

    /// <summary>
    /// §4 border-block-width two-value shorthand sets block start and end widths.
    /// In horizontal-tb: start→top, end→bottom.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#propdef-border-block-width", section: "4")]
    [SpecFact]
    public void Border_block_width_shorthand_maps_to_top_and_bottom_in_horizontal_tb()
    {
        var style = Compute("writing-mode: horizontal-tb; border-block-width: 3px 5px;");
        style.GetLength(PropertyId.BorderTopWidth).Should().Be(new CssLength(3, CssLengthUnit.Px));
        style.GetLength(PropertyId.BorderBottomWidth).Should().Be(new CssLength(5, CssLengthUnit.Px));
    }

    // =================================================================
    // §4 — Writing-mode is inherited (confirms §2 of CSS Writing Modes 4)
    // =================================================================

    /// <summary>
    /// §2 writing-mode is an inherited property so a child sees the parent value.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#writing-modes", section: "2")]
    [SpecFact]
    public void Writing_mode_is_inherited()
        => PropertyRegistry.Inherits(PropertyId.WritingMode).Should().BeTrue();

    /// <summary>
    /// §2 direction is an inherited property.
    /// </summary>
    [Spec("css-logical-1", "https://www.w3.org/TR/css-logical-1/#writing-modes", section: "2")]
    [SpecFact]
    public void Direction_is_inherited()
        => PropertyRegistry.Inherits(PropertyId.Direction).Should().BeTrue();
}
