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

    // M1-01e — comment + CDATA states ------------------------------------

    /// <summary><c>&lt;!</c> not followed by <c>--</c>, <c>DOCTYPE</c>, or
    /// <c>[CDATA[</c>. Falls into bogus comment.</summary>
    IncorrectlyOpenedComment,

    /// <summary><c>&lt;!--&gt;</c> or <c>&lt;!---&gt;</c> — empty comment closed too soon.</summary>
    AbruptClosingOfEmptyComment,

    /// <summary><c>&lt;!-- &lt;!--</c> — nested comment start observed.</summary>
    NestedComment,

    /// <summary><c>&lt;!-- foo --!&gt;</c> — bogus close.</summary>
    IncorrectlyClosedComment,

    /// <summary><c>&lt;![CDATA[</c> seen in HTML content (allowed only in foreign content).</summary>
    CdataInHtmlContent,

    // M1-01f — doctype states --------------------------------------------

    /// <summary><c>&lt;!DOCTYPE</c> followed by non-whitespace, non-EOF, non-<c>&gt;</c>.</summary>
    MissingWhitespaceBeforeDoctypeName,

    /// <summary><c>&lt;!DOCTYPE&gt;</c> with no name.</summary>
    MissingDoctypeName,

    /// <summary>Doctype public identifier closes with <c>&gt;</c> instead of the quote.</summary>
    AbruptDoctypePublicIdentifier,

    /// <summary>Doctype system identifier closes with <c>&gt;</c> instead of the quote.</summary>
    AbruptDoctypeSystemIdentifier,

    /// <summary><c>PUBLIC "..." "..."</c> with no whitespace between identifiers.</summary>
    MissingWhitespaceBetweenDoctypePublicAndSystemIdentifiers,

    /// <summary>Public identifier started without an opening quote.</summary>
    MissingQuoteBeforeDoctypePublicIdentifier,

    /// <summary>System identifier started without an opening quote.</summary>
    MissingQuoteBeforeDoctypeSystemIdentifier,

    /// <summary><c>PUBLIC&gt;</c> with no identifier.</summary>
    MissingDoctypePublicIdentifier,

    /// <summary><c>SYSTEM&gt;</c> with no identifier.</summary>
    MissingDoctypeSystemIdentifier,

    /// <summary><c>PUBLIC</c> not followed by whitespace.</summary>
    MissingWhitespaceAfterDoctypePublicKeyword,

    /// <summary><c>SYSTEM</c> not followed by whitespace.</summary>
    MissingWhitespaceAfterDoctypeSystemKeyword,

    /// <summary>Extra content after the system identifier; drops into bogus doctype.</summary>
    UnexpectedCharacterAfterDoctypeSystemIdentifier,

    /// <summary>Doctype name followed by garbage that isn't <c>PUBLIC</c>/<c>SYSTEM</c>.</summary>
    InvalidCharacterSequenceAfterDoctypeName,

    // M1-01g — character reference states ------------------------------

    /// <summary>Named character reference matched without a trailing semicolon (e.g. <c>&amp;amp foo</c>).</summary>
    MissingSemicolonAfterCharacterReference,

    /// <summary>An ampersand-form reference (<c>&amp;foo;</c>) isn't in the table.</summary>
    UnknownNamedCharacterReference,

    /// <summary><c>&amp;#;</c> or <c>&amp;#x;</c> with no digits.</summary>
    AbsenceOfDigitsInNumericCharacterReference,

    /// <summary><c>&amp;#0;</c>.</summary>
    NullCharacterReference,

    /// <summary>Numeric reference &gt; U+10FFFF.</summary>
    CharacterReferenceOutsideUnicodeRange,

    /// <summary>Numeric reference inside the UTF-16 surrogate range.</summary>
    SurrogateCharacterReference,

    /// <summary>Numeric reference points at a Unicode noncharacter.</summary>
    NoncharacterCharacterReference,

    /// <summary>Numeric reference points at a C0 / C1 control that isn't whitespace.</summary>
    ControlCharacterReference,
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

/// <summary>
/// A parse-error sink that counts reports. Used by the parser to tag the
/// "html.parse" diagnostic span with an error total without an O(n) walk.
/// </summary>
public sealed class CountingParseErrorSink : IParseErrorSink
{
    public int Count { get; private set; }

    public void Report(HtmlParseError code, int line, int column) => Count++;
}
