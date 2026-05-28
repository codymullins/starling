using AwesomeAssertions;
using Starling.Css.Animations;
using Starling.Css.Parser;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssAnimations1;

/// <summary>
/// §2 conformance — <c>@keyframes</c> rule parsing.
/// Spec: <see href="https://www.w3.org/TR/css-animations-1/#keyframes"/>
/// </summary>
[TestClass]
[Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/", section: "2")]
public sealed class KeyframesParseTests
{
    private static List<KeyframesRule> ParseAll(string source)
    {
        var sheet = new CssParser(source).ParseStyleSheet();
        return KeyframesParser.ParseAll(sheet).ToList();
    }

    private static KeyframesRule Parse(string source)
        => ParseAll(source).Single();

    // §2 — @keyframes syntax: the rule must have a name (ident or string).
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#keyframes", section: "2")]
    [SpecFact]
    public void Keyframes_name_is_extracted_from_ident()
    {
        var rule = Parse("@keyframes slide { from { opacity: 0 } to { opacity: 1 } }");
        rule.Name.Should().Be("slide");
    }

    // §2 — quoted string names are valid keyframe names.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#keyframes", section: "2")]
    [SpecFact]
    public void Keyframes_name_is_extracted_from_quoted_string()
    {
        var rule = Parse("@keyframes \"my-anim\" { from { opacity: 0 } to { opacity: 1 } }");
        rule.Name.Should().Be("my-anim");
    }

    // §2 — anonymous @keyframes (no name) must be silently skipped.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#keyframes", section: "2")]
    [SpecFact]
    public void Keyframes_without_name_is_skipped()
    {
        var rules = ParseAll("@keyframes { from { opacity: 0 } }");
        rules.Should().BeEmpty();
    }

    // §2 — `from` keyword maps to offset 0.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#keyframe-selector", section: "2")]
    [SpecFact]
    public void From_selector_maps_to_offset_zero()
    {
        var rule = Parse("@keyframes f { from { opacity: 0 } to { opacity: 1 } }");
        rule.Frames[0].Offset.Should().Be(0.0);
    }

    // §2 — `to` keyword maps to offset 1.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#keyframe-selector", section: "2")]
    [SpecFact]
    public void To_selector_maps_to_offset_one()
    {
        var rule = Parse("@keyframes f { from { opacity: 0 } to { opacity: 1 } }");
        rule.Frames[1].Offset.Should().Be(1.0);
    }

    // §2 — 0% maps to offset 0.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#keyframe-selector", section: "2")]
    [SpecFact]
    public void Zero_percent_maps_to_offset_zero()
    {
        var rule = Parse("@keyframes f { 0% { opacity: 0 } 100% { opacity: 1 } }");
        rule.Frames[0].Offset.Should().Be(0.0);
    }

    // §2 — 50% maps to offset 0.5.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#keyframe-selector", section: "2")]
    [SpecFact]
    public void Fifty_percent_maps_to_offset_point_five()
    {
        var rule = Parse("@keyframes f { 0% { opacity: 0 } 50% { opacity: 0.5 } 100% { opacity: 1 } }");
        rule.Frames[1].Offset.Should().BeApproximately(0.5, 1e-9);
    }

    // §2 — 100% maps to offset 1.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#keyframe-selector", section: "2")]
    [SpecFact]
    public void Hundred_percent_maps_to_offset_one()
    {
        var rule = Parse("@keyframes f { 0% { opacity: 0 } 100% { opacity: 1 } }");
        rule.Frames[1].Offset.Should().Be(1.0);
    }

    // §2 — keyframes with multiple selectors (0%, 100%) expand to one frame per offset.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#keyframe-selector", section: "2")]
    [SpecFact]
    public void Multiple_selectors_expand_to_one_frame_per_offset()
    {
        var rule = Parse("@keyframes bounce { 0%, 100% { opacity: 0 } 50% { opacity: 1 } }");
        // Frame at 0%, shared declarations, frame at 50%, frame at 100%.
        rule.Frames.Select(f => f.Offset).Should().Equal(0.0, 0.5, 1.0);
        // 0% and 100% share their declaration list (same keyframe block).
        rule.Frames[0].Declarations.Should().BeSameAs(rule.Frames[2].Declarations);
    }

    // §2 — from, 50%, to in one rule produces three frames sorted by offset.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#keyframes", section: "2")]
    [SpecFact]
    public void From_fifty_to_produces_three_sorted_frames()
    {
        var rule = Parse("@keyframes f { to { opacity: 1 } from { opacity: 0 } 50% { opacity: 0.5 } }");
        rule.Frames.Select(f => f.Offset).Should().Equal(0.0, 0.5, 1.0);
    }

    // §2 — declarations inside a keyframe block are captured.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#keyframes", section: "2")]
    [SpecFact]
    public void Declarations_within_keyframe_are_captured()
    {
        var rule = Parse("@keyframes f { from { opacity: 0; color: red } to { opacity: 1 } }");
        rule.Frames[0].Declarations.Should().HaveCount(2);
        rule.Frames[0].Declarations.Select(d => d.Property).Should().Contain("opacity").And.Contain("color");
    }

    // §2 — declaration values round-trip correctly (opacity: 0.7 as CssNumber).
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#keyframes", section: "2")]
    [SpecFact]
    public void Declaration_value_is_preserved_as_css_number()
    {
        var rule = Parse("@keyframes f { from { opacity: 0.7 } to { opacity: 1 } }");
        rule.Frames[0].Declarations.Should().ContainSingle()
            .Which.Value.Should().BeOfType<CssNumber>()
            .Which.Value.Should().BeApproximately(0.7, 1e-9);
    }

    // §2 — multiple @keyframes rules in the same stylesheet are all collected.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#keyframes", section: "2")]
    [SpecFact]
    public void Multiple_at_keyframes_in_sheet_are_all_collected()
    {
        var rules = ParseAll(
            "@keyframes a { from { opacity: 0 } to { opacity: 1 } }" +
            "@keyframes b { from { opacity: 1 } to { opacity: 0 } }" +
            "@keyframes c { 50% { opacity: 0.5 } }");
        rules.Select(r => r.Name).Should().Equal("a", "b", "c");
    }

    // §2 — keyframe selectors outside [0,100%] are dropped per spec.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#keyframe-selector", section: "2")]
    [SpecFact]
    public void Out_of_range_selectors_are_dropped()
    {
        var rule = Parse("@keyframes f { -10% { opacity: 0 } 50% { opacity: 0.5 } 110% { opacity: 1 } }");
        rule.Frames.Select(f => f.Offset).Should().Equal(0.5);
    }

    // §2 — vendor-prefixed @-webkit-keyframes is treated as @keyframes.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#keyframes", section: "2")]
    [SpecFact]
    public void Webkit_prefixed_keyframes_alias_is_recognised()
    {
        var rule = Parse("@-webkit-keyframes spin { 0% { opacity: 0 } 100% { opacity: 1 } }");
        rule.Name.Should().Be("spin");
    }

    // §2 — vendor-prefixed @-moz-keyframes is treated as @keyframes.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#keyframes", section: "2")]
    [SpecFact]
    public void Moz_prefixed_keyframes_alias_is_recognised()
    {
        var rule = Parse("@-moz-keyframes spin { 0% { opacity: 0 } 100% { opacity: 1 } }");
        rule.Name.Should().Be("spin");
    }

    // §2 — animation-timing-function inside a keyframe is extracted into
    // SegmentTimingFunction and is NOT included in the normal Declarations list.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#keyframes", section: "2")]
    [SpecFact]
    public void Per_keyframe_timing_function_is_captured_not_in_declarations()
    {
        var rule = Parse("@keyframes f { from { opacity: 0; animation-timing-function: ease-in } to { opacity: 1 } }");
        var fromFrame = rule.Frames[0];
        // animation-timing-function must not appear in the regular declaration list.
        fromFrame.Declarations.Select(d => d.Property)
            .Should().NotContain("animation-timing-function");
        // It must be captured in SegmentTimingFunction.
        fromFrame.SegmentTimingFunction.Should().NotBeNull();
    }

    // §2 — a keyframe with only animation-timing-function has no regular declarations.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#keyframes", section: "2")]
    [SpecFact]
    public void Keyframe_with_only_timing_function_has_empty_declarations()
    {
        var rule = Parse("@keyframes f { from { animation-timing-function: linear } to { opacity: 1 } }");
        rule.Frames[0].Declarations.Should().BeEmpty();
        rule.Frames[0].SegmentTimingFunction.Should().NotBeNull();
    }

    // §2 — frames are sorted ascending by offset regardless of source order.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#keyframes", section: "2")]
    [SpecFact]
    public void Frames_are_sorted_ascending_by_offset()
    {
        var rule = Parse("@keyframes f { 75% { opacity: 0.75 } 25% { opacity: 0.25 } 50% { opacity: 0.5 } }");
        rule.Frames.Select(f => f.Offset).Should().BeInAscendingOrder();
        rule.Frames.Select(f => f.Offset).Should().Equal(0.25, 0.5, 0.75);
    }
}
