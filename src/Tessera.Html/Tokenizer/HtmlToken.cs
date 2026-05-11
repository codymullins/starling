namespace Tessera.Html.Tokenizer;

/// <summary>
/// One emitted by the HTML tokenizer (WHATWG HTML §13.2.5). The discriminated
/// union mirrors the spec's token categories. Subsequent agents (M1-01b…g)
/// extend the populating logic; this shape is stable.
/// </summary>
public abstract record HtmlToken;

/// <summary>
/// A single character emitted into the parser. The token holds the raw code
/// point; CR/LF/NUL normalization happens upstream in <c>PreprocessedStream</c>.
/// </summary>
public sealed record CharacterToken(int CodePoint) : HtmlToken;

/// <summary>
/// Start tag. Attributes are added in order discovered; duplicates are
/// suppressed per spec §13.2.5.32 by the attribute states (M1-01b owns).
/// </summary>
public sealed record StartTagToken(
    string Name,
    IReadOnlyList<HtmlAttribute> Attributes,
    bool SelfClosing) : HtmlToken;

/// <summary>End tag. Attributes are a parse error but tracked for the spec.</summary>
public sealed record EndTagToken(
    string Name,
    IReadOnlyList<HtmlAttribute> Attributes,
    bool SelfClosing) : HtmlToken;

/// <summary>HTML comment content.</summary>
public sealed record CommentToken(string Data) : HtmlToken;

/// <summary>
/// DOCTYPE. The tree builder decides quirks vs. limited-quirks vs. no-quirks
/// from these fields per §13.2.6.2; the tokenizer just reports.
/// </summary>
public sealed record DoctypeToken(
    string? Name,
    string? PublicId,
    string? SystemId,
    bool ForceQuirks) : HtmlToken;

/// <summary>Tokenizer reached end-of-input.</summary>
public sealed record EndOfFileToken : HtmlToken
{
    public static EndOfFileToken Instance { get; } = new();
}

/// <summary>An attribute as collected by the tokenizer.</summary>
public sealed record HtmlAttribute(string Name, string Value);
