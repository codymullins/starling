using System.Globalization;
using System.Text;

namespace Tessera.Js.Lex;

/// <summary>
/// ECMAScript lexer. Pull-based: <see cref="Next"/> returns the next token
/// (or sticky <see cref="JsTokenKind.EndOfFile"/>); <see cref="Peek"/> for
/// one-token lookahead.
/// </summary>
/// <remarks>
/// <para>
/// First-cut implementation (wp:M3-01-js-lexer). Covers ES2024 lexical
/// grammar minus three context-sensitive pieces deferred to follow-up:
/// </para>
/// <list type="bullet">
///   <item>Template literals (<c>`...${...}`</c>) — need parser-driven state.</item>
///   <item>RegExp literals (<c>/foo/gi</c>) — disambiguated from division by
///         the previous token's grammatical position; that's a parser hook.</item>
///   <item>Full Unicode IdentifierStart / IdentifierPart classification —
///         uses a permissive ASCII + non-ASCII-letters subset for now.</item>
/// </list>
/// <para>
/// Reserved-word categories are first-class <see cref="JsTokenKind"/> values
/// so the parser doesn't have to re-match identifier text. Contextual
/// keywords (<c>let</c>, <c>async</c>, <c>await</c>, <c>get</c>, <c>set</c>,
/// <c>of</c>, <c>from</c>, <c>as</c>, <c>static</c>, <c>target</c>,
/// <c>meta</c>) come out as <see cref="JsTokenKind.Identifier"/>; the parser
/// disambiguates based on position.
/// </para>
/// </remarks>
public sealed class JsLexer
{
    private readonly string _src;
    private readonly IJsLexErrorSink _errors;
    private int _i;
    private int _line = 1;
    private int _col = 1;
    private JsToken? _peeked;
    private bool _precedingLineTerm;

    public JsLexer(string source, IJsLexErrorSink? errors = null)
    {
        _src = source ?? throw new ArgumentNullException(nameof(source));
        _errors = errors ?? IJsLexErrorSink.Null;
    }

    /// <summary>Return the next token, advancing the stream. EOF is sticky.</summary>
    public JsToken Next()
    {
        if (_peeked is { } p) { _peeked = null; return p; }
        return Scan();
    }

    /// <summary>One-token lookahead.</summary>
    public JsToken Peek()
    {
        _peeked ??= Scan();
        return _peeked.Value;
    }

    /// <summary>Drain to a list. Useful for tests; not for production parsing.</summary>
    public List<JsToken> Drain()
    {
        var tokens = new List<JsToken>();
        while (true)
        {
            var t = Next();
            tokens.Add(t);
            if (t.Kind == JsTokenKind.EndOfFile) return tokens;
        }
    }

    // -----------------------------------------------------------------------
    // Core scan loop
    // -----------------------------------------------------------------------
    private JsToken Scan()
    {
        SkipWhitespaceAndComments();
        var start = CurrentPos();
        var precededByLT = _precedingLineTerm;
        _precedingLineTerm = false;

        if (_i >= _src.Length)
            return MakeToken(JsTokenKind.EndOfFile, "", start, start, precededByLT);

        var c = _src[_i];

        // Identifier / keyword
        if (IsIdStart(c))
            return ScanIdentifier(start, precededByLT);

        // Numeric literal
        if (c >= '0' && c <= '9')
            return ScanNumber(start, precededByLT);

        // String literal
        if (c == '"' || c == '\'')
            return ScanString(c, start, precededByLT);

        // Punctuator
        return ScanPunctuator(start, precededByLT);
    }

    // -----------------------------------------------------------------------
    // Whitespace, line terminators, comments — §12.2 / §12.3 / §12.4
    // -----------------------------------------------------------------------
    private void SkipWhitespaceAndComments()
    {
        while (_i < _src.Length)
        {
            var c = _src[_i];
            if (IsWhitespace(c)) { Advance(); continue; }
            if (IsLineTerminator(c))
            {
                _precedingLineTerm = true;
                // CRLF counts as one line break.
                if (c == '\r' && _i + 1 < _src.Length && _src[_i + 1] == '\n')
                    AdvanceRaw();
                _i++;
                _line++;
                _col = 1;
                continue;
            }
            if (c == '/' && _i + 1 < _src.Length)
            {
                var next = _src[_i + 1];
                if (next == '/') { SkipLineComment(); continue; }
                if (next == '*') { SkipBlockComment(); continue; }
            }
            break;
        }
    }

