using System.Globalization;

namespace Tessera.Css.Tokenizer;

public sealed class CssTokenizer
{
    private readonly string _source;
    private int _position;

    public CssTokenizer(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    public static IReadOnlyList<CssToken> Tokenize(string source)
    {
        var tokenizer = new CssTokenizer(source);
        var tokens = new List<CssToken>();
        CssToken token;
        do
        {
            token = tokenizer.NextToken();
            tokens.Add(token);
        }
        while (token.Type != CssTokenType.Eof);

        return tokens;
    }

    public CssToken NextToken()
    {
        while (StartsWith("/*"))
            ConsumeComment();

        if (IsEnd)
            return new CssToken(CssTokenType.Eof);

        var c = Peek();
        if (char.IsWhiteSpace(c))
            return ConsumeWhitespace();
        if (StartsWith("<!--"))
        {
            _position += 4;
            return new CssToken(CssTokenType.Cdo);
        }
        if (StartsWith("-->"))
        {
            _position += 3;
            return new CssToken(CssTokenType.Cdc);
        }
        if (c is '"' or '\'')
            return ConsumeString(Read());
        if (WouldStartNumber())
            return ConsumeNumeric();
        if (c == '@')
        {
            Read();
            if (WouldStartIdentifier())
                return new CssToken(CssTokenType.AtKeyword, ConsumeName());
            return new CssToken(CssTokenType.Delim, Delimiter: '@');
        }
        if (c == '#')
        {
            Read();
            if (IsName(Peek()))
                return new CssToken(CssTokenType.Hash, ConsumeName());
            return new CssToken(CssTokenType.Delim, Delimiter: '#');
        }
        if (WouldStartIdentifier())
            return ConsumeIdentLike();

        return Read() switch
        {
            ':' => new CssToken(CssTokenType.Colon),
            ';' => new CssToken(CssTokenType.Semicolon),
            ',' => new CssToken(CssTokenType.Comma),
            '[' => new CssToken(CssTokenType.LeftSquare),
            ']' => new CssToken(CssTokenType.RightSquare),
            '(' => new CssToken(CssTokenType.LeftParen),
            ')' => new CssToken(CssTokenType.RightParen),
            '{' => new CssToken(CssTokenType.LeftBrace),
            '}' => new CssToken(CssTokenType.RightBrace),
            var delimiter => new CssToken(CssTokenType.Delim, Delimiter: delimiter),
        };
    }

    private CssToken ConsumeWhitespace()
    {
        var start = _position;
        while (!IsEnd && char.IsWhiteSpace(Peek()))
            _position++;
        return new CssToken(CssTokenType.Whitespace, _source[start.._position]);
    }

    private CssToken ConsumeString(char ending)
    {
        var value = new System.Text.StringBuilder();
        while (!IsEnd)
        {
            var c = Read();
            if (c == ending)
                return new CssToken(CssTokenType.String, value.ToString());
            if (c is '\n' or '\r' or '\f')
                return new CssToken(CssTokenType.BadString, value.ToString());
            if (c == '\\' && !IsEnd)
                value.Append(Read());
            else
                value.Append(c);
        }

        return new CssToken(CssTokenType.String, value.ToString());
    }

    private CssToken ConsumeNumeric()
    {
        var start = _position;
        if (Peek() is '+' or '-')
            _position++;

        ConsumeDigits();
        if (Peek() == '.' && char.IsAsciiDigit(Peek(1)))
        {
            _position++;
            ConsumeDigits();
        }

        if (Peek() is 'e' or 'E' && (char.IsAsciiDigit(Peek(1)) ||
            ((Peek(1) is '+' or '-') && char.IsAsciiDigit(Peek(2)))))
        {
            _position++;
            if (Peek() is '+' or '-')
                _position++;
            ConsumeDigits();
        }

        var numberText = _source[start.._position];
        var number = double.Parse(numberText, CultureInfo.InvariantCulture);
        if (Peek() == '%')
        {
            _position++;
            return new CssToken(CssTokenType.Percentage, Number: number);
        }

        if (WouldStartIdentifier())
        {
            var unit = ConsumeName();
            return new CssToken(CssTokenType.Dimension, Number: number, Unit: unit);
        }

        return new CssToken(CssTokenType.Number, Number: number);
    }

    private CssToken ConsumeIdentLike()
    {
        var name = ConsumeName();
        if (Peek() != '(')
            return new CssToken(CssTokenType.Ident, name);

        _position++;
        if (name.Equals("url", StringComparison.OrdinalIgnoreCase))
            return ConsumeUrl();

        return new CssToken(CssTokenType.Function, name);
    }

    private CssToken ConsumeUrl()
    {
        while (char.IsWhiteSpace(Peek()))
            _position++;

        if (Peek() is '"' or '\'')
            return new CssToken(CssTokenType.Function, "url");

        var start = _position;
        while (!IsEnd && Peek() != ')')
        {
            if (Peek() is '"' or '\'' or '(' || char.IsWhiteSpace(Peek()))
            {
                while (!IsEnd && Peek() != ')')
                    _position++;
                if (!IsEnd)
                    _position++;
                return new CssToken(CssTokenType.BadUrl);
            }

            _position++;
        }

        var value = _source[start.._position];
        if (!IsEnd)
            _position++;
        return new CssToken(CssTokenType.Url, value);
    }

    private string ConsumeName()
    {
        var start = _position;
        while (IsName(Peek()))
            _position++;
        return _source[start.._position];
    }

    private void ConsumeDigits()
    {
        while (char.IsAsciiDigit(Peek()))
            _position++;
    }

    private void ConsumeComment()
    {
        _position += 2;
        var end = _source.IndexOf("*/", _position, StringComparison.Ordinal);
        _position = end < 0 ? _source.Length : end + 2;
    }

    private bool WouldStartIdentifier()
    {
        var c1 = Peek();
        var c2 = Peek(1);
        if (c1 == '-')
            return IsNameStart(c2) || c2 == '-';
        return IsNameStart(c1);
    }

    private bool WouldStartNumber()
    {
        var c1 = Peek();
        var c2 = Peek(1);
        var c3 = Peek(2);
        return c1 switch
        {
            '+' or '-' => char.IsAsciiDigit(c2) || (c2 == '.' && char.IsAsciiDigit(c3)),
            '.' => char.IsAsciiDigit(c2),
            _ => char.IsAsciiDigit(c1),
        };
    }

    private bool StartsWith(string value)
        => _position + value.Length <= _source.Length &&
           string.CompareOrdinal(_source, _position, value, 0, value.Length) == 0;

    private bool IsEnd => _position >= _source.Length;

    private char Peek(int offset = 0)
        => _position + offset < _source.Length ? _source[_position + offset] : '\0';

    private char Read() => _source[_position++];

    private static bool IsNameStart(char c)
        => char.IsAsciiLetter(c) || c == '_' || c >= 0x80;

    private static bool IsName(char c)
        => IsNameStart(c) || char.IsAsciiDigit(c) || c == '-';
}
