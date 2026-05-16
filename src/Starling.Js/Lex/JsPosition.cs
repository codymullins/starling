namespace Tessera.Js.Lex;

/// <summary>
/// Source position: 1-based line / column plus 0-based absolute char offset.
/// Used for error reporting and the parser's <see cref="JsToken"/> ranges.
/// </summary>
public readonly record struct JsPosition(int Line, int Column, int Offset)
{
    public static JsPosition Start { get; } = new(1, 1, 0);

    public override string ToString() => $"{Line}:{Column}";
}
