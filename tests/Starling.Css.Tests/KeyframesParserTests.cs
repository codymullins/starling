using FluentAssertions;
using Tessera.Css.Animations;
using Tessera.Css.Parser;
using Tessera.Css.Values;
using Xunit;
using Starling.Spec;

namespace Tessera.Css.Tests;

[Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/")]

public sealed class KeyframesParserTests
{
    private static List<KeyframesRule> ParseAll(string source)
    {
        var sheet = new CssParser(source).ParseStyleSheet();
        return KeyframesParser.ParseAll(sheet).ToList();
    }

    [Fact]
    public void From_and_to_translate_to_0_and_1()
    {
        var rules = ParseAll("@keyframes fade { from { opacity: 0 } to { opacity: 1 } }");
        rules.Should().ContainSingle();
        var k = rules[0];
        k.Name.Should().Be("fade");
        k.Frames.Select(f => f.Offset).Should().Equal(0.0, 1.0);
        k.Frames[0].Declarations.Should().ContainSingle()
            .Which.Property.Should().Be("opacity");
        k.Frames[1].Declarations[0].Value.Should().BeOfType<CssNumber>()
            .Which.Value.Should().Be(1);
    }

    [Fact]
    public void Percent_selectors_resolve_to_normalized_offsets()
    {
        var rules = ParseAll("@keyframes pulse { 0% { opacity: 0.2 } 50% { opacity: 1 } 100% { opacity: 0.2 } }");
        rules[0].Frames.Select(f => f.Offset).Should().Equal(0.0, 0.5, 1.0);
    }

    [Fact]
    public void Grouped_selectors_expand_to_one_frame_per_offset()
    {
        var rules = ParseAll("@keyframes bounce { 0%, 100% { transform: translateY(0) } 50% { transform: translateY(-10px) } }");
        rules[0].Frames.Select(f => f.Offset).Should().Equal(0.0, 0.5, 1.0);
        // 0% and 100% frames share their declarations.
        rules[0].Frames[0].Declarations.Should().BeSameAs(rules[0].Frames[2].Declarations);
    }

    [Fact]
    public void Out_of_range_selectors_are_dropped()
    {
        var rules = ParseAll("@keyframes weird { -10% { opacity: 0 } 150% { opacity: 1 } 50% { opacity: 0.5 } }");
        rules[0].Frames.Select(f => f.Offset).Should().Equal(0.5);
    }

    [Fact]
    public void Vendor_prefixed_alias_is_recognised()
    {
        var rules = ParseAll("@-webkit-keyframes spin { 0% { transform: rotate(0deg) } 100% { transform: rotate(360deg) } }");
        rules.Should().ContainSingle().Which.Name.Should().Be("spin");
    }

    [Fact]
    public void Multiple_keyframes_rules_in_one_sheet()
    {
        var rules = ParseAll(
            "@keyframes a { from { opacity: 0 } to { opacity: 1 } }" +
            "@keyframes b { from { opacity: 1 } to { opacity: 0 } }");
        rules.Select(r => r.Name).Should().Equal("a", "b");
    }

    [Fact]
    public void Anonymous_at_rule_with_no_name_is_skipped()
    {
        var rules = ParseAll("@keyframes { from { opacity: 0 } }");
        rules.Should().BeEmpty();
    }
}
