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

    /// <summary>NULL character observed. Each state decides what to do with the code point.</summary>
    UnexpectedNullCharacter,

    // M1-01b — tag + attribute states ------------------------------------

    /// <summary>EOF before the tag name in <c>&lt;</c> or <c>&lt;/</c>.</summary>
    EofBeforeTagName,

    /// <summary>e.g. <c>&lt;?</c> outside any other tag state.</summary>
    UnexpectedQuestionMarkInsteadOfTagName,

    /// <summary>e.g. <c>&lt;@</c> — <c>&lt;</c> followed by something other than alpha / <c>!</c> / <c>/</c> / <c>?</c>.</summary>
    InvalidFirstCharacterOfTagName,

    /// <summary><c>&lt;/&gt;</c>.</summary>
    MissingEndTagName,

    /// <summary>Same attribute name appears twice on one tag.</summary>
    DuplicateAttribute,

    /// <summary><c>=</c> before any attribute name on a tag.</summary>
    UnexpectedEqualsSignBeforeAttributeName,

    /// <summary><c>"</c>, <c>'</c>, or <c>&lt;</c> inside an attribute name.</summary>
    UnexpectedCharacterInAttributeName,

    /// <summary><c>&gt;</c> immediately after <c>=</c> with no value.</summary>
    MissingAttributeValue,

    /// <summary><c>"</c>, <c>'</c>, <c>&lt;</c>, <c>=</c>, or backtick in an unquoted value.</summary>
    UnexpectedCharacterInUnquotedAttributeValue,

    /// <summary>Two attributes adjacent without whitespace between, e.g. <c>a="x"b="y"</c>.</summary>
    MissingWhitespaceBetweenAttributes,

    /// <summary>Stray <c>/</c> in a tag, e.g. <c>&lt;a /b&gt;</c>.</summary>
    UnexpectedSolidusInTag,
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
