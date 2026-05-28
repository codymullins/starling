using AwesomeAssertions;
using Starling.Css.Cascade;
using Starling.Css.CounterStyle;
using Starling.Css.Parser;

namespace Starling.Css.Spec.Tests.CssCounterStyles3;

/// <summary>
/// Conformance for <see href="https://www.w3.org/TR/css-counter-styles-3/">CSS
/// Counter Styles Level 3</see> — the <c>@counter-style</c> at-rule, the counter
/// generation systems, and the predefined styles.
/// </summary>
[TestClass]
[Spec("css-counter-styles-3", "https://www.w3.org/TR/css-counter-styles-3/")]
public sealed class CounterStyleTests
{
    private static CounterStyleRule ParseOne(string css)
    {
        var sheet = CssParser.ParseStyleSheet(css);
        var at = sheet.Rules.OfType<AtRule>().Single(r => r.Name == "counter-style");
        CounterStyleParser.TryParse(at, out var rule).Should().BeTrue();
        return rule!;
    }

    private static CounterStyleResolver ResolverFor(string css)
    {
        var sheet = CssParser.ParseStyleSheet(css);
        var rules = CounterStyleParser.ParseAll(sheet);
        return new CounterStyleResolver(rules);
    }

    // --- §3 at-rule parsing ---

    [Spec("css-counter-styles-3", "https://www.w3.org/TR/css-counter-styles-3/#the-counter-style-rule", section: "3")]
    [SpecFact]
    public void Parses_counter_style_at_rule_name_and_system()
    {
        var rule = ParseOne("@counter-style thumbs { system: cyclic; symbols: \"\\1F44D\"; suffix: \" \"; }");
        rule.Name.Should().Be("thumbs");
        rule.System.Should().Be(CounterSystem.Cyclic);
        rule.Symbols.Should().ContainSingle();
        rule.Suffix.Should().Be(" ");
    }

    [Spec("css-counter-styles-3", "https://www.w3.org/TR/css-counter-styles-3/#counter-style-system", section: "3.1")]
    [SpecFact]
    public void Parses_system_descriptor_for_each_keyword()
    {
        ParseOne("@counter-style a { system: numeric; symbols: '0' '1'; }").System.Should().Be(CounterSystem.Numeric);
        ParseOne("@counter-style b { system: alphabetic; symbols: 'a' 'b'; }").System.Should().Be(CounterSystem.Alphabetic);
        ParseOne("@counter-style c { system: symbolic; symbols: '*'; }").System.Should().Be(CounterSystem.Symbolic);
        ParseOne("@counter-style d { system: additive; additive-symbols: 1 'i'; }").System.Should().Be(CounterSystem.Additive);
        var fixedRule = ParseOne("@counter-style e { system: fixed 3; symbols: 'a' 'b'; }");
        fixedRule.System.Should().Be(CounterSystem.Fixed);
        fixedRule.FixedFirstValue.Should().Be(3);
        var ext = ParseOne("@counter-style f { system: extends decimal; }");
        ext.System.Should().Be(CounterSystem.Extends);
        ext.ExtendsName.Should().Be("decimal");
    }

    [Spec("css-counter-styles-3", "https://www.w3.org/TR/css-counter-styles-3/#counter-style-additive-symbols", section: "3.1.4")]
    [SpecFact]
    public void Parses_additive_symbols_sorted_descending_by_weight()
    {
        var rule = ParseOne("@counter-style x { system: additive; additive-symbols: 1 'I', 5 'V', 10 'X'; }");
        rule.AdditiveSymbols.Select(a => a.Weight).Should().ContainInOrder(10, 5, 1);
        rule.AdditiveSymbols[0].Symbol.Should().Be("X");
    }

    [Spec("css-counter-styles-3", "https://www.w3.org/TR/css-counter-styles-3/#counter-style-negative", section: "3.2")]
    [SpecFact]
    public void Parses_negative_prefix_and_suffix()
    {
        var rule = ParseOne("@counter-style x { system: numeric; symbols: '0' '1'; negative: '(' ')'; }");
        rule.NegativePrefix.Should().Be("(");
        rule.NegativeSuffix.Should().Be(")");
    }

