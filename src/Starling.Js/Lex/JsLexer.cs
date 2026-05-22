using System.Globalization;
using System.Text;

namespace Starling.Js.Lex;

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
    /// <summary>§B.1.2 — set by <see cref="ScanEscape"/> when the escape it
    /// just consumed was a legacy octal escape (<c>\1</c>…<c>\377</c>) or a
    /// <c>\8</c>/<c>\9</c> NonOctalDecimalEscapeSequence. <see cref="ScanString"/>
    /// reads it to tag the resulting string token as a strict-mode error.</summary>
    private bool _lastEscapeWasLegacyOctal;

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

    /// <summary>Push a previously-consumed token back into the lookahead slot.
    /// Used by the parser when reclassifying a <c>/</c> token as the head of a
    /// regex literal (then immediately calls <see cref="ScanRegExp"/>, whose
    /// peeked-slash rollback fixes the underlying byte position).</summary>
    public void PushBack(JsToken token)
    {
        if (_peeked is not null)
            throw new InvalidOperationException("PushBack called with a token already peeked");
        _peeked = token;
    }

    /// <summary>B1b-2c — disambiguate <c>async ( ... )</c>. Look ahead at the
    /// next <c>(</c> ... <c>)</c> chunk and see whether it is followed by
    /// <c>=&gt;</c>. Returns true if so. Does not mutate parser-visible state
    /// (snapshots position + peeked token).</summary>
    /// <remarks>
    /// Called only after the parser has confirmed the current token is the
    /// <c>async</c> identifier and the peeked token is <c>LParen</c>. The
    /// scan starts at the current position, walking forward over balanced
    /// parens. Strings/templates inside are not lexed precisely — we treat
    /// them as inert characters since balancing only cares about parens.
    /// This is approximate but adequate for disambiguation.
    /// </remarks>
    public bool LookaheadIsAsyncArrow()
    {
        // Called after the parser has confirmed _current == "async" identifier
        // and _peeked == LParen. The lexer's _i pointer therefore sits just
        // past the peeked '(' character — the precondition for the shared
        // balanced-paren scan below.
        return LookaheadIsArrowFromParen(_i);
    }

    /// <summary>Disambiguate a parenthesized arrow head from an ordinary
    /// grouping/sequence expression. Given the source offset just past an
    /// opening <c>(</c>, walk balanced parens to the matching <c>)</c> and
    /// report whether <c>=&gt;</c> follows. Pure: reads <c>_src</c> only, no
    /// state mutation, so the parser can probe before committing to the
    /// arrow-parameter parse path.</summary>
    /// <remarks>
    /// Shares the approximate scan of <see cref="LookaheadIsAsyncArrow"/>:
    /// strings, templates, and comments are skipped so their inner parens
    /// don't unbalance the count. Regex literals are not lexed precisely —
    /// adequate for arrow disambiguation, matching the async variant.
    /// </remarks>
    public bool LookaheadIsArrowFromParen(int afterOpenParenOffset)
    {
        var i = afterOpenParenOffset;
        var depth = 1; // caller is positioned just inside the opening (
        for (; i < _src.Length; i++)
        {
            var c = _src[i];
            if (c == '"' || c == '\'')
            {
                var quote = c;
                i++;
                while (i < _src.Length && _src[i] != quote)
                {
                    if (_src[i] == '\\') i++;
                    i++;
                }
                continue;
            }
            if (c == '`')
            {
                i++;
                while (i < _src.Length && _src[i] != '`') i++;
                continue;
            }
            if (c == '/' && i + 1 < _src.Length && _src[i + 1] == '/')
            {
                while (i < _src.Length && _src[i] != '\n') i++;
                continue;
            }
            if (c == '/' && i + 1 < _src.Length && _src[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < _src.Length && !(_src[i] == '*' && _src[i + 1] == '/')) i++;
                i++; // skip the '/'
                continue;
            }
            if (c == '(') depth++;
            else if (c == ')')
            {
                depth--;
                if (depth == 0) { i++; break; }
            }
        }
        // Skip whitespace between ')' and a possible '=>'.
        while (i < _src.Length)
        {
            var c = _src[i];
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { i++; continue; }
            break;
        }
        return i + 1 < _src.Length && _src[i] == '=' && _src[i + 1] == '>';
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
        // §12.5 Hashbang comment — only at the very first byte of the script,
        // mirrors Node/V8/JSC. Treat as a line comment.
        if (_i == 0 && _src.Length >= 2 && _src[0] == '#' && _src[1] == '!')
        {
            while (_i < _src.Length && !IsLineTerminator(_src[_i])) Advance();
        }

        SkipWhitespaceAndComments();
        var start = CurrentPos();
        var precededByLT = _precedingLineTerm;
        _precedingLineTerm = false;

        if (_i >= _src.Length)
            return MakeToken(JsTokenKind.EndOfFile, "", start, start, precededByLT);

        var c = _src[_i];

        // Identifier / keyword — an identifier may begin with a raw
        // IdentifierStart char or a \u escape that decodes to one (§12.6).
        if (IsIdStart(c) || (c == '\\' && StartsIdentifierEscape(_i)))
            return ScanIdentifier(start, precededByLT);

        // Private identifier — #name, only valid in class bodies (parser
        // enforces). The name part likewise allows a leading \u escape.
        if (c == '#' && _i + 1 < _src.Length
            && (IsIdStart(_src[_i + 1]) || (_src[_i + 1] == '\\' && StartsIdentifierEscape(_i + 1))))
            return ScanPrivateIdentifier(start, precededByLT);

        // Numeric literal
        if (c >= '0' && c <= '9')
            return ScanNumber(start, precededByLT);

        // String literal
        if (c == '"' || c == '\'')
            return ScanString(c, start, precededByLT);

        // Template literal — parser-driven thereafter for substitutions.
        if (c == '`')
            return ScanTemplateBody(start, precededByLT, head: true);

        // Punctuator
        return ScanPunctuator(start, precededByLT);
    }

    /// <summary>Parser entry point for the start of a regex literal — called
    /// when the previous token's grammatical position permits a regex. Assumes
    /// the next character is <c>/</c>. Emits a single
    /// <see cref="JsTokenKind.RegExpLiteral"/> token whose <c>Value</c> is the
    /// tuple <c>(pattern: string, flags: string)</c>.</summary>
    public JsToken ScanRegExp()
    {
        if (_peeked is { } pq && (pq.Kind == JsTokenKind.Slash || pq.Kind == JsTokenKind.SlashEq))
        {
            // Roll back the peeked `/` (or `/=`) — we're going to re-lex it
            // as a regex; the lexeme that follows the leading slash is part
            // of the regex pattern.
            _i -= pq.Lexeme.Length;
            _col -= pq.Lexeme.Length;
            _peeked = null;
        }
        SkipWhitespaceAndComments();
        var start = CurrentPos();
        var precededByLT = _precedingLineTerm;
        _precedingLineTerm = false;

        if (_i >= _src.Length || _src[_i] != '/')
            throw new InvalidOperationException("ScanRegExp called at non-slash position");

        Advance(); // opening /
        var patternStart = _i;
        var inClass = false;
        while (_i < _src.Length)
        {
            var c = _src[_i];
            if (IsLineTerminator(c))
            {
                _errors.Report(JsLexError.UnterminatedRegExp, start, "unterminated regular expression");
                return MakeToken(JsTokenKind.Invalid, _src[start.Offset.._i], start, CurrentPos(), precededByLT);
            }
            if (c == '\\')
            {
                Advance();
                if (_i < _src.Length) Advance();
                continue;
            }
            if (c == '[') { inClass = true; Advance(); continue; }
            if (c == ']') { inClass = false; Advance(); continue; }
            if (c == '/' && !inClass) break;
            Advance();
        }
        if (_i >= _src.Length || _src[_i] != '/')
        {
            _errors.Report(JsLexError.UnterminatedRegExp, start, "unterminated regular expression");
            return MakeToken(JsTokenKind.Invalid, _src[start.Offset.._i], start, CurrentPos(), precededByLT);
        }
        var pattern = _src[patternStart.._i];
        Advance(); // closing /
        var flagsStart = _i;
        while (_i < _src.Length && IsIdPart(_src[_i])) Advance();
        var flags = _src[flagsStart.._i];
        var lex = _src[start.Offset.._i];
        return MakeToken(JsTokenKind.RegExpLiteral, lex, start, CurrentPos(), precededByLT, (pattern, flags));
    }

    /// <summary>Parser entry point after a <c>}</c> closes a substitution in a
    /// template literal. Continues scanning the template body — emits either
    /// <see cref="JsTokenKind.TemplateMiddle"/> (another <c>${</c> follows) or
    /// <see cref="JsTokenKind.TemplateTail"/> (closing backtick).</summary>
    public JsToken ScanTemplateContinuation()
    {
        _peeked = null;
        var start = CurrentPos();
        return ScanTemplateBody(start, precededByLT: false, head: false);
    }

    private JsToken ScanTemplateBody(JsPosition start, bool precededByLT, bool head)
    {
        // Caller positioned us at either the opening backtick (head=true) or
        // the character immediately after the `}` of a ${…} substitution.
        if (head)
        {
            if (_i >= _src.Length || _src[_i] != '`')
                throw new InvalidOperationException("template head called at non-backtick");
            Advance();
        }
        var begin = _i;
        var sb = new StringBuilder();
        while (_i < _src.Length)
        {
            var c = _src[_i];
            if (c == '`')
            {
                Advance();
                var kind = head ? JsTokenKind.TemplateNoSubstitution : JsTokenKind.TemplateTail;
                return MakeToken(kind, _src[begin.._i], start, CurrentPos(), precededByLT, sb.ToString());
            }
            if (c == '$' && _i + 1 < _src.Length && _src[_i + 1] == '{')
            {
                Advance(); Advance();
                var kind = head ? JsTokenKind.TemplateHead : JsTokenKind.TemplateMiddle;
                return MakeToken(kind, _src[begin.._i], start, CurrentPos(), precededByLT, sb.ToString());
            }
            if (c == '\\')
            {
                Advance();
                if (_i >= _src.Length) break;
                // Line continuation \<LineTerminator> is dropped.
                if (IsLineTerminator(_src[_i]))
                {
                    if (_src[_i] == '\r' && _i + 1 < _src.Length && _src[_i + 1] == '\n') AdvanceRaw();
                    _i++; _line++; _col = 1; _precedingLineTerm = true;
                    continue;
                }
                sb.Append(ScanEscape(start));
                continue;
            }
            if (IsLineTerminator(c))
            {
                // Raw newlines are legal inside templates; track them for ASI.
                _precedingLineTerm = true;
                if (c == '\r' && _i + 1 < _src.Length && _src[_i + 1] == '\n') AdvanceRaw();
                sb.Append('\n');
                _i++; _line++; _col = 1;
                continue;
            }
            sb.Append(c);
            Advance();
        }
        _errors.Report(JsLexError.UnterminatedTemplate, start, "unterminated template literal");
        return MakeToken(JsTokenKind.Invalid, _src[begin.._i], start, CurrentPos(), precededByLT, sb.ToString());
    }

    private JsToken ScanPrivateIdentifier(JsPosition start, bool precededByLT)
    {
        var begin = _i;
        Advance(); // '#'
        var sb = new StringBuilder("#");
        // The #-name allows the same \u escapes as a plain identifier (§12.6).
        ScanIdentifierChars(sb, start);
        var lex = sb.ToString();
        return MakeToken(JsTokenKind.PrivateIdentifier, _src[begin.._i], start, CurrentPos(), precededByLT, lex);
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
        _ = ScanIdentifierChars(sb, start);
        var lex = sb.ToString();
        // The token keeps its keyword kind even when written with a \u escape, so
        // an escaped reserved word stays usable as an IdentifierName (property /
        // member name — `a.if`, `{ if: 1 }`) while the parser still
        // rejects a keyword-kind token where a BindingIdentifier / reference is
        // required (`var if` → SyntaxError), per §12.7.2. A non-reserved
        // escaped name resolves to a plain Identifier.
        var kind = KeywordLookup(lex);
        var end = CurrentPos();
        return MakeToken(kind, lex, start, end, precededByLT,
            kind == JsTokenKind.BooleanLiteral ? lex == "true"
                : kind == JsTokenKind.NullLiteral ? (object?)null
                : null);
    }

    /// <summary>Consume IdentifierStart followed by IdentifierPart* into
    /// <paramref name="sb"/>, decoding any <c>\u</c> escapes (§12.6). Returns
    /// true if at least one escape was used. Stops at the first char that is
    /// neither a raw IdentifierPart nor a valid identifier escape.</summary>
    private bool ScanIdentifierChars(StringBuilder sb, JsPosition start)
    {
        var hasEscape = false;
        var first = true;
        while (_i < _src.Length)
        {
            var c = _src[_i];
            if (c == '\\')
            {
                var cp = PeekUnicodeEscape(_i, out var len);
                if (cp < 0 || (first ? !IsIdStartCp(cp) : !IsIdPartCp(cp)))
                {
                    _errors.Report(JsLexError.InvalidUnicodeEscape, start,
                        "invalid unicode escape in identifier");
                    break;
                }
                sb.Append(char.ConvertFromUtf32(cp));
                for (var k = 0; k < len; k++) Advance();
                hasEscape = true;
            }
            else if (first ? IsIdStart(c) : IsIdPart(c))
            {
                sb.Append(c);
                Advance();
            }
            else break;
            first = false;
        }
        return hasEscape;
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
        // §12.9.3 / B.1.2 — a literal that starts with `0` immediately followed
        // by a decimal digit is either a LegacyOctalIntegerLiteral (`0123`) or a
        // NonOctalDecimalIntegerLiteral (`08`, `09`). Both are strict-mode
        // SyntaxErrors. Tag the token so the parser can raise the error when the
        // surrounding scope is strict. A leading-zero literal with a `.` or `e`
        // (e.g. `0.5`, `0e3`) is an ordinary DecimalLiteral and not tagged.
        var legacyOctal = isInteger && lex.Length >= 2 && lex[0] == '0' && IsAsciiDigit(lex[1]);
        // Legacy octal literals (`010`) denote a base-8 value; .NET parses the
        // lexeme as decimal above (`010` → 10). Recompute octal-style when every
        // digit is 0-7 so the runtime sees the spec value in sloppy mode.
        if (legacyOctal && AllOctalDigits(lex))
        {
            value = 0;
            for (var k = 0; k < lex.Length; k++) value = value * 8 + (lex[k] - '0');
        }
        return MakeToken(JsTokenKind.NumericLiteral, lex, start, CurrentPos(), precededByLT, value, legacyOctal);
    }

    private static bool AllOctalDigits(string s)
    {
        foreach (var ch in s) if (ch < '0' || ch > '7') return false;
        return true;
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

    /// <summary>
    /// Scans a numeric literal that begins with a decimal point, e.g. <c>.5</c>,
    /// <c>.25e3</c>, <c>.0</c>.  ECMAScript §12.9.3 <c>DecimalLiteral</c>
    /// production: <c>. DecimalDigits ExponentPart?</c>.
    /// Called only when the current character is <c>.</c> and the lookahead is
    /// an ASCII digit — the caller (ScanPunctuator) has already verified this.
    /// </summary>
    private JsToken ScanLeadingDotNumber(JsPosition start, bool precededByLT)
    {
        var begin = _i;
        Advance(); // consume the '.'
        // Fractional digits (guaranteed at least one by caller's lookahead check).
        while (_i < _src.Length && IsAsciiDigit(_src[_i])) Advance();
        // Optional exponent part: [eE] [+-]? Digits
        if (_i < _src.Length && (_src[_i] == 'e' || _src[_i] == 'E'))
        {
            Advance();
            if (_i < _src.Length && (_src[_i] == '+' || _src[_i] == '-')) Advance();
            if (_i >= _src.Length || !IsAsciiDigit(_src[_i]))
                _errors.Report(JsLexError.InvalidNumericLiteral, start, "exponent has no digits");
            while (_i < _src.Length && IsAsciiDigit(_src[_i])) Advance();
        }
        var lex = _src[begin.._i];
        if (!double.TryParse(lex, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            _errors.Report(JsLexError.InvalidNumericLiteral, start, lex);
            value = double.NaN;
        }
        return MakeToken(JsTokenKind.NumericLiteral, lex, start, CurrentPos(), precededByLT, value);
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
        var legacyOctal = false; // §B.1.2 — a legacy octal / \8 / \9 escape was seen
        while (_i < _src.Length)
        {
            var c = _src[_i];
            if (c == quote)
            {
                Advance();
                return MakeToken(JsTokenKind.StringLiteral, _src[begin.._i],
                    start, CurrentPos(), precededByLT, sb.ToString(), legacyOctal);
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
                if (_lastEscapeWasLegacyOctal) legacyOctal = true;
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
        _lastEscapeWasLegacyOctal = false;
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
            // §B.1.2 LegacyOctalEscapeSequence — `\` followed by an octal digit
            // (incl. `\0` followed by another digit). Decode the octal value
            // (sloppy semantics) and tag it as a strict-mode error.
            case >= '0' and <= '7':
                return ScanLegacyOctalEscape(e);
            // §B.1.2 NonOctalDecimalEscapeSequence — `\8` / `\9`. The value is
            // just the digit, but it is a strict-mode error.
            case '8':
            case '9':
                _lastEscapeWasLegacyOctal = true;
                return e.ToString();
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

    /// <summary>§B.1.2 LegacyOctalEscapeSequence — decode <c>\NNN</c> where the
    /// first octal digit <paramref name="first"/> has already been consumed.
    /// One to three octal digits: if the first digit is 0-3 up to three digits
    /// follow; if 4-7 only one more. Sets <see cref="_lastEscapeWasLegacyOctal"/>
    /// so the surrounding string is flagged a strict-mode error.</summary>
    private string ScanLegacyOctalEscape(char first)
    {
        _lastEscapeWasLegacyOctal = true;
        var value = first - '0';
        var maxMore = first <= '3' ? 2 : 1;
        for (var k = 0; k < maxMore; k++)
        {
            if (_i >= _src.Length || _src[_i] < '0' || _src[_i] > '7') break;
            value = value * 8 + (_src[_i] - '0');
            Advance();
        }
        return ((char)value).ToString();
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

        // §12.9.3 DecimalLiteral — ". DecimalDigits ExponentPart?" — a numeric
        // literal may start with a decimal point when followed by an ASCII digit.
        // Must be checked BEFORE the '...' three-char check so that the three-dot
        // case (p1 == '.' && p2 == '.') still falls through to Ellipsis below.
        if (c == '.' && p1 >= '0' && p1 <= '9')
            return ScanLeadingDotNumber(start, precededByLT);

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
        // §12.10 OptionalChainingPunctuator: `?.` is optional-chaining ONLY when
        // not immediately followed by a decimal digit, so `x ? .5 : y` stays a
        // conditional with a leading-dot number, and `a?.b` / `a?.[i]` / `a?.(x)`
        // still chain. `?.` + digit falls through to a `?` Question token.
        if (c == '?' && p1 == '.' && !(p2 >= '0' && p2 <= '9'))
            return Punct(JsTokenKind.QuestionDot, 2, start, precededByLT);
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
        if (IsOtherIdStart(c)) return true; // \u00A712.6 Other_ID_Start
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
        if (IsOtherIdContinue(c)) return true; // \u00A712.6 Other_ID_Continue
        var cat = CharUnicodeInfo.GetUnicodeCategory(c);
        return cat is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.DecimalDigitNumber
            or UnicodeCategory.ConnectorPunctuation;
    }

    /// <summary>ECMAScript \u00A712.6 IdentifierStartChar includes the Unicode
    /// <c>Other_ID_Start</c> property code points, which are NOT in the letter
    /// categories tested above. Enumerate them explicitly.</summary>
    private static bool IsOtherIdStart(char c)
        => c is '\u1885'   // MONGOLIAN LETTER ALI GALI BALUDA
            or '\u1886'    // MONGOLIAN LETTER ALI GALI THREE BALUDA
            or '\u2118'    // SCRIPT CAPITAL P (U+2118 wp / weierstrass p)
            or '\u212E'    // ESTIMATED SYMBOL
            or '\u309B'    // KATAKANA-HIRAGANA VOICED SOUND MARK
            or '\u309C';   // KATAKANA-HIRAGANA SEMI-VOICED SOUND MARK

    /// <summary>ECMAScript \u00A712.6 IdentifierPartChar includes the Unicode
    /// <c>Other_ID_Continue</c> property code points (plus the
    /// Other_ID_Start set), none of which are in the mark/number/punctuation
    /// categories tested above. Enumerate the Other_ID_Continue set here.</summary>
    private static bool IsOtherIdContinue(char c)
        => c is '\u00B7'   // MIDDLE DOT (\u00B7)
            or '\u0387'    // GREEK ANO TELEIA
            or '\u1369'    // ETHIOPIC DIGIT ONE
            or '\u136A'
            or '\u136B'
            or '\u136C'
            or '\u136D'
            or '\u136E'
            or '\u136F'
            or '\u1370'
            or '\u1371'    // ETHIOPIC DIGIT NINE
            or '\u19DA';   // NEW TAI LUE THAM DIGIT ONE

    // ----- §12.6 IdentifierName UnicodeEscapeSequence support -----------------

    private static int HexDigit(char c) =>
        c >= '0' && c <= '9' ? c - '0'
        : c >= 'a' && c <= 'f' ? c - 'a' + 10
        : c >= 'A' && c <= 'F' ? c - 'A' + 10
        : -1;

    /// <summary>Code-point-aware IdentifierStart test (handles astral code
    /// points produced by a <c>\u{...}</c> escape).</summary>
    private static bool IsIdStartCp(int cp)
    {
        if (cp <= 0xFFFF) return IsIdStart((char)cp);
        var cat = CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(cp), 0);
        return cat is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter
            or UnicodeCategory.OtherLetter or UnicodeCategory.LetterNumber;
    }

    /// <summary>Code-point-aware IdentifierPart test.</summary>
    private static bool IsIdPartCp(int cp)
    {
        if (cp <= 0xFFFF) return IsIdPart((char)cp);
        var cat = CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(cp), 0);
        return IsIdStartCp(cp) || cat is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.DecimalDigitNumber
            or UnicodeCategory.ConnectorPunctuation;
    }

    /// <summary>Peek a <c>\uXXXX</c> or <c>\u{...}</c> escape starting at
    /// <paramref name="pos"/> (which must point at the backslash) WITHOUT
    /// consuming. Returns the decoded code point and sets <paramref name="len"/>
    /// to the number of source chars it spans; returns -1 when the slice is not
    /// a well-formed unicode escape.</summary>
    private int PeekUnicodeEscape(int pos, out int len)
    {
        len = 0;
        if (pos + 1 >= _src.Length || _src[pos] != '\\' || _src[pos + 1] != 'u') return -1;
        var p = pos + 2;
        if (p < _src.Length && _src[p] == '{')
        {
            p++;
            int val = 0; var any = false;
            while (p < _src.Length && _src[p] != '}')
            {
                var d = HexDigit(_src[p]); if (d < 0) return -1;
                val = val * 16 + d; if (val > 0x10FFFF) return -1;
                any = true; p++;
            }
            if (!any || p >= _src.Length || _src[p] != '}') return -1;
            len = (p - pos) + 1;
            return val;
        }
        if (p + 4 > _src.Length) return -1;
        int v = 0;
        for (var k = 0; k < 4; k++) { var d = HexDigit(_src[p + k]); if (d < 0) return -1; v = v * 16 + d; }
        len = (p + 4) - pos;
        return v;
    }

    /// <summary>True when the source at <paramref name="pos"/> begins a
    /// <c>\u</c> escape whose code point is a valid IdentifierStart.</summary>
    private bool StartsIdentifierEscape(int pos)
    {
        var cp = PeekUnicodeEscape(pos, out _);
        return cp >= 0 && IsIdStartCp(cp);
    }

    private static JsToken MakeToken(
        JsTokenKind kind, string lexeme, JsPosition start, JsPosition end,
        bool precededByLT, object? value = null, bool legacyOctal = false)
        => new(kind, lexeme, start, end, value)
        {
            PrecededByLineTerminator = precededByLT,
            LegacyOctal = legacyOctal,
        };
}
