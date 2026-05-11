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

    public static StyleSheet ParseStyleSheet(string source) => new CssParser(source).ParseStyleSheet();

    public StyleSheet ParseStyleSheet()
        => new(_source, ConsumeRuleList(topLevel: true));

    public IReadOnlyList<CssDeclaration> ParseDeclarationList()
    {
        var declarations = new List<CssDeclaration>();
        while (!IsEnd && Current.Type != CssTokenType.RightBrace)
        {
            SkipWhitespaceAndSemicolons();
            if (Current.Type == CssTokenType.Eof || Current.Type == CssTokenType.RightBrace)
                break;

            if (Current.Type == CssTokenType.Ident && PeekNonWhitespace(1).Type == CssTokenType.Colon)
            {
                declarations.Add(ConsumeDeclaration());
                continue;
            }

            ConsumeUntil(CssTokenType.Semicolon, CssTokenType.RightBrace);
        }

        return declarations;
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
                ? ConsumeAtRule()
                : ConsumeQualifiedRule());
        }

        return rules;
    }

    private AtRule ConsumeAtRule()
    {
        var name = Current.Value;
        _position++;
        var prelude = ConsumeComponentValuesUntil(CssTokenType.Semicolon, CssTokenType.LeftBrace);
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

        var rules = ConsumeRuleList(topLevel: false);
        ConsumeIf(CssTokenType.RightBrace);
        return new AtRule(name, prelude, rules, []);
    }

    private StyleRule ConsumeQualifiedRule()
    {
        var prelude = ConsumeComponentValuesUntil(CssTokenType.LeftBrace, CssTokenType.Eof);
        if (Current.Type != CssTokenType.LeftBrace)
            return new StyleRule(prelude, []);

        _position++;
        var declarations = ParseDeclarationList();
        ConsumeIf(CssTokenType.RightBrace);
        return new StyleRule(prelude, declarations);
    }

    private CssDeclaration ConsumeDeclaration()
    {
        var name = Current.Value;
        _position++;
        ConsumeIf(CssTokenType.Colon);
        var values = ConsumeComponentValuesUntil(CssTokenType.Semicolon, CssTokenType.RightBrace);
        var important = RemoveTrailingImportant(values);
        ConsumeIf(CssTokenType.Semicolon);
        return new CssDeclaration(name, values, important);
    }

    private List<CssComponentValue> ConsumeComponentValuesUntil(params CssTokenType[] terminators)
    {
        var values = new List<CssComponentValue>();
        while (!IsEnd && !terminators.Contains(Current.Type))
        {
            if (Current.Type == CssTokenType.Whitespace)
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
        var values = ConsumeComponentValuesUntil(end, CssTokenType.Eof);
        ConsumeIf(end);
        return new CssSimpleBlock(start, values);
    }

    private CssFunction ConsumeFunction()
    {
        var name = Consume().Value;
        var values = ConsumeComponentValuesUntil(CssTokenType.RightParen, CssTokenType.Eof);
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
