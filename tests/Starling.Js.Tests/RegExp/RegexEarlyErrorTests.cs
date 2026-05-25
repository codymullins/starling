using AwesomeAssertions;
using Starling.Js.RegExp;
namespace Starling.Js.Tests.RegExp;

/// <summary>
/// wp:M3-65 — §22.2.1.1 Static Semantics: Early Errors. A pattern that violates
/// these rules must fail to compile (the JS parser surfaces it as a parse-phase
/// SyntaxError). Each invalid case here mirrors a test262
/// <c>language/literals/regexp</c> negative test; each valid case guards against
/// regressing patterns that must still parse.
/// </summary>
[TestClass]
public class RegexEarlyErrorTests
{
    private static void Compile(string pattern, string flags = "")
    {
        RegexFlagParser.TryParse(flags, out var f, out _);
        CompiledRegex.Compile(pattern, f);
    }

    private static System.Action Act(string pattern, string flags = "")
        => () => Compile(pattern, flags);

    // ----- Duplicate named-capture group names ---------------------------------

    [TestMethod]
    public void Duplicate_group_name_same_alternative_is_error()
        => Act("(?<a>a)(?<a>a)").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Duplicate_group_name_same_alternative_with_filler_is_error()
        => Act("(?<a>a)(?<b>b)(?<a>a)").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Duplicate_group_name_same_alternative_is_error_u()
        => Act("(?<a>a)(?<a>a)", "u").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Duplicate_group_name_separate_alternatives_is_valid()
        => Act("(?<a>a)|(?<a>b)").Should().NotThrow(); // ES2025 regexp-duplicate-named-groups

    [TestMethod]
    public void Duplicate_group_name_separate_alternatives_is_valid_u()
        => Act("(?<x>a)|(?<x>b)", "u").Should().NotThrow();

    [TestMethod]
    public void Duplicate_group_name_nested_separate_alternatives_is_valid()
        => Act("(?:(?<n>a)|(?<n>b))", "u").Should().NotThrow();

    [TestMethod]
    public void Duplicate_group_name_same_alternative_nested_in_group_is_error()
        => Act("(?:(?<n>a)(?<n>b))", "u").Should().Throw<RegexSyntaxException>();

    // ----- Malformed GroupSpecifier / GroupName -------------------------------

    [TestMethod]
    public void Empty_group_name_is_error()
        => Act("(?<>a)").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Numeric_leading_group_name_is_error()
        => Act("(?<42a>a)").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Punctuator_starting_group_name_is_error()
        => Act("(?<:a>a)").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Punctuator_within_group_name_is_error()
        => Act("(?<a:>a)").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Non_id_start_group_name_is_error()
        => Act("(?<❤>a)").Should().Throw<RegexSyntaxException>(); // U+2764 HEAVY BLACK HEART

    [TestMethod]
    public void Unterminated_group_name_is_error()
        => Act("(?<aa)").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Lone_lead_surrogate_in_group_name_is_error()
        => Act("(?<a\uD801>a)").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Lone_trail_surrogate_in_group_name_is_error()
        => Act("(?<a\uDCA4>a)").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Unicode_escape_in_group_name_decoding_to_non_id_is_error()
        => Act("(?<a\\uDCA4>.)").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Valid_group_name_parses()
        => Act("(?<foo>a)").Should().NotThrow();

    [TestMethod]
    public void Valid_group_name_with_dollar_and_underscore_parses()
        => Act("(?<$_x9>a)").Should().NotThrow();

    [TestMethod]
    public void Valid_group_name_with_unicode_escape_start_parses()
        => Act("(?<\\u0041bc>a)").Should().NotThrow(); // A == 'A'

    [TestMethod]
    public void Astral_unicode_escape_non_id_in_group_name_is_error_u()
        => Act("(?<a\\u{1F08B}>.)", "u").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Astral_unicode_escape_in_group_name_non_unicode_is_error()
        => Act("(?<a\\u{1F08B}>.)").Should().Throw<RegexSyntaxException>(); // \u{} not valid escape in non-u

    [TestMethod]
    public void Out_of_range_unicode_escape_in_group_name_is_error_u()
        => Act("(?<a\\u{110000}>.)", "u").Should().Throw<RegexSyntaxException>();

    // ----- \k<name> references ------------------------------------------------

