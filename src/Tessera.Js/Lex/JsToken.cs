namespace Tessera.Js.Lex;

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

    public override string ToString() =>
        $"{Kind} \"{Lexeme}\" at {Start}";
}
