namespace Tessera.Js.Lex;

/// <summary>
/// Categorized lexer errors. Spec calls these "early errors" — the parser
/// surfaces them as <c>SyntaxError</c>.
/// </summary>
public enum JsLexError
{
    InvalidCharacter,
    UnterminatedString,
    UnterminatedComment,
    InvalidEscape,
    InvalidNumericLiteral,
    InvalidUnicodeEscape,
    UnexpectedEndOfInput,
}

public interface IJsLexErrorSink
{
    void Report(JsLexError code, JsPosition position, string message);

    public static IJsLexErrorSink Null { get; } = new NullSink();

    private sealed class NullSink : IJsLexErrorSink
    {
        public void Report(JsLexError code, JsPosition position, string message) { /* drop */ }
    }
}
