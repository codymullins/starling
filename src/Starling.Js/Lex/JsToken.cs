namespace Starling.Js.Lex;

/// <summary>
/// A lexer token. <see cref="Lexeme"/> is the raw source slice;
/// <see cref="Value"/> carries the decoded literal value (a <c>double</c>
/// for numeric, a <c>string</c> for string literals, <c>null</c>
/// otherwise). For BigInt literals, <see cref="Value"/> is the raw digits
/// string (BigInteger conversion lives in the JS runtime, not the lexer).
/// </summary>
public readonly record struct JsToken(
    JsTokenKind Kind,
    string Lexeme,
    JsPosition Start,
    JsPosition End,
    object? Value = null)
{
    /// <summary>True if this token was preceded by a line terminator in the
    /// source — needed by the parser's automatic-semicolon-insertion rules.</summary>
    public bool PrecededByLineTerminator { get; init; }

    /// <summary>True when this token uses a legacy syntactic form that is a
    /// strict-mode SyntaxError (ES §12.9.3 / B.1.2): a legacy octal integer
    /// literal (<c>0123</c>), a NonOctalDecimalInteger (<c>08</c>/<c>09</c>),
    /// or a string literal containing a legacy octal / <c>\8</c> / <c>\9</c>
    /// escape sequence. The lexer cannot know whether the surrounding scope is
    /// strict, so it merely tags the token; the parser raises the error when
    /// the token appears in a strict scope.</summary>
    public bool LegacyOctal { get; init; }

    public override string ToString() =>
        $"{Kind} \"{Lexeme}\" at {Start}";
}