    [TestMethod]
    public void Dangling_named_backref_is_error()
        => Act("(?<a>.)\\k<b>").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Dangling_named_backref_is_error_u()
        => Act("(?<a>.)\\k<b>", "u").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Incomplete_named_backref_no_lt_is_error()
        => Act("(?<a>.)\\k").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Incomplete_named_backref_unterminated_is_error()
        => Act("(?<a>.)\\k<a").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Empty_named_backref_is_error()
        => Act("(?<a>.)\\k<>").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Dangling_named_backref_without_group_u_is_error()
        => Act("\\k<a>", "u").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Valid_named_backref_parses()
        => Act("(?<a>.)\\k<a>").Should().NotThrow();

    [TestMethod]
    public void Forward_named_backref_parses()
        => Act("\\k<a>(?<a>x)").Should().NotThrow();

    [TestMethod]
    public void Bare_k_with_no_named_groups_non_unicode_is_literal()
    {
        // Annex B: \k with no named group present is an IdentityEscape ('k').
        Act("\\k").Should().NotThrow();
        Act("\\k<a>").Should().NotThrow(); // matches the literal text "k<a>"
    }

    // ----- Braced quantifier --------------------------------------------------

    [TestMethod]
    public void Quantifier_min_greater_than_max_is_error()
        => Act("a{2,1}").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Lone_braced_quantifier_exact_is_error()
        => Act("{2}").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Lone_braced_quantifier_lower_is_error()
        => Act("{2,}").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Lone_braced_quantifier_range_is_error()
        => Act("{2,3}").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Non_quantifier_brace_is_literal_non_unicode()
        => Act("a{").Should().NotThrow(); // ExtendedPatternChar literal

    [TestMethod]
    public void Valid_braced_quantifier_parses()
    {
        Act("a{2,3}").Should().NotThrow();
        Act("a{2}").Should().NotThrow();
        Act("a{2,}").Should().NotThrow();
    }

    // ----- Lookaround quantifiers ---------------------------------------------

    [TestMethod]
    public void Optional_lookbehind_is_error()
        => Act(".(?<=.)?").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Ranged_lookbehind_is_error()
        => Act(".(?<=.){2,3}").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Optional_negative_lookbehind_is_error()
        => Act(".(?<!.)?").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Optional_lookahead_is_error_under_u()
        => Act(".(?=.)?", "u").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Ranged_lookahead_is_error_under_u()
        => Act(".(?=.){2,3}", "u").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Optional_lookahead_is_valid_non_unicode()
        => Act("(?:(?=(abc)))?a").Should().NotThrow(); // Annex B QuantifiableAssertion

    // ----- Unicode-mode escapes ----------------------------------------------

    [TestMethod]
    public void Invalid_identity_escape_is_error_under_u()
        => Act("\\M", "u").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Legacy_octal_escape_is_error_under_u()
        => Act("\\1", "u").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Out_of_bounds_decimal_escape_is_error_under_u()
        => Act("\\8", "u").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Identity_escape_in_named_capture_is_error_under_u()
        => Act("(?<a>\\a)", "u").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Legacy_octal_backref_is_literal_non_unicode()
        => Act("\\1").Should().NotThrow(); // Annex B: \1 with no group is octal/literal

    [TestMethod]
    public void Unicode_escape_out_of_range_is_error_under_u()
        => Act("\\u{110000}", "u").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Unicode_escape_empty_braces_is_error_under_u()
        => Act("\\u{}", "u").Should().Throw<RegexSyntaxException>();

    [TestMethod]
    public void Class_escape_as_range_endpoint_is_error_under_u()
    {
        Act("[\\d-a]", "u").Should().Throw<RegexSyntaxException>();
        Act("[a-\\d]", "u").Should().Throw<RegexSyntaxException>();
    }

    [TestMethod]
    public void Valid_class_escape_as_range_endpoint_non_unicode()
        => Act("[\\d-a]").Should().NotThrow(); // Annex B: '-' is literal here

    [TestMethod]
    public void Valid_unicode_escapes_parse_under_u()
    {
        Act("\\u{1F600}", "u").Should().NotThrow();
        Act("\\.", "u").Should().NotThrow();      // identity escape of SyntaxChar
        Act("[a-z]", "u").Should().NotThrow();
        Act("(\\d)\\1", "u").Should().NotThrow(); // valid backref under u
    }

    // ----- Lookbehind that is NOT a named group (regression guard) -------------

    [TestMethod]
    public void Lookbehind_is_not_treated_as_named_group()
        => Act("(?<=ab)c").Should().NotThrow();
}