    private void SkipLineComment()
    {
        // Already at "//".
        Advance(); Advance();
        while (_i < _src.Length && !IsLineTerminator(_src[_i])) Advance();
    }

    private void SkipBlockComment()
    {
        var start = CurrentPos();
        Advance(); Advance(); // skip "/*"
        while (_i < _src.Length)
        {
            if (_src[_i] == '*' && _i + 1 < _src.Length && _src[_i + 1] == '/')
            {
                Advance(); Advance();
                return;
            }
            if (IsLineTerminator(_src[_i]))
            {
                _precedingLineTerm = true;
                if (_src[_i] == '\r' && _i + 1 < _src.Length && _src[_i + 1] == '\n') AdvanceRaw();
                _i++; _line++; _col = 1;
            }
            else Advance();
        }
        _errors.Report(JsLexError.UnterminatedComment, start, "block comment without */");
    }

    // -----------------------------------------------------------------------
    // Identifier / keyword
    // -----------------------------------------------------------------------
    private JsToken ScanIdentifier(JsPosition start, bool precededByLT)
    {
        var sb = new StringBuilder();
        while (_i < _src.Length && IsIdPart(_src[_i]))
        {
            sb.Append(_src[_i]);
            Advance();
        }
        var lex = sb.ToString();
        var kind = KeywordLookup(lex);
        var end = CurrentPos();
        return MakeToken(kind, lex, start, end, precededByLT,
            kind == JsTokenKind.BooleanLiteral ? lex == "true"
                : kind == JsTokenKind.NullLiteral ? (object?)null
                : null);
    }

    private static JsTokenKind KeywordLookup(string s) => s switch
    {
        "break"      => JsTokenKind.Break,
        "case"       => JsTokenKind.Case,
        "catch"      => JsTokenKind.Catch,
        "class"      => JsTokenKind.Class,
        "const"      => JsTokenKind.Const,
        "continue"   => JsTokenKind.Continue,
        "debugger"   => JsTokenKind.Debugger,
        "default"    => JsTokenKind.Default,
        "delete"     => JsTokenKind.Delete,
        "do"         => JsTokenKind.Do,
        "else"       => JsTokenKind.Else,
        "enum"       => JsTokenKind.Enum,
        "export"     => JsTokenKind.Export,
        "extends"    => JsTokenKind.Extends,
        "false"      => JsTokenKind.BooleanLiteral,
        "finally"    => JsTokenKind.Finally,
        "for"        => JsTokenKind.For,
        "function"   => JsTokenKind.Function,
        "if"         => JsTokenKind.If,
        "import"     => JsTokenKind.Import,
        "in"         => JsTokenKind.In,
        "instanceof" => JsTokenKind.Instanceof,
        "new"        => JsTokenKind.New,
        "null"       => JsTokenKind.NullLiteral,
        "return"     => JsTokenKind.Return,
        "super"      => JsTokenKind.Super,
        "switch"     => JsTokenKind.Switch,
        "this"       => JsTokenKind.This,
        "throw"      => JsTokenKind.Throw,
        "true"       => JsTokenKind.BooleanLiteral,
        "try"        => JsTokenKind.Try,
        "typeof"     => JsTokenKind.Typeof,
        "var"        => JsTokenKind.Var,
        "void"       => JsTokenKind.Void,
        "while"      => JsTokenKind.While,
        "with"       => JsTokenKind.With,
        "yield"      => JsTokenKind.Yield,
        _            => JsTokenKind.Identifier,
    };