    [Spec("css-counter-styles-3", "https://www.w3.org/TR/css-counter-styles-3/#counter-style-prefix", section: "3.3")]
    [SpecFact]
    public void Parses_prefix_and_suffix_descriptors()
    {
        var rule = ParseOne("@counter-style x { system: numeric; symbols: '0' '1'; prefix: '['; suffix: ']'; }");
        rule.Prefix.Should().Be("[");
        rule.Suffix.Should().Be("]");
    }

    [Spec("css-counter-styles-3", "https://www.w3.org/TR/css-counter-styles-3/#counter-style-range", section: "3.4")]
    [SpecFact]
    public void Parses_range_and_fallback_descriptors()
    {
        var rule = ParseOne("@counter-style x { system: cyclic; symbols: '*'; range: 1 5; fallback: lower-roman; }");
        rule.HasExplicitRange.Should().BeTrue();
        rule.RangeLow.Should().Be(1);
        rule.RangeHigh.Should().Be(5);
        rule.Fallback.Should().Be("lower-roman");
    }

    [Spec("css-counter-styles-3", "https://www.w3.org/TR/css-counter-styles-3/#counter-style-pad", section: "3.5")]
    [SpecFact]
    public void Parses_pad_descriptor()
    {
        var rule = ParseOne("@counter-style x { system: numeric; symbols: '0' '1'; pad: 4 '0'; }");
        rule.PadLength.Should().Be(4);
        rule.PadSymbol.Should().Be("0");
    }

    // --- §2/§6 generation systems ---

    [Spec("css-counter-styles-3", "https://www.w3.org/TR/css-counter-styles-3/#cyclic-system", section: "2.1")]
    [SpecFact]
    public void Cyclic_system_repeats_symbols()
    {
        var r = ResolverFor("@counter-style c { system: cyclic; symbols: 'a' 'b' 'c'; suffix: ''; }");
        r.RenderCore("c", 1).Should().Be("a");
        r.RenderCore("c", 3).Should().Be("c");
        r.RenderCore("c", 4).Should().Be("a");
    }

    [Spec("css-counter-styles-3", "https://www.w3.org/TR/css-counter-styles-3/#fixed-system", section: "2.2")]
    [SpecFact]
    public void Fixed_system_uses_symbols_then_falls_back()
    {
        var r = ResolverFor("@counter-style f { system: fixed; symbols: 'a' 'b' 'c'; suffix: ''; fallback: decimal; }");
        r.RenderCore("f", 1).Should().Be("a");
        r.RenderCore("f", 3).Should().Be("c");
        // Past the symbol run, fixed falls back to decimal.
        r.RenderCore("f", 4).Should().Be("4");
    }

    [Spec("css-counter-styles-3", "https://www.w3.org/TR/css-counter-styles-3/#symbolic-system", section: "2.3")]
    [SpecFact]
    public void Symbolic_system_repeats_chosen_symbol()
    {
        var r = ResolverFor("@counter-style s { system: symbolic; symbols: '*' '\\2020'; suffix: ''; }");
        r.RenderCore("s", 1).Should().Be("*");
        // value 3, two symbols: symbol index (3-1)%2 = 0 ('*'), repeated ceil(3/2)=2 times.
        r.RenderCore("s", 3).Should().Be("**");
    }

    [Spec("css-counter-styles-3", "https://www.w3.org/TR/css-counter-styles-3/#alphabetic-system", section: "2.4")]
    [SpecFact]
    public void Alphabetic_system_is_bijective_base_n()
    {
        var r = ResolverFor("@counter-style a { system: alphabetic; symbols: 'a' 'b' 'c'; suffix: ''; }");
        r.RenderCore("a", 1).Should().Be("a");
        r.RenderCore("a", 3).Should().Be("c");
        r.RenderCore("a", 4).Should().Be("aa");
        r.RenderCore("a", 7).Should().Be("ba");
    }

    [Spec("css-counter-styles-3", "https://www.w3.org/TR/css-counter-styles-3/#numeric-system", section: "2.5")]
    [SpecFact]
    public void Numeric_system_is_positional_base_n()
    {
        var r = ResolverFor("@counter-style n { system: numeric; symbols: '0' '1' '2' '3' '4' '5' '6' '7' '8' '9'; suffix: ''; }");
        r.RenderCore("n", 0).Should().Be("0");
        r.RenderCore("n", 12).Should().Be("12");
        r.RenderCore("n", 100).Should().Be("100");
        // Base-2 numeric: 5 -> "101".
        var bin = ResolverFor("@counter-style b { system: numeric; symbols: '0' '1'; suffix: ''; }");
        bin.RenderCore("b", 5).Should().Be("101");
    }

