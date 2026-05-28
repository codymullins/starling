namespace Starling.Css.Tokenizer;

public readonly record struct CssToken(
    CssTokenType Type,
    string Value = "",
    double Number = 0,
    string Unit = "",
    char Delimiter = '\0',
    bool HasSign = false,
    bool IsInteger = false,
    // CSS Syntax 3 §4.3.6 — a hash token's type flag: true = "id" (the value
    // would start an ident sequence, e.g. #main), false = "unrestricted"
    // (e.g. #1abc). Only meaningful when Type == Hash.
    bool HashIsId = false)
{
    public override string ToString() => Type switch
    {
        CssTokenType.Delim => $"Delim({Delimiter})",
        CssTokenType.Number or CssTokenType.Percentage => $"{Type}({Number})",
        CssTokenType.Dimension => $"Dimension({Number}{Unit})",
        CssTokenType.Eof => "Eof",
        _ when Value.Length > 0 => $"{Type}({Value})",
        _ => Type.ToString(),
    };
}
