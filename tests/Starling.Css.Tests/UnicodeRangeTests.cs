using FluentAssertions;
using Starling.Css.FontFace;
using Starling.Css.Parser;
namespace Starling.Css.Tests;

[TestClass]
public sealed class UnicodeRangeTests
{
    [TestMethod]
    public void Parses_range_with_wildcards()
    {
        // U+4?? expands to U+400..U+4FF — Cyrillic block.
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: "Subset";
                src: url("subset.woff2");
                unicode-range: U+4??;
            }
            """);
        var rule = FontFaceParser.ParseAll(sheet).Should().ContainSingle().Subject;
        rule.UnicodeRange.Should().NotBeNull();
        rule.UnicodeRange!.Contains(0x400).Should().BeTrue();
        rule.UnicodeRange.Contains(0x4FF).Should().BeTrue();
        rule.UnicodeRange.Contains(0x3FF).Should().BeFalse();
        rule.UnicodeRange.Contains(0x500).Should().BeFalse();
    }

    [TestMethod]
    public void Parses_range_list()
    {
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: "Multi";
                src: url("multi.woff2");
                unicode-range: U+0-7F, U+2000-206F, U+1F600;
            }
            """);
        var rule = FontFaceParser.ParseAll(sheet).Should().ContainSingle().Subject;
        var r = rule.UnicodeRange!;
        r.Contains('A').Should().BeTrue();
        r.Contains(0x2014).Should().BeTrue();   // em-dash
        r.Contains(0x1F600).Should().BeTrue();  // 😀
        r.Contains(0x1F601).Should().BeFalse();
    }

    [TestMethod]
    public void CoversAll_handles_surrogate_pairs()
    {
        var range = new UnicodeRangeSet(new[] { (0, 0x10FFFF) });
        range.CoversAll("Hello 😀").Should().BeTrue();
    }

    [TestMethod]
    public void Disjoint_ranges_are_kept_disjoint()
    {
        var range = new UnicodeRangeSet(new[] { (0, 0x7F), (0x2000, 0x20FF) });
        range.Contains(0x80).Should().BeFalse();
        range.Contains(0x1FFF).Should().BeFalse();
        range.Contains(0x2000).Should().BeTrue();
    }
}
