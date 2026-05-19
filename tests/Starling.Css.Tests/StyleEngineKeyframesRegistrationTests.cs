using FluentAssertions;
using Tessera.Css.Cascade;
using Tessera.Css.Parser;
using Xunit;
using Starling.Spec;

namespace Tessera.Css.Tests;

[Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/")]

public sealed class StyleEngineKeyframesRegistrationTests
{
    private static StyleSheet ParseSheet(string source)
        => new CssParser(source).ParseStyleSheet(StyleOrigin.Author);

    [Fact]
    public void Adding_sheet_with_keyframes_registers_rule_by_name()
    {
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(ParseSheet(
            "@keyframes fade { 0% { opacity: 0 } 100% { opacity: 1 } }"));

        engine.AnimationEngine.HasKeyframes("fade").Should().BeTrue();
        var rule = engine.AnimationEngine.GetKeyframes("fade")!;
        rule.Frames.Should().HaveCount(2);
    }

    [Fact]
    public void Sheets_without_keyframes_do_not_register_anything()
    {
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(ParseSheet(".x { color: red }"));
        engine.AnimationEngine.HasKeyframes("anything").Should().BeFalse();
    }

    [Fact]
    public void Removing_sheet_drops_keyframes_unique_to_it()
    {
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        var sheet = ParseSheet("@keyframes spin { 0% { opacity: 0 } 100% { opacity: 1 } }");
        engine.AddStyleSheet(sheet);
        engine.AnimationEngine.HasKeyframes("spin").Should().BeTrue();

        engine.RemoveStyleSheet(sheet);
        engine.AnimationEngine.HasKeyframes("spin").Should().BeFalse();
    }

    [Fact]
    public void Removing_one_of_two_sheets_preserves_keyframes_in_remaining_sheet()
    {
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        var keep = ParseSheet("@keyframes a { 0% { opacity: 0 } 100% { opacity: 1 } }");
        var drop = ParseSheet("@keyframes b { 0% { opacity: 0 } 100% { opacity: 1 } }");
        engine.AddStyleSheet(keep);
        engine.AddStyleSheet(drop);

        engine.RemoveStyleSheet(drop);

        engine.AnimationEngine.HasKeyframes("a").Should().BeTrue();
        engine.AnimationEngine.HasKeyframes("b").Should().BeFalse();
    }

    [Fact]
    public void Later_sheet_with_same_name_overrides_earlier_rule()
    {
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(ParseSheet(
            "@keyframes fade { 0% { opacity: 0 } 100% { opacity: 0.5 } }"));
        engine.AddStyleSheet(ParseSheet(
            "@keyframes fade { 0% { opacity: 0.25 } 100% { opacity: 1 } }"));

        var rule = engine.AnimationEngine.GetKeyframes("fade")!;
        rule.Frames[0].Declarations[0].Value.ToString().Should().Be("0.25");
        rule.Frames[^1].Declarations[0].Value.ToString().Should().Be("1");
    }
}
