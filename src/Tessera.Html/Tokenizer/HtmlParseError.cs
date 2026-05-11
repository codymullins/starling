namespace Tessera.Html.Tokenizer;

/// <summary>
/// Parse-error codes from WHATWG HTML §13.2.2.
/// <see href="https://html.spec.whatwg.org/multipage/parsing.html#parse-errors"/>.
/// The enum names match the spec slugs. Tokenizer states report into an
/// <c>IParseErrorSink</c>; agent M1-01h drives the list to completeness.
/// </summary>
/// <remarks>
/// Only entries actively referenced by states implemented so far are listed.
/// Adding a new state (M1-01b…g) goes hand-in-hand with adding its errors.
/// Avoid renaming entries — golden test fixtures key off the slug.
/// </remarks>
public enum HtmlParseError
{
    /// <summary>Tokenizer reached EOF inside a state that expects more input.</summary>
    EofInTag,

    /// <summary>EOF reached inside a comment.</summary>
    EofInComment,

    /// <summary>EOF in DOCTYPE name (or related sub-state).</summary>
    EofInDoctype,

    /// <summary>EOF inside a CDATA section.</summary>
    EofInCdata,

    /// <summary>EOF inside a script-data escape.</summary>
    EofInScriptHtmlCommentLikeText,

    /// <summary>Unexpected null character (after preprocessor it's U+FFFD).</summary>
    UnexpectedNullCharacter,
}

/// <summary>
/// Receives parse errors. Default implementation drops them; agents can swap
/// in a recording sink during tests.
/// </summary>
public interface IParseErrorSink
{
    void Report(HtmlParseError code, int line, int column);

    /// <summary>A no-op sink. Convenient default.</summary>
    public static IParseErrorSink Null { get; } = new NullSink();

    private sealed class NullSink : IParseErrorSink
    {
        public void Report(HtmlParseError code, int line, int column) { /* drop */ }
    }
}