    // -----------------------------------------------------------------------
    // Numeric literal — §12.9.3
    // -----------------------------------------------------------------------
    private JsToken ScanNumber(JsPosition start, bool precededByLT)
    {
        var begin = _i;
        var c = _src[_i];

        // Detect hex, binary, octal prefixes.
        if (c == '0' && _i + 1 < _src.Length)
        {
            var p = _src[_i + 1];
            if (p == 'x' || p == 'X')
                return ScanRadixNumber(start, precededByLT, begin, radix: 16);
            if (p == 'b' || p == 'B')
                return ScanRadixNumber(start, precededByLT, begin, radix: 2);
            if (p == 'o' || p == 'O')
                return ScanRadixNumber(start, precededByLT, begin, radix: 8);
        }

        // Decimal: digits [. digits] [eE [+-]? digits] [n]?
        while (_i < _src.Length && IsAsciiDigit(_src[_i])) Advance();
        var isInteger = true;
        if (_i < _src.Length && _src[_i] == '.')
        {
            isInteger = false;
            Advance();
            while (_i < _src.Length && IsAsciiDigit(_src[_i])) Advance();
        }
        if (_i < _src.Length && (_src[_i] == 'e' || _src[_i] == 'E'))
        {
            isInteger = false;
            Advance();
            if (_i < _src.Length && (_src[_i] == '+' || _src[_i] == '-')) Advance();
            if (_i >= _src.Length || !IsAsciiDigit(_src[_i]))
                _errors.Report(JsLexError.InvalidNumericLiteral, start, "exponent has no digits");
            while (_i < _src.Length && IsAsciiDigit(_src[_i])) Advance();
        }

        // BigInt suffix `n` only legal on pure integers.
        if (isInteger && _i < _src.Length && _src[_i] == 'n')
        {
            var digitsBi = _src[begin.._i];
            Advance(); // consume n
            return MakeToken(JsTokenKind.BigIntLiteral, _src[begin.._i],
                start, CurrentPos(), precededByLT, digitsBi);
        }

        var lex = _src[begin.._i];
        if (!double.TryParse(lex, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            _errors.Report(JsLexError.InvalidNumericLiteral, start, lex);
            value = double.NaN;
        }
        return MakeToken(JsTokenKind.NumericLiteral, lex, start, CurrentPos(), precededByLT, value);
    }

    private JsToken ScanRadixNumber(JsPosition start, bool precededByLT, int begin, int radix)
    {
        Advance(); Advance(); // 0x / 0b / 0o
        var digitStart = _i;
        while (_i < _src.Length && IsDigitInRadix(_src[_i], radix)) Advance();
        if (_i == digitStart)
            _errors.Report(JsLexError.InvalidNumericLiteral, start, "radix literal has no digits");
        var isInteger = true; // always for these prefixes
        // BigInt suffix permitted on integer radix forms too.
        if (_i < _src.Length && _src[_i] == 'n')
        {
            var digitsBi = _src[digitStart.._i];
            Advance();
            return MakeToken(JsTokenKind.BigIntLiteral, _src[begin.._i],
                start, CurrentPos(), precededByLT, digitsBi);
        }
        var digits = _src[digitStart.._i];
        double value;
        try
        {
            value = (double)Convert.ToInt64(digits, radix);
        }
        catch
        {
            _errors.Report(JsLexError.InvalidNumericLiteral, start, _src[begin.._i]);
            value = double.NaN;
        }
        _ = isInteger; // silence unused
        return MakeToken(JsTokenKind.NumericLiteral, _src[begin.._i],
            start, CurrentPos(), precededByLT, value);
    }

    private static bool IsDigitInRadix(char c, int radix) => radix switch
    {
        2 => c == '0' || c == '1',
        8 => c >= '0' && c <= '7',
        16 => IsAsciiDigit(c) || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f'),
        _ => false,
    };

    // -----------------------------------------------------------------------
    // String literal — §12.9.4
    // -----------------------------------------------------------------------
    private JsToken ScanString(char quote, JsPosition start, bool precededByLT)
    {
        var begin = _i;
        Advance(); // skip opening quote
        var sb = new StringBuilder();
        while (_i < _src.Length)
        {
            var c = _src[_i];
            if (c == quote)
            {
                Advance();
                return MakeToken(JsTokenKind.StringLiteral, _src[begin.._i],
                    start, CurrentPos(), precededByLT, sb.ToString());
            }
            if (IsLineTerminator(c))
            {
                _errors.Report(JsLexError.UnterminatedString, start,
                    "string literal contains unescaped line terminator");
                return MakeToken(JsTokenKind.Invalid, _src[begin.._i],
                    start, CurrentPos(), precededByLT, sb.ToString());
            }
            if (c == '\\')
            {
                Advance();
                if (_i >= _src.Length)
                {
                    _errors.Report(JsLexError.UnterminatedString, start, "string ends in backslash");
                    break;
                }
                sb.Append(ScanEscape(start));
                continue;
            }
            sb.Append(c);
            Advance();
        }
        _errors.Report(JsLexError.UnterminatedString, start, "closing quote not found");
        return MakeToken(JsTokenKind.Invalid, _src[begin.._i],
            start, CurrentPos(), precededByLT, sb.ToString());
    }

    private string ScanEscape(JsPosition start)
    {
        var e = _src[_i];
        Advance();
        switch (e)
        {
            case 'n': return "\n";
            case 'r': return "\r";
            case 't': return "\t";
            case 'b': return "\b";
            case 'f': return "\f";
            case 'v': return "\v";
            case '0' when _i >= _src.Length || !IsAsciiDigit(_src[_i]): return "\0";
            case '\'': return "'";
            case '"': return "\"";
            case '\\': return "\\";
            case '\n': return "";     // line continuation
            case '\r':
                if (_i < _src.Length && _src[_i] == '\n') Advance();
                return "";
            case 'x':
                return ScanHexEscape(start, 2);
            case 'u':
                if (_i < _src.Length && _src[_i] == '{')
                {
                    Advance();
                    var sb = new StringBuilder();
                    while (_i < _src.Length && _src[_i] != '}')
                    {
                        if (!IsHex(_src[_i]))
                        {
                            _errors.Report(JsLexError.InvalidUnicodeEscape, start, "expected hex digit");
                            break;
                        }
                        sb.Append(_src[_i]);
                        Advance();
                    }
                    if (_i < _src.Length && _src[_i] == '}') Advance();
                    if (sb.Length == 0) return "";
                    var code = Convert.ToInt32(sb.ToString(), 16);
                    if (code > 0x10FFFF)
                    {
                        _errors.Report(JsLexError.InvalidUnicodeEscape, start, "code point out of range");
                        return "�";
                    }
                    return char.ConvertFromUtf32(code);
                }
                return ScanHexEscape(start, 4);
            default:
                return e.ToString();
        }
    }

    private string ScanHexEscape(JsPosition start, int digits)
    {
        if (_i + digits > _src.Length)
        {
            _errors.Report(JsLexError.InvalidEscape, start, "truncated hex escape");
            return "�";
        }
        var slice = _src.Substring(_i, digits);
        foreach (var ch in slice)
        {
            if (!IsHex(ch))
            {
                _errors.Report(JsLexError.InvalidEscape, start, "bad hex digit");
                return "�";
            }
        }
        for (var k = 0; k < digits; k++) Advance();
        return ((char)Convert.ToInt32(slice, 16)).ToString();
    }

    // -----------------------------------------------------------------------
    // Punctuators — §12.8.1
    // -----------------------------------------------------------------------
    private JsToken ScanPunctuator(JsPosition start, bool precededByLT)
    {
        var c = _src[_i];
        char p1 = _i + 1 < _src.Length ? _src[_i + 1] : '\0';
        char p2 = _i + 2 < _src.Length ? _src[_i + 2] : '\0';

        // 3-char punctuators
        if (c == '=' && p1 == '=' && p2 == '=') return Punct(JsTokenKind.EqEqEq, 3, start, precededByLT);
        if (c == '!' && p1 == '=' && p2 == '=') return Punct(JsTokenKind.BangEqEq, 3, start, precededByLT);
        if (c == '<' && p1 == '<' && p2 == '=') return Punct(JsTokenKind.LtLtEq, 3, start, precededByLT);
        if (c == '>' && p1 == '>' && p2 == '>')
        {
            char p3 = _i + 3 < _src.Length ? _src[_i + 3] : '\0';
            if (p3 == '=') return Punct(JsTokenKind.GtGtGtEq, 4, start, precededByLT);
            return Punct(JsTokenKind.GtGtGt, 3, start, precededByLT);
        }
        if (c == '>' && p1 == '>' && p2 == '=') return Punct(JsTokenKind.GtGtEq, 3, start, precededByLT);
        if (c == '*' && p1 == '*' && p2 == '=') return Punct(JsTokenKind.StarStarEq, 3, start, precededByLT);
        if (c == '&' && p1 == '&' && p2 == '=') return Punct(JsTokenKind.AmpAmpEq, 3, start, precededByLT);
        if (c == '|' && p1 == '|' && p2 == '=') return Punct(JsTokenKind.PipePipeEq, 3, start, precededByLT);
        if (c == '?' && p1 == '?' && p2 == '=') return Punct(JsTokenKind.QuestionQuestionEq, 3, start, precededByLT);
        if (c == '.' && p1 == '.' && p2 == '.') return Punct(JsTokenKind.Ellipsis, 3, start, precededByLT);

        // 2-char punctuators
        if (c == '=' && p1 == '=') return Punct(JsTokenKind.EqEq, 2, start, precededByLT);
        if (c == '!' && p1 == '=') return Punct(JsTokenKind.BangEq, 2, start, precededByLT);
        if (c == '<' && p1 == '=') return Punct(JsTokenKind.LtEq, 2, start, precededByLT);
        if (c == '>' && p1 == '=') return Punct(JsTokenKind.GtEq, 2, start, precededByLT);
        if (c == '<' && p1 == '<') return Punct(JsTokenKind.LtLt, 2, start, precededByLT);
        if (c == '>' && p1 == '>') return Punct(JsTokenKind.GtGt, 2, start, precededByLT);
        if (c == '+' && p1 == '+') return Punct(JsTokenKind.PlusPlus, 2, start, precededByLT);
        if (c == '-' && p1 == '-') return Punct(JsTokenKind.MinusMinus, 2, start, precededByLT);
        if (c == '*' && p1 == '*') return Punct(JsTokenKind.StarStar, 2, start, precededByLT);
        if (c == '&' && p1 == '&') return Punct(JsTokenKind.AmpAmp, 2, start, precededByLT);
        if (c == '|' && p1 == '|') return Punct(JsTokenKind.PipePipe, 2, start, precededByLT);
        if (c == '?' && p1 == '?') return Punct(JsTokenKind.QuestionQuestion, 2, start, precededByLT);
        if (c == '?' && p1 == '.') return Punct(JsTokenKind.QuestionDot, 2, start, precededByLT);
        if (c == '=' && p1 == '>') return Punct(JsTokenKind.Arrow, 2, start, precededByLT);
        if (c == '+' && p1 == '=') return Punct(JsTokenKind.PlusEq, 2, start, precededByLT);
        if (c == '-' && p1 == '=') return Punct(JsTokenKind.MinusEq, 2, start, precededByLT);
        if (c == '*' && p1 == '=') return Punct(JsTokenKind.StarEq, 2, start, precededByLT);
        if (c == '/' && p1 == '=') return Punct(JsTokenKind.SlashEq, 2, start, precededByLT);
        if (c == '%' && p1 == '=') return Punct(JsTokenKind.PercentEq, 2, start, precededByLT);
        if (c == '&' && p1 == '=') return Punct(JsTokenKind.AmpEq, 2, start, precededByLT);
        if (c == '|' && p1 == '=') return Punct(JsTokenKind.PipeEq, 2, start, precededByLT);
        if (c == '^' && p1 == '=') return Punct(JsTokenKind.CaretEq, 2, start, precededByLT);

        // 1-char punctuators
        var k = c switch
        {
            '{' => JsTokenKind.LBrace,
            '}' => JsTokenKind.RBrace,
            '(' => JsTokenKind.LParen,
            ')' => JsTokenKind.RParen,
            '[' => JsTokenKind.LBracket,
            ']' => JsTokenKind.RBracket,
            '.' => JsTokenKind.Dot,
            ';' => JsTokenKind.Semicolon,
            ',' => JsTokenKind.Comma,
            '<' => JsTokenKind.Lt,
            '>' => JsTokenKind.Gt,
            '+' => JsTokenKind.Plus,
            '-' => JsTokenKind.Minus,
            '*' => JsTokenKind.Star,
            '/' => JsTokenKind.Slash,
            '%' => JsTokenKind.Percent,
            '&' => JsTokenKind.Amp,
            '|' => JsTokenKind.Pipe,
            '^' => JsTokenKind.Caret,
            '~' => JsTokenKind.Tilde,
            '!' => JsTokenKind.Bang,
            '?' => JsTokenKind.Question,
            ':' => JsTokenKind.Colon,
            '=' => JsTokenKind.Eq,
            _ => JsTokenKind.Invalid,
        };
        if (k == JsTokenKind.Invalid)
            _errors.Report(JsLexError.InvalidCharacter, start, $"unexpected character '{c}' (U+{(int)c:X4})");
        return Punct(k, 1, start, precededByLT);
    }

    private JsToken Punct(JsTokenKind kind, int len, JsPosition start, bool precededByLT)
    {
        var lex = _src.Substring(_i, len);
        for (var k = 0; k < len; k++) Advance();
        return MakeToken(kind, lex, start, CurrentPos(), precededByLT);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------
    private JsPosition CurrentPos() => new(_line, _col, _i);

    private void Advance()
    {
        // For single-char positions (no line terminators here — those are
        // handled separately in SkipWhitespaceAndComments / SkipBlockComment).
        _col++;
        _i++;
    }

    /// <summary>Advance without bumping column — used when a CR is the first
    /// half of a CRLF and the LF half will run the normal advance path.</summary>
    private void AdvanceRaw() { _i++; }

    private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';

    private static bool IsHex(char c)
        => IsAsciiDigit(c) || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');

    private static bool IsWhitespace(char c)
        => c == ' '
        || c == '\t'
        || c == '\v'
        || c == '\f'
        || c == '\u00A0'   // NBSP
        || c == '\uFEFF';

    private static bool IsLineTerminator(char c)
        => c == '\n'
        || c == '\r'
        || c == '\u2028'   // LINE SEPARATOR
        || c == '\u2029';


    /// <summary>ASCII identifier start + non-ASCII letters per UnicodeCategory.
    /// Full IdentifierStart per spec §12.6 is more permissive; this is enough
    /// for the cases this slice covers.</summary>
    private static bool IsIdStart(char c)
    {
        if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) return true;
        if (c == '_' || c == '$') return true;
        if (c < 0x80) return false;
        var cat = CharUnicodeInfo.GetUnicodeCategory(c);
        return cat is UnicodeCategory.UppercaseLetter
            or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter
            or UnicodeCategory.OtherLetter
            or UnicodeCategory.LetterNumber;
    }

    private static bool IsIdPart(char c)
    {
        if (IsIdStart(c)) return true;
        if (c >= '0' && c <= '9') return true;
        if (c == '\u200C' || c == '\u200D') return true; // ZWNJ/ZWJ
        var cat = CharUnicodeInfo.GetUnicodeCategory(c);
        return cat is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.DecimalDigitNumber
            or UnicodeCategory.ConnectorPunctuation;
    }

    private static JsToken MakeToken(
        JsTokenKind kind, string lexeme, JsPosition start, JsPosition end,
        bool precededByLT, object? value = null)
        => new(kind, lexeme, start, end, value) { PrecededByLineTerminator = precededByLT };
}