    [Spec("css-counter-styles-3", "https://www.w3.org/TR/css-counter-styles-3/#additive-system", section: "2.6")]
    [SpecFact]
    public void Additive_system_uses_sign_value_notation()
    {
        var r = ResolverFor(
            "@counter-style roman { system: additive; suffix: ''; " +
            "additive-symbols: 1000 'M', 900 'CM', 500 'D', 400 'CD', 100 'C', 90 'XC', " +
            "50 'L', 40 'XL', 10 'X', 9 'IX', 5 'V', 4 'IV', 1 'I'; }");
        r.RenderCore("roman", 4).Should().Be("IV");
        r.RenderCore("roman", 2023).Should().Be("MMXXIII");
    }

    [Spec("css-counter-styles-3", "https://www.w3.org/TR/css-counter-styles-3/#counter-style-system", section: "3.1")]
    [SpecFact]
    public void Extends_inherits_base_system_and_overrides_descriptors()
    {
        var r = ResolverFor(
            "@counter-style paren { system: extends decimal; prefix: '('; suffix: ') '; }");
        // Core comes from decimal; affixes from this rule.
        r.RenderCore("paren", 3).Should().Be("3");
        r.Render("paren", 3).Should().Be("(3) ");
    }

    // --- §6 prefix / suffix / negative / pad / range / fallback ---

    [Spec("css-counter-styles-3", "https://www.w3.org/TR/css-counter-styles-3/#generate-a-counter", section: "6")]
    [SpecFact]
    public void Render_wraps_core_with_prefix_and_suffix()
    {
        var r = ResolverFor("@counter-style n { system: numeric; symbols: '0' '1' '2' '3' '4' '5' '6' '7' '8' '9'; prefix: '#'; suffix: ': '; }");
        r.Render("n", 7).Should().Be("#7: ");
    }

    [Spec("css-counter-styles-3", "https://www.w3.org/TR/css-counter-styles-3/#counter-style-negative", section: "3.2")]
    [SpecFact]
    public void Negative_values_get_negative_prefix_and_suffix()
    {
        var r = ResolverFor("@counter-style n { system: numeric; symbols: '0' '1' '2' '3' '4' '5' '6' '7' '8' '9'; negative: '(' ')'; suffix: ''; }");
        r.Render("n", -12).Should().Be("(12)");
        r.Render("n", 12).Should().Be("12");
    }

    [Spec("css-counter-styles-3", "https://www.w3.org/TR/css-counter-styles-3/#counter-style-pad", section: "3.5")]
    [SpecFact]
    public void Pad_left_pads_to_minimum_length()
    {
        var r = ResolverFor("@counter-style n { system: numeric; symbols: '0' '1' '2' '3' '4' '5' '6' '7' '8' '9'; pad: 3 '0'; suffix: ''; }");
        r.RenderCore("n", 5).Should().Be("005");
        r.RenderCore("n", 42).Should().Be("042");
        r.RenderCore("n", 1234).Should().Be("1234");
    }

    [Spec("css-counter-styles-3", "https://www.w3.org/TR/css-counter-styles-3/#counter-style-range", section: "3.4")]
    [SpecFact]
    public void Out_of_range_value_uses_fallback()
    {
        var r = ResolverFor("@counter-style c { system: cyclic; symbols: '*'; range: 1 3; fallback: decimal; suffix: ''; }");
        r.RenderCore("c", 2).Should().Be("*");
        // 5 is outside [1,3], so decimal renders it.
        r.RenderCore("c", 5).Should().Be("5");
    }

    [Spec("css-counter-styles-3", "https://www.w3.org/TR/css-counter-styles-3/#counter-style-range", section: "3.4")]
    [SpecFact]
    public void Multiple_comma_separated_range_segments_each_match()
    {
        // §3.4: `range` accepts comma-separated segments; a value is in range if
        // it falls within ANY segment. Here [1 3] and [7 9] are accepted; values
        // between/after fall back to decimal.
        var r = ResolverFor("@counter-style m { system: cyclic; symbols: '*'; range: 1 3, 7 9; fallback: decimal; suffix: ''; }");
        r.RenderCore("m", 2).Should().Be("*");   // in [1,3]
        r.RenderCore("m", 8).Should().Be("*");   // in [7,9]
        r.RenderCore("m", 5).Should().Be("5");   // gap between segments → fallback
        r.RenderCore("m", 10).Should().Be("10"); // past last segment → fallback
    }

