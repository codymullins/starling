using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssMasking1;

/// <summary>
/// Property parsing + cascade conformance for
/// <see href="https://www.w3.org/TR/css-masking-1/">CSS Masking 1</see>.
/// </summary>
[TestClass]
[Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/")]
public sealed class MaskingTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    private static CssValue Single(string css, PropertyId id)
        => Expand(css).Single(d => d.Id == id).Value;

    // ----- clip-path (§6.1) ---------------------------------------------

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-clip-path", section: "6.1")]
    [SpecFact]
    public void Clip_path_none_parses()
        => Single("clip-path: none;", PropertyId.ClipPath).Should().Be(new CssKeyword("none"));

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-clip-path", section: "6.1")]
    [SpecFact]
    public void Clip_path_url_reference_parses()
        => Single("clip-path: url(#m);", PropertyId.ClipPath).Should().Be(new CssUrl("#m"));

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-clip-path", section: "6.1")]
    [SpecFact]
    public void Clip_path_circle_basic_shape_parses_as_function()
    {
        var value = Single("clip-path: circle(50%);", PropertyId.ClipPath);
        value.Should().BeOfType<CssFunctionValue>();
        ((CssFunctionValue)value).Name.Should().Be("circle");
    }

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-clip-path", section: "6.1")]
    [SpecFact]
    public void Clip_path_inset_basic_shape_parses_as_function()
    {
        var value = Single("clip-path: inset(10px 20px 30px 40px);", PropertyId.ClipPath);
        value.Should().BeOfType<CssFunctionValue>();
        ((CssFunctionValue)value).Name.Should().Be("inset");
    }

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-clip-path", section: "6.1")]
    [SpecFact]
    public void Clip_path_polygon_basic_shape_parses_as_function()
    {
        var value = Single("clip-path: polygon(0% 0%, 100% 0%, 100% 100%);", PropertyId.ClipPath);
        value.Should().BeOfType<CssFunctionValue>();
        ((CssFunctionValue)value).Name.Should().Be("polygon");
    }

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-clip-path", section: "6.1")]
    [SpecFact]
    public void Clip_path_geometry_box_border_box_keyword_parses()
        => Single("clip-path: border-box;", PropertyId.ClipPath).Should().Be(new CssKeyword("border-box"));

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-clip-path", section: "6.1")]
    [SpecFact]
    public void Clip_path_geometry_box_margin_box_keyword_parses()
        => Single("clip-path: margin-box;", PropertyId.ClipPath).Should().Be(new CssKeyword("margin-box"));

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-clip-path", section: "6.1")]
    [SpecFact]
    public void Clip_path_initial_value_is_none()
        => PropertyRegistry.InitialValue(PropertyId.ClipPath).Should().Be(new CssKeyword("none"));

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-clip-path", section: "6.1")]
    [SpecFact]
    public void Clip_path_is_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.ClipPath).Should().BeFalse();

    // ----- mask-image (§7.5.1) ------------------------------------------

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-mask-image", section: "7.5.1")]
    [SpecFact]
    public void Mask_image_none_parses()
        => Single("mask-image: none;", PropertyId.MaskImage).Should().Be(new CssKeyword("none"));

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-mask-image", section: "7.5.1")]
    [SpecFact]
    public void Mask_image_url_parses()
        => Single("mask-image: url(m.svg);", PropertyId.MaskImage).Should().Be(new CssUrl("m.svg"));

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-mask-image", section: "7.5.1")]
    [SpecFact]
    public void Mask_image_comma_list_of_two_parses_both_urls()
    {
        var value = Single("mask-image: url(a.svg), url(b.svg);", PropertyId.MaskImage);
        value.Should().BeOfType<CssValueList>();
        var urls = ((CssValueList)value).Values.OfType<CssUrl>().ToList();
        urls.Should().HaveCount(2);
        urls[0].Should().Be(new CssUrl("a.svg"));
        urls[1].Should().Be(new CssUrl("b.svg"));
    }

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-mask-image", section: "7.5.1")]
    [SpecFact]
    public void Mask_image_initial_value_is_none()
        => PropertyRegistry.InitialValue(PropertyId.MaskImage).Should().Be(new CssKeyword("none"));

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-mask-image", section: "7.5.1")]
    [SpecFact]
    public void Mask_image_is_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.MaskImage).Should().BeFalse();

    // ----- mask-mode (§7.5.2) -------------------------------------------

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-mask-mode", section: "7.5.2")]
    [SpecFact]
    public void Mask_mode_alpha_parses()
        => Single("mask-mode: alpha;", PropertyId.MaskMode).Should().Be(new CssKeyword("alpha"));

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-mask-mode", section: "7.5.2")]
    [SpecFact]
    public void Mask_mode_luminance_parses()
        => Single("mask-mode: luminance;", PropertyId.MaskMode).Should().Be(new CssKeyword("luminance"));

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-mask-mode", section: "7.5.2")]
    [SpecFact]
    public void Mask_mode_match_source_parses()
        => Single("mask-mode: match-source;", PropertyId.MaskMode).Should().Be(new CssKeyword("match-source"));

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-mask-mode", section: "7.5.2")]
    [SpecFact]
    public void Mask_mode_initial_value_is_match_source()
        => PropertyRegistry.InitialValue(PropertyId.MaskMode).Should().Be(new CssKeyword("match-source"));

    // ----- mask-repeat (§7.5.4) -----------------------------------------

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-mask-repeat", section: "7.5.4")]
    [SpecFact]
    [DataRow("repeat")]
    [DataRow("no-repeat")]
    [DataRow("space")]
    [DataRow("round")]
    [DataRow("repeat-x")]
    [DataRow("repeat-y")]
    public void Mask_repeat_keyword_parses(string keyword)
        => Single($"mask-repeat: {keyword};", PropertyId.MaskRepeat).Should().Be(new CssKeyword(keyword));

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-mask-repeat", section: "7.5.4")]
    [SpecFact]
    public void Mask_repeat_initial_value_is_repeat()
        => PropertyRegistry.InitialValue(PropertyId.MaskRepeat).Should().Be(new CssKeyword("repeat"));

    // ----- mask-position (§7.5.3) ---------------------------------------

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-mask-position", section: "7.5.3")]
    [SpecFact]
    public void Mask_position_center_keyword_parses()
        => Single("mask-position: center;", PropertyId.MaskPosition).Should().Be(new CssKeyword("center"));

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-mask-position", section: "7.5.3")]
    [SpecFact]
    public void Mask_position_initial_value_is_zero_zero()
        => PropertyRegistry.InitialValue(PropertyId.MaskPosition).Should().Be(new CssKeyword("0% 0%"));

    // ----- mask-size (§7.5.5) -------------------------------------------

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-mask-size", section: "7.5.5")]
    [SpecFact]
    public void Mask_size_contain_keyword_parses()
        => Single("mask-size: contain;", PropertyId.MaskSize).Should().Be(new CssKeyword("contain"));

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-mask-size", section: "7.5.5")]
    [SpecFact]
    public void Mask_size_initial_value_is_auto()
        => PropertyRegistry.InitialValue(PropertyId.MaskSize).Should().Be(new CssKeyword("auto"));

    // ----- mask-clip (§7.5.6) -------------------------------------------

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-mask-clip", section: "7.5.6")]
    [SpecFact]
    public void Mask_clip_content_box_keyword_parses()
        => Single("mask-clip: content-box;", PropertyId.MaskClip).Should().Be(new CssKeyword("content-box"));

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-mask-clip", section: "7.5.6")]
    [SpecFact]
    public void Mask_clip_initial_value_is_border_box()
        => PropertyRegistry.InitialValue(PropertyId.MaskClip).Should().Be(new CssKeyword("border-box"));

    // ----- mask-origin (§7.5.7) -----------------------------------------

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-mask-origin", section: "7.5.7")]
    [SpecFact]
    public void Mask_origin_padding_box_keyword_parses()
        => Single("mask-origin: padding-box;", PropertyId.MaskOrigin).Should().Be(new CssKeyword("padding-box"));

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-mask-origin", section: "7.5.7")]
    [SpecFact]
    public void Mask_origin_initial_value_is_border_box()
        => PropertyRegistry.InitialValue(PropertyId.MaskOrigin).Should().Be(new CssKeyword("border-box"));

    // ----- mask-composite (§7.5.8) --------------------------------------

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-mask-composite", section: "7.5.8")]
    [SpecFact]
    public void Mask_composite_subtract_keyword_parses()
        => Single("mask-composite: subtract;", PropertyId.MaskComposite).Should().Be(new CssKeyword("subtract"));

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-mask-composite", section: "7.5.8")]
    [SpecFact]
    public void Mask_composite_initial_value_is_add()
        => PropertyRegistry.InitialValue(PropertyId.MaskComposite).Should().Be(new CssKeyword("add"));

    // ----- mask shorthand (§7.5) ----------------------------------------

    [Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/#the-mask", section: "7.5")]
    [SpecFact]
    public void Mask_shorthand_sets_longhands()
    {
        // mask: url(m.svg) luminance 10px 20px / cover no-repeat content-box border-box add;
        // → mask-image/-mode/-position/-size/-repeat/-origin/-clip/-composite per §7.5.
        var decls = Expand("mask: url(m.svg) luminance 10px 20px / cover no-repeat content-box border-box add");

        decls.Single(d => d.Id == PropertyId.MaskImage).Value.Should().BeOfType<CssUrl>();
        decls.Single(d => d.Id == PropertyId.MaskMode).Value.Should().Be(new CssKeyword("luminance"));
        decls.Single(d => d.Id == PropertyId.MaskRepeat).Value.Should().Be(new CssKeyword("no-repeat"));
        decls.Single(d => d.Id == PropertyId.MaskOrigin).Value.Should().Be(new CssKeyword("content-box"));
        decls.Single(d => d.Id == PropertyId.MaskClip).Value.Should().Be(new CssKeyword("border-box"));
        decls.Single(d => d.Id == PropertyId.MaskComposite).Value.Should().Be(new CssKeyword("add"));
        decls.Single(d => d.Id == PropertyId.MaskSize).Value.Should().Be(new CssKeyword("cover"));
        // Position carries the two length components (10px 20px).
        decls.Single(d => d.Id == PropertyId.MaskPosition).Value.Should().BeOfType<CssValueList>();
    }
}
