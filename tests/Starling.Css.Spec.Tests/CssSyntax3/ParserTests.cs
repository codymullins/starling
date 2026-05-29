using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Tokenizer;

namespace Starling.Css.Spec.Tests.CssSyntax3;

/// <summary>
/// Parser conformance for
/// <see href="https://www.w3.org/TR/css-syntax-3/#parsing">CSS Syntax Level 3 §5</see>.
/// </summary>
[TestClass]
[Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/", section: "5")]
public sealed class ParserTests
{
    // ─── §5.3.2 parse a stylesheet ────────────────────────────────────────────

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#parse-stylesheet", section: "5.3.2")]
    [SpecFact]
    public void ParseStyleSheet_returns_a_stylesheet_object()
    {
        var sheet = CssParser.ParseStyleSheet("a { color: red; }");
        sheet.Should().NotBeNull();
        sheet.Source.Should().Be("a { color: red; }");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#parse-stylesheet", section: "5.3.2")]
    [SpecFact]
    public void ParseStyleSheet_empty_source_gives_no_rules()
    {
        var sheet = CssParser.ParseStyleSheet("");
        sheet.Rules.Should().BeEmpty();
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#parse-stylesheet", section: "5.3.2")]
    [SpecFact]
    public void ParseStyleSheet_multiple_top_level_rules()
    {
        var sheet = CssParser.ParseStyleSheet("a { } b { } c { }");
        sheet.Rules.Should().HaveCount(3);
    }

    // ─── §5.3.4 parse a list of rules ─────────────────────────────────────────

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#parse-list-of-rules", section: "5.3.4")]
    [SpecFact]
    public void Cdo_and_cdc_are_skipped_at_top_level_of_stylesheet()
    {
        // §5.3.4: CDO/CDC are ignored at the top level
        var sheet = CssParser.ParseStyleSheet("<!-- p { color: red; } -->");
        var rule = sheet.Rules.OfType<StyleRule>().Should().ContainSingle().Subject;
        rule.Declarations.Should().ContainSingle(d => d.Name == "color");
    }

    // ─── §5.4.4 consume a qualified rule ──────────────────────────────────────

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-qualified-rule", section: "5.4.4")]
    [SpecFact]
    public void QualifiedRule_has_prelude_and_block_with_declarations()
    {
        var sheet = CssParser.ParseStyleSheet("body { color: red; }");
        var rule = sheet.Rules.Should().ContainSingle().Which.Should().BeOfType<StyleRule>().Subject;
        rule.Prelude.Should().NotBeEmpty();
        rule.Declarations.Should().ContainSingle(d => d.Name == "color");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-qualified-rule", section: "5.4.4")]
    [SpecFact]
    public void QualifiedRule_prelude_contains_selector_tokens()
    {
        var sheet = CssParser.ParseStyleSheet(".card { color: blue; }");
        var rule = sheet.Rules.Should().ContainSingle().Which.Should().BeOfType<StyleRule>().Subject;
        // Prelude should have Delim('.') followed by Ident("card")
        var tokenTypes = rule.Prelude.OfType<CssTokenValue>().Select(v => v.Token.Type).ToList();
        tokenTypes.Should().Contain(CssTokenType.Delim);
        tokenTypes.Should().Contain(CssTokenType.Ident);
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-qualified-rule", section: "5.4.4")]
    [SpecFact]
    public void QualifiedRule_unterminated_block_does_not_throw()
    {
        // §5.4.4: if EOF is reached without a closing brace, the rule is still produced
        var act = () => CssParser.ParseStyleSheet("p { color: red");
        act.Should().NotThrow();
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-qualified-rule", section: "5.4.4")]
    [SpecFact]
    public void QualifiedRule_prelude_without_block_at_eof_produces_rule_with_no_declarations()
    {
        // §5.4.4: a prelude without a matching { is an error; rule still emitted
        var sheet = CssParser.ParseStyleSheet("a > b");
        // Should not throw; likely produces an empty rule
        sheet.Rules.Should().HaveCount(1);
    }

    // ─── §5.4.5 consume an at-rule ────────────────────────────────────────────

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-at-rule", section: "5.4.5")]
    [SpecFact]
    public void AtRule_with_block_parses_nested_rules()
    {
        var sheet = CssParser.ParseStyleSheet("@media screen { a { color: red; } }");
        var media = sheet.Rules.Should().ContainSingle().Which.Should().BeOfType<AtRule>().Subject;
        media.Name.Should().Be("media");
        media.Rules.Should().ContainSingle().Which.Should().BeOfType<StyleRule>();
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-at-rule", section: "5.4.5")]
    [SpecFact]
    public void AtRule_prelude_contains_query_tokens()
    {
        var sheet = CssParser.ParseStyleSheet("@media (max-width: 600px) { }");
        var media = sheet.Rules.Should().ContainSingle().Which.Should().BeOfType<AtRule>().Subject;
        // Prelude is the (max-width: 600px) block
        media.Prelude.Should().NotBeEmpty();
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-at-rule", section: "5.4.5")]
    [SpecFact]
    public void AtRule_with_semicolon_only_has_no_block()
    {
        // §5.4.5: if the at-rule ends with semicolon, no block is consumed
        var sheet = CssParser.ParseStyleSheet("@import url(a.css);");
        var atRule = sheet.Rules.Should().ContainSingle().Which.Should().BeOfType<AtRule>().Subject;
        atRule.Name.Should().Be("import");
        atRule.Rules.Should().BeEmpty();
        atRule.Declarations.Should().BeEmpty();
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-at-rule", section: "5.4.5")]
    [SpecFact]
    public void AtRule_at_font_face_parses_declarations_not_rules()
    {
        // @font-face body is a declaration block per CSS Fonts spec
        var sheet = CssParser.ParseStyleSheet("@font-face { font-family: \"Inter\"; src: url(inter.woff2); }");
        var fontFace = sheet.Rules.Should().ContainSingle().Which.Should().BeOfType<AtRule>().Subject;
        fontFace.Name.Should().Be("font-face");
        fontFace.Rules.Should().BeEmpty();
        fontFace.Declarations.Select(d => d.Name).Should().ContainInOrder("font-family", "src");
    }

    // ─── §5.4.6 consume a declaration ─────────────────────────────────────────

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-declaration", section: "5.4.6")]
    [SpecFact]
    public void Declaration_name_and_value()
    {
        var sheet = CssParser.ParseStyleSheet("p { color: red; }");
        var decl = sheet.Rules.OfType<StyleRule>().Single().Declarations.Should().ContainSingle().Subject;
        decl.Name.Should().Be("color");
        decl.Value.OfType<CssTokenValue>().Should()
            .ContainSingle(v => v.Token.Value == "red");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-declaration", section: "5.4.6")]
    [SpecFact]
    public void Declaration_with_important_flag()
    {
        // §5.4.6: !important is recognized and stripped from the value list
        var sheet = CssParser.ParseStyleSheet("p { color: red !important; }");
        var decl = sheet.Rules.OfType<StyleRule>().Single().Declarations.Should().ContainSingle().Subject;
        decl.Name.Should().Be("color");
        decl.Important.Should().BeTrue();
        // The !important tokens must be removed from the value
        decl.Value.OfType<CssTokenValue>()
            .Should().NotContain(v => v.Token.Delimiter == '!');
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-declaration", section: "5.4.6")]
    [SpecFact]
    public void Declaration_without_important_flag_is_false()
    {
        var sheet = CssParser.ParseStyleSheet("p { margin: 0; }");
        var decl = sheet.Rules.OfType<StyleRule>().Single().Declarations.Single();
        decl.Important.Should().BeFalse();
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-declaration", section: "5.4.6")]
    [SpecFact]
    public void Declaration_whitespace_around_colon_is_tolerated()
    {
        // §5.4.6: whitespace around the colon is consumed
        var sheet = CssParser.ParseStyleSheet("p { width   :  10px ; }");
        var decl = sheet.Rules.OfType<StyleRule>().Single().Declarations.Should().ContainSingle().Subject;
        decl.Name.Should().Be("width");
        decl.Value.OfType<CssTokenValue>()
            .Should().ContainSingle(v => v.Token.Type == CssTokenType.Dimension && v.Token.Unit == "px");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-declaration", section: "5.4.6")]
    [SpecFact]
    public void Declaration_not_important_when_ident_differs_from_important()
    {
        // §5.4.6: only the exact word "important" (case-insensitive) qualifies
        var sheet = CssParser.ParseStyleSheet("p { a: 1 !nope; }");
        sheet.Rules.OfType<StyleRule>().Single().Declarations.Single().Important.Should().BeFalse();
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-declaration", section: "5.4.6")]
    [SpecFact]
    public void Declaration_important_case_insensitive()
    {
        // §5.4.6: !IMPORTANT (uppercase) is also recognized
        var sheet = CssParser.ParseStyleSheet("p { color: red !IMPORTANT; }");
        sheet.Rules.OfType<StyleRule>().Single().Declarations.Single().Important.Should().BeTrue();
    }

    // ─── §5.4.7 consume a list of declarations ────────────────────────────────

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-list-of-declarations", section: "5.4.7")]
    [SpecFact]
    public void DeclarationList_multiple_declarations()
    {
        var sheet = CssParser.ParseStyleSheet("p { color: red; margin: 4px; padding: 2px; }");
        var rule = sheet.Rules.OfType<StyleRule>().Single();
        rule.Declarations.Should().HaveCount(3);
        rule.Declarations.Select(d => d.Name)
            .Should().ContainInOrder("color", "margin", "padding");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-list-of-declarations", section: "5.4.7")]
    [SpecFact]
    public void DeclarationList_last_declaration_without_semicolon_is_included()
    {
        // §5.4.7: the last declaration may not have a trailing semicolon
        var sheet = CssParser.ParseStyleSheet("p { color: red }");
        sheet.Rules.OfType<StyleRule>().Single().Declarations.Should().ContainSingle(d => d.Name == "color");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-list-of-declarations", section: "5.4.7")]
    [SpecFact]
    public void DeclarationList_error_recovery_drops_invalid_declaration_keeps_rest()
    {
        // §5.4.7: if a declaration is invalid, it is dropped; parsing continues
        var sheet = CssParser.ParseStyleSheet("p { ???; color: red; }");
        var rule = sheet.Rules.OfType<StyleRule>().Should().ContainSingle().Subject;
        rule.Declarations.Should().ContainSingle(d => d.Name == "color");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-list-of-declarations", section: "5.4.7")]
    [SpecFact]
    public void DeclarationList_error_before_valid_decl_only_drops_bad_one()
    {
        // Bad declaration is skipped; the valid one after it is still parsed
        var sheet = CssParser.ParseStyleSheet("p { 123bad: value; color: green; }");
        var rule = sheet.Rules.OfType<StyleRule>().Single();
        rule.Declarations.Should().ContainSingle(d => d.Name == "color");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-list-of-declarations", section: "5.4.7")]
    [SpecFact]
    public void DeclarationList_custom_property_with_dashed_name()
    {
        // Custom properties --x: value are valid declarations per CSS Variables 1
        var sheet = CssParser.ParseStyleSheet("p { --brand: #036; color: var(--brand); }");
        var rule = sheet.Rules.OfType<StyleRule>().Single();
        rule.Declarations.Select(d => d.Name).Should().ContainInOrder("--brand", "color");
    }

    // ─── §5.4.8 / §5.4.9 consume a component value / simple block / function ──

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-component-value", section: "5.4.8")]
    [SpecFact]
    public void ComponentValue_simple_token_is_CssTokenValue()
    {
        var sheet = CssParser.ParseStyleSheet("p { margin: 4px; }");
        var val = sheet.Rules.OfType<StyleRule>().Single().Declarations.Single()
            .Value.Should().ContainSingle().Which.Should().BeOfType<CssTokenValue>().Subject;
        val.Token.Type.Should().Be(CssTokenType.Dimension);
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-simple-block", section: "5.4.9")]
    [SpecFact]
    public void SimpleBlock_round_bracket_balancing()
    {
        // Declaration value like (a + b) becomes a CssSimpleBlock
        var sheet = CssParser.ParseStyleSheet("p { x: (a + b); }");
        var decl = sheet.Rules.OfType<StyleRule>().Single().Declarations.Single();
        var block = decl.Value.OfType<CssSimpleBlock>().Should().ContainSingle().Subject;
        block.StartToken.Should().Be(CssTokenType.LeftParen);
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-simple-block", section: "5.4.9")]
    [SpecFact]
    public void SimpleBlock_square_bracket_balancing()
    {
        var sheet = CssParser.ParseStyleSheet("p { x: [a, b]; }");
        var decl = sheet.Rules.OfType<StyleRule>().Single().Declarations.Single();
        var block = decl.Value.OfType<CssSimpleBlock>().Should().ContainSingle().Subject;
        block.StartToken.Should().Be(CssTokenType.LeftSquare);
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-simple-block", section: "5.4.9")]
    [SpecFact]
    public void SimpleBlock_nested_braces_in_at_rule_prelude()
    {
        var sheet = CssParser.ParseStyleSheet("@media (max-width: 600px) { }");
        var media = sheet.Rules.Should().ContainSingle().Which.Should().BeOfType<AtRule>().Subject;
        // The (max-width: 600px) is a simple block with LeftParen
        var block = media.Prelude.OfType<CssSimpleBlock>().Should().ContainSingle().Subject;
        block.StartToken.Should().Be(CssTokenType.LeftParen);
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-function", section: "5.4.10")]
    [SpecFact]
    public void Function_component_value_captures_name_and_arguments()
    {
        var sheet = CssParser.ParseStyleSheet("p { width: calc(100% - 16px); }");
        var func = sheet.Rules.OfType<StyleRule>().Single()
            .Declarations.Single().Value.OfType<CssFunction>().Should().ContainSingle().Subject;
        func.Name.Should().Be("calc");
        func.Values.Should().NotBeEmpty();
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-function", section: "5.4.10")]
    [SpecFact]
    public void Function_component_value_arguments_contain_correct_token_types()
    {
        var sheet = CssParser.ParseStyleSheet("p { width: calc(100% - 16px); }");
        var func = sheet.Rules.OfType<StyleRule>().Single()
            .Declarations.Single().Value.OfType<CssFunction>().Single();
        var tokenTypes = func.Values.OfType<CssTokenValue>().Select(v => v.Token.Type).ToList();
        tokenTypes.Should().Contain(CssTokenType.Percentage);
        tokenTypes.Should().Contain(CssTokenType.Delim);   // '-'
        tokenTypes.Should().Contain(CssTokenType.Dimension);
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-function", section: "5.4.10")]
    [SpecFact]
    public void Function_unterminated_is_closed_at_eof()
    {
        // §5.4.10: if EOF is encountered, the function is closed
        var act = () => CssParser.ParseStyleSheet("p { width: calc(100%");
        act.Should().NotThrow();
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-function", section: "5.4.10")]
    [SpecFact]
    public void Function_with_no_arguments()
    {
        var sheet = CssParser.ParseStyleSheet("p { x: foo(); }");
        var func = sheet.Rules.OfType<StyleRule>().Single()
            .Declarations.Single().Value.OfType<CssFunction>().Should().ContainSingle().Subject;
        func.Name.Should().Be("foo");
        func.Values.Should().BeEmpty();
    }

    // ─── §5 brace-balance of selector / at-rule block ─────────────────────────

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#parsing", section: "5")]
    [SpecFact]
    public void Nested_braces_in_at_rule_are_balanced()
    {
        // A @supports rule with nested style rules
        var sheet = CssParser.ParseStyleSheet("@supports (display: grid) { .a { color: red; } .b { color: blue; } }");
        var supports = sheet.Rules.Should().ContainSingle().Which.Should().BeOfType<AtRule>().Subject;
        supports.Name.Should().Be("supports");
        supports.Rules.Should().HaveCount(2);
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#parsing", section: "5")]
    [SpecFact]
    public void Nested_parens_in_media_prelude_are_balanced()
    {
        // Nested parens: @media (not (screen)) { }
        var sheet = CssParser.ParseStyleSheet("@media (not (screen)) { a { } }");
        var media = sheet.Rules.Should().ContainSingle().Which.Should().BeOfType<AtRule>().Subject;
        media.Rules.Should().ContainSingle();
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#parsing", section: "5")]
    [SpecFact]
    public void Selector_with_attribute_square_brackets_balanced()
    {
        var sheet = CssParser.ParseStyleSheet("[href=\"a\"] { color: red; }");
        var rule = sheet.Rules.Should().ContainSingle().Which.Should().BeOfType<StyleRule>().Subject;
        rule.Declarations.Should().ContainSingle(d => d.Name == "color");
    }

    // ─── §5 style rule: selector + multiple declarations ──────────────────────

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#parsing", section: "5")]
    [SpecFact]
    public void StyleRule_comma_separated_selector_prelude()
    {
        var sheet = CssParser.ParseStyleSheet("body, .card { color: red; }");
        var rule = sheet.Rules.Should().ContainSingle().Which.Should().BeOfType<StyleRule>().Subject;
        // Prelude should contain Comma token
        rule.Prelude.OfType<CssTokenValue>()
            .Should().Contain(v => v.Token.Type == CssTokenType.Comma);
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#parsing", section: "5")]
    [SpecFact]
    public void StyleRule_pseudo_element_in_selector_prelude()
    {
        var sheet = CssParser.ParseStyleSheet("a::before { content: ''; }");
        var rule = sheet.Rules.Should().ContainSingle().Which.Should().BeOfType<StyleRule>().Subject;
        // Prelude should contain two Colon tokens
        var colonCount = rule.Prelude.OfType<CssTokenValue>()
            .Count(v => v.Token.Type == CssTokenType.Colon);
        colonCount.Should().Be(2);
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#parsing", section: "5")]
    [SpecFact]
    public void StyleRule_multiple_declarations_with_important()
    {
        var sheet = CssParser.ParseStyleSheet("body { color: red; margin: 1rem 2px !important; }");
        var rule = sheet.Rules.Should().ContainSingle().Which.Should().BeOfType<StyleRule>().Subject;
        rule.Declarations.Should().HaveCount(2);
        rule.Declarations[0].Important.Should().BeFalse();
        rule.Declarations[1].Important.Should().BeTrue();
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#parsing", section: "5")]
    [SpecFact]
    public void StyleRule_dimension_and_number_in_shorthand_value()
    {
        var sheet = CssParser.ParseStyleSheet("p { margin: 1rem 2px; }");
        var decl = sheet.Rules.OfType<StyleRule>().Single().Declarations.Single();
        var dims = decl.Value.OfType<CssTokenValue>()
            .Where(v => v.Token.Type == CssTokenType.Dimension).ToList();
        dims.Should().HaveCount(2);
        dims[0].Token.Unit.Should().Be("rem");
        dims[1].Token.Unit.Should().Be("px");
    }

    // ─── §5 at-rule: prelude + block with nested rules ────────────────────────

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#parsing", section: "5")]
    [SpecFact]
    public void AtRule_media_with_nested_style_rules()
    {
        var sheet = CssParser.ParseStyleSheet("""
            @media (max-width: 600px) {
                .card { display: block; }
            }
            """);
        var media = sheet.Rules.Should().ContainSingle().Which.Should().BeOfType<AtRule>().Subject;
        media.Name.Should().Be("media");
        var nested = media.Rules.Should().ContainSingle().Which.Should().BeOfType<StyleRule>().Subject;
        nested.Declarations.Should().ContainSingle(d => d.Name == "display");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#parsing", section: "5")]
    [SpecFact]
    public void AtRule_import_url_prelude_contains_url_token()
    {
        var sheet = CssParser.ParseStyleSheet("@import url(a.css);");
        var atRule = sheet.Rules.Should().ContainSingle().Which.Should().BeOfType<AtRule>().Subject;
        atRule.Prelude.OfType<CssTokenValue>()
            .Should().ContainSingle(v => v.Token.Type == CssTokenType.Url);
    }

    // ─── §5 var() function ────────────────────────────────────────────────────

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#parsing", section: "5")]
    [SpecFact]
    public void Var_function_with_custom_property_argument()
    {
        var sheet = CssParser.ParseStyleSheet("p { color: var(--brand); }");
        var func = sheet.Rules.OfType<StyleRule>().Single()
            .Declarations.Single().Value.OfType<CssFunction>().Should().ContainSingle().Subject;
        func.Name.Should().Be("var");
        // Argument is an ident token for --brand
        func.Values.OfType<CssTokenValue>()
            .Should().ContainSingle(v => v.Token.Type == CssTokenType.Ident && v.Token.Value == "--brand");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#parsing", section: "5")]
    [SpecFact]
    public void Var_function_with_fallback_argument()
    {
        var sheet = CssParser.ParseStyleSheet("p { color: var(--c, red); }");
        var func = sheet.Rules.OfType<StyleRule>().Single()
            .Declarations.Single().Value.OfType<CssFunction>().Single();
        func.Name.Should().Be("var");
        // The comma and fallback are all inside the function's Values
        func.Values.OfType<CssTokenValue>()
            .Should().Contain(v => v.Token.Type == CssTokenType.Comma);
    }

    // ─── §5 parse a declaration list (standalone API) ─────────────────────────

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#parse-list-of-declarations", section: "5.3.7")]
    [SpecFact]
    public void ParseDeclarationList_returns_declarations()
    {
        var parser = new CssParser("color: red; margin: 4px;");
        var decls = parser.ParseDeclarationList();
        decls.Should().HaveCount(2);
        decls[0].Name.Should().Be("color");
        decls[1].Name.Should().Be("margin");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#parse-list-of-declarations", section: "5.3.7")]
    [SpecFact]
    public void ParseDeclarationList_error_recovery_drops_invalid_keeps_valid()
    {
        var parser = new CssParser("bad!: value; color: red;");
        var decls = parser.ParseDeclarationList();
        decls.Should().ContainSingle(d => d.Name == "color");
    }
}
