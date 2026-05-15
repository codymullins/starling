using Tessera.Css.Tokenizer;

namespace Tessera.Css.Parser;

public sealed class CssParser
{
    private readonly IReadOnlyList<CssToken> _tokens;
    private readonly string _source;
    private int _position;

    public CssParser(string source)
        : this(source, CssTokenizer.Tokenize(source))
    {
    }

    public CssParser(string source, IReadOnlyList<CssToken> tokens)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(tokens);
        _source = source;
        _tokens = tokens;
    }

    public static StyleSheet ParseStyleSheet(string source, StyleOrigin origin = StyleOrigin.Author)
        => new CssParser(source).ParseStyleSheet(origin);

    public StyleSheet ParseStyleSheet(StyleOrigin origin = StyleOrigin.Author)
        => new(_source, ConsumeRuleList(topLevel: true), origin);

    public IReadOnlyList<CssDeclaration> ParseDeclarationList()
        => ParseDeclarationsAndNested(allowNesting: false).Declarations;

    private (IReadOnlyList<CssDeclaration> Declarations, IReadOnlyList<CssRule> NestedRules) ParseDeclarationsAndNested(bool allowNesting)
    {
        var declarations = new List<CssDeclaration>();
        var nestedRules = new List<CssRule>();
        while (!IsEnd && Current.Type != CssTokenType.RightBrace)
        {
            SkipWhitespaceAndSemicolons();
            if (Current.Type == CssTokenType.Eof || Current.Type == CssTokenType.RightBrace)
                break;

            if (allowNesting && Current.Type == CssTokenType.AtKeyword)
            {
                // Nested at-rule inside a style rule.
                nestedRules.Add(ConsumeAtRule(insideStyleRule: true));
                continue;
            }

            if (Current.Type == CssTokenType.Ident && PeekNonWhitespace(1).Type == CssTokenType.Colon &&
                !LooksLikeNestedRuleStart())
            {
                declarations.Add(ConsumeDeclaration());
                continue;
            }

            if (allowNesting && LooksLikeNestedRuleStart())
            {
                nestedRules.Add(ConsumeQualifiedRule(allowNested: true));
                continue;
            }

            ConsumeUntil(CssTokenType.Semicolon, CssTokenType.RightBrace);
        }

        return (declarations, nestedRules);
    }

    // A nested style rule's prelude can be a `&`-prefixed selector, a combinator, an ident type selector,
    // a class/id/`*`/`[`/`:` start, or arbitrary tokens followed by `{`. The simplest test that catches
    // all of these and excludes declarations is: scan forward to the next `{` or `;` at depth 0; if `{` wins,
    // it's a rule.
    private bool LooksLikeNestedRuleStart()
    {
        var depth = 0;
        for (var i = _position; i < _tokens.Count; i++)
        {
            var t = _tokens[i].Type;
            if (t == CssTokenType.LeftParen || t == CssTokenType.LeftSquare)
                depth++;
            else if (t == CssTokenType.RightParen || t == CssTokenType.RightSquare)
                depth = Math.Max(0, depth - 1);
            else if (depth == 0)
            {
                if (t == CssTokenType.LeftBrace) return true;
                if (t == CssTokenType.Semicolon || t == CssTokenType.RightBrace || t == CssTokenType.Eof) return false;
            }
        }
        return false;
    }

    private List<CssRule> ConsumeRuleList(bool topLevel)
    {
        var rules = new List<CssRule>();
        while (!IsEnd)
        {
            SkipWhitespaceAndSemicolons();
            if (Current.Type == CssTokenType.Eof || (!topLevel && Current.Type == CssTokenType.RightBrace))
                break;
            if (topLevel && Current.Type is CssTokenType.Cdo or CssTokenType.Cdc)
            {
                _position++;
                continue;
            }

            rules.Add(Current.Type == CssTokenType.AtKeyword
                ? ConsumeAtRule(insideStyleRule: false)
                : ConsumeQualifiedRule(allowNested: false));
        }

        return rules;
    }

    private AtRule ConsumeAtRule(bool insideStyleRule)
    {
        var name = Current.Value;
        _position++;
        var prelude = ConsumeComponentValuesUntil(
            preserveWhitespace: false,
            CssTokenType.Semicolon,
            CssTokenType.LeftBrace);
        if (Current.Type == CssTokenType.Semicolon)
        {
            _position++;
            return new AtRule(name, prelude, [], []);
        }

        if (Current.Type != CssTokenType.LeftBrace)
            return new AtRule(name, prelude, [], []);

        _position++;
        if (name.Equals("font-face", StringComparison.OrdinalIgnoreCase))
        {
            var declarations = ParseDeclarationList();
            ConsumeIf(CssTokenType.RightBrace);
            return new AtRule(name, prelude, [], declarations);
        }

        // CSS Nesting 1: when an at-rule (media/supports/layer) appears inside a style rule,
        // its body is a mix of declarations and nested rules, the same as the parent style rule.
        // We wrap bare declarations in a synthetic StyleRule with `&` as prelude so the
        // cascade walker treats them as applying to the parent selector.
        if (insideStyleRule && name is "media" or "supports" or "layer" ||
            insideStyleRule && IsConditionalAtRule(name))
        {
            var (decls, nested) = ParseDeclarationsAndNested(allowNesting: true);
            var combined = new List<CssRule>();
            if (decls.Count > 0)
                combined.Add(SyntheticAmpersandRule(decls));
            combined.AddRange(nested);
            ConsumeIf(CssTokenType.RightBrace);
            return new AtRule(name, prelude, combined, []);
        }

        var rules = ConsumeRuleList(topLevel: false);
        ConsumeIf(CssTokenType.RightBrace);
        return new AtRule(name, prelude, rules, []);
    }

    private static bool IsConditionalAtRule(string name)
        => name.Equals("media", StringComparison.OrdinalIgnoreCase)
        || name.Equals("supports", StringComparison.OrdinalIgnoreCase)
        || name.Equals("layer", StringComparison.OrdinalIgnoreCase);

    private static StyleRule SyntheticAmpersandRule(IReadOnlyList<CssDeclaration> declarations)
    {
        var prelude = new List<CssComponentValue>
        {
            new CssTokenValue(new CssToken(CssTokenType.Delim, Delimiter: '&')),
        };
        return new StyleRule(prelude, declarations, null);
    }

    private StyleRule ConsumeQualifiedRule(bool allowNested)
    {
        var prelude = ConsumeComponentValuesUntil(
            preserveWhitespace: true,
            CssTokenType.LeftBrace,
            CssTokenType.Eof);
        if (Current.Type != CssTokenType.LeftBrace)
            return new StyleRule(prelude, [], null);

        _position++;
        var (declarations, nested) = ParseDeclarationsAndNested(allowNesting: true);
        ConsumeIf(CssTokenType.RightBrace);
        return new StyleRule(prelude, declarations, nested.Count == 0 ? null : nested);
    }

    private CssDeclaration ConsumeDeclaration()
    {
        var name = Current.Value;
        _position++;
        SkipWhitespace();
        ConsumeIf(CssTokenType.Colon);
        var values = ConsumeComponentValuesUntil(
            preserveWhitespace: false,
            CssTokenType.Semicolon,
            CssTokenType.RightBrace);
        var important = RemoveTrailingImportant(values);
        ConsumeIf(CssTokenType.Semicolon);
        return new CssDeclaration(name, values, important);
    }

    private void SkipWhitespace()
    {
        while (Current.Type == CssTokenType.Whitespace)
            _position++;
    }

    private List<CssComponentValue> ConsumeComponentValuesUntil(
        bool preserveWhitespace,
        params CssTokenType[] terminators)
    {
        var values = new List<CssComponentValue>();
        while (!IsEnd && !terminators.Contains(Current.Type))
        {
            if (!preserveWhitespace && Current.Type == CssTokenType.Whitespace)
            {
                _position++;
                continue;
            }

            values.Add(ConsumeComponentValue());
        }

        return values;
    }

    private CssComponentValue ConsumeComponentValue()
    {
        if (Current.Type is CssTokenType.LeftBrace or CssTokenType.LeftParen or CssTokenType.LeftSquare)
            return ConsumeSimpleBlock();
        if (Current.Type == CssTokenType.Function)
            return ConsumeFunction();

        return new CssTokenValue(Consume());
    }

    private CssSimpleBlock ConsumeSimpleBlock()
    {
        var start = Consume().Type;
        var end = MatchingEnd(start);
        var values = ConsumeComponentValuesUntil(
            preserveWhitespace: true,
            end,
            CssTokenType.Eof);
        ConsumeIf(end);
        return new CssSimpleBlock(start, values);
    }

    private CssFunction ConsumeFunction()
    {
        var name = Consume().Value;
        var values = ConsumeComponentValuesUntil(
            preserveWhitespace: true,
            CssTokenType.RightParen,
            CssTokenType.Eof);
        ConsumeIf(CssTokenType.RightParen);
        return new CssFunction(name, values);
    }

    private void SkipWhitespaceAndSemicolons()
    {
        while (Current.Type is CssTokenType.Whitespace or CssTokenType.Semicolon)
            _position++;
    }

    private void ConsumeUntil(params CssTokenType[] terminators)
    {
        while (!IsEnd && !terminators.Contains(Current.Type))
            _position++;
        ConsumeIf(CssTokenType.Semicolon);
    }

    private bool ConsumeIf(CssTokenType type)
    {
        if (Current.Type != type)
            return false;

        _position++;
        return true;
    }

    private CssToken Consume() => _tokens[_position++];

    private CssToken PeekNonWhitespace(int offset)
    {
        var index = _position + offset;
        while (index < _tokens.Count && _tokens[index].Type == CssTokenType.Whitespace)
            index++;
        return index < _tokens.Count ? _tokens[index] : new CssToken(CssTokenType.Eof);
    }

    private CssToken Current
        => _position < _tokens.Count ? _tokens[_position] : new CssToken(CssTokenType.Eof);

    private bool IsEnd => Current.Type == CssTokenType.Eof;

    private static CssTokenType MatchingEnd(CssTokenType start) => start switch
    {
        CssTokenType.LeftBrace => CssTokenType.RightBrace,
        CssTokenType.LeftParen => CssTokenType.RightParen,
        CssTokenType.LeftSquare => CssTokenType.RightSquare,
        _ => CssTokenType.Eof,
    };

    private static bool RemoveTrailingImportant(List<CssComponentValue> values)
    {
        if (values.Count < 2 ||
            values[^2] is not CssTokenValue { Token.Type: CssTokenType.Delim, Token.Delimiter: '!' } ||
            values[^1] is not CssTokenValue { Token.Type: CssTokenType.Ident } ident ||
            !ident.Token.Value.Equals("important", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        values.RemoveRange(values.Count - 2, 2);
        return true;
    }
}