    [Spec("css-counter-styles-3", "https://www.w3.org/TR/css-counter-styles-3/#counter-style-range", section: "3.4")]
    [SpecFact]
    public void Range_segment_with_infinite_bound_is_open_ended()
    {
        // `infinite` leaves that side unbounded.
        var r = ResolverFor("@counter-style hi { system: cyclic; symbols: '*'; range: 5 infinite; fallback: decimal; suffix: ''; }");
        r.RenderCore("hi", 4).Should().Be("4");    // below 5 → fallback
        r.RenderCore("hi", 5).Should().Be("*");    // at lower bound
        r.RenderCore("hi", 9999).Should().Be("*"); // open-ended upper
    }

    // --- §7 predefined styles ---

    [Spec("css-counter-styles-3", "https://www.w3.org/TR/css-counter-styles-3/#simple-numeric", section: "7.1.1")]
    [SpecFact]
    public void Predefined_decimal_and_leading_zero()
    {
        var r = CounterStyleResolver.Default;
        r.RenderCore("decimal", 42).Should().Be("42");
        r.RenderCore("decimal-leading-zero", 1).Should().Be("01");
        r.RenderCore("decimal-leading-zero", 9).Should().Be("09");
        r.RenderCore("decimal-leading-zero", 10).Should().Be("10");
    }

    [Spec("css-counter-styles-3", "https://www.w3.org/TR/css-counter-styles-3/#complex-predefined-counters", section: "7.3")]
    [SpecFact]
    public void Predefined_roman()
    {
        var r = CounterStyleResolver.Default;
        r.RenderCore("lower-roman", 4).Should().Be("iv");
        r.RenderCore("lower-roman", 9).Should().Be("ix");
        r.RenderCore("upper-roman", 2023).Should().Be("MMXXIII");
        // Out of roman range (> 3999) falls back to decimal.
        r.RenderCore("lower-roman", 4000).Should().Be("4000");
    }

    [Spec("css-counter-styles-3", "https://www.w3.org/TR/css-counter-styles-3/#alphabetic-system", section: "7.1.2")]
    [SpecFact]
    public void Predefined_alpha()
    {
        var r = CounterStyleResolver.Default;
        r.RenderCore("lower-alpha", 1).Should().Be("a");
        r.RenderCore("lower-alpha", 26).Should().Be("z");
        r.RenderCore("lower-alpha", 27).Should().Be("aa");
        r.RenderCore("upper-alpha", 28).Should().Be("AB");
        r.RenderCore("lower-latin", 27).Should().Be("aa");
    }

    [Spec("css-counter-styles-3", "https://www.w3.org/TR/css-counter-styles-3/#disc", section: "7.4")]
    [SpecFact]
    public void Predefined_glyph_styles_and_full_marker_text()
    {
        var r = CounterStyleResolver.Default;
        r.RenderCore("disc", 1).Should().Be(CounterStyleResolver.Disc);
        r.RenderCore("circle", 1).Should().Be(CounterStyleResolver.Circle);
        r.RenderCore("square", 1).Should().Be(CounterStyleResolver.Square);
        // Numeric predefined styles render with the ". " suffix.
        r.Render("decimal", 3).Should().Be("3. ");
        // Glyph styles have an empty suffix.
        r.Render("disc", 1).Should().Be(CounterStyleResolver.Disc);
    }

    // --- engine integration ---

    [Spec("css-counter-styles-3", "https://www.w3.org/TR/css-counter-styles-3/#the-counter-style-rule", section: "3")]
    [SpecFact]
    public void Style_engine_registers_counter_styles_from_sheets()
    {
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet(
            "@counter-style box { system: cyclic; symbols: 'x'; suffix: ''; }"));
        engine.CounterStyles.RenderCore("box", 7).Should().Be("x");
        // Predefined styles are still resolvable through the engine resolver.
        engine.CounterStyles.RenderCore("lower-roman", 4).Should().Be("iv");
    }
}
