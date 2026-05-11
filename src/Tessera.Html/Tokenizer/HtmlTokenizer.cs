using Tessera.Html.InputStream;

namespace Tessera.Html.Tokenizer;

/// <summary>
/// WHATWG HTML tokenizer scaffold. M1-01a delivers the public surface and
/// the <see cref="TokenizerState.Data"/> state plus EOF handling. The
/// remaining states are filled in by subsequent agents (wp:M1-01b…g).
/// </summary>
/// <remarks>
/// API shape rationale:
/// <list type="bullet">
///   <item>
///     Push-driven: <see cref="Feed(System.ReadOnlySpan{char})"/> +
///     <see cref="EndOfInput"/>. The engine feeds bytes as they arrive on
///     the network; the tokenizer is restartable across chunk boundaries.
///   </item>
///   <item>
///     Pull-mode token reader: <see cref="ReadToken"/> returns the next
///     fully-formed token (or <c>null</c> if more input is needed). This
///     keeps the tokenizer test-friendly without dragging in a Channel.
///   </item>
/// </list>
/// A handful of helper "TODO M1-01x" markers route unimplemented states to
/// loud failures. Replace each marker by porting the matching spec section.
/// </remarks>
public sealed class HtmlTokenizer
{
    private readonly PreprocessedStream _stream = new();
    private readonly Queue<HtmlToken> _emitted = new();
    private readonly IParseErrorSink _errors;

    private TokenizerState _state = TokenizerState.Data;
    private bool _eofReached;
    private bool _eofProcessed;

    // Position tracking (1-based to match the spec's "first character is at
    // line 1, column 1"). Only used for parse error reporting.
    private int _line = 1;
    private int _column = 0;

    public HtmlTokenizer(IParseErrorSink? errorSink = null)
    {
        _errors = errorSink ?? IParseErrorSink.Null;
    }

    /// <summary>The current state. Exposed for tests; not for general use.</summary>
    internal TokenizerState State => _state;

    /// <summary>Push more input. Idempotent on empty spans.</summary>
    public void Feed(ReadOnlySpan<char> chars) => _stream.Feed(chars);

    /// <summary>Signal end-of-input. Subsequent <see cref="ReadToken"/>
    /// calls will eventually emit an <see cref="EndOfFileToken"/>.</summary>
    public void EndOfInput()
    {
        _stream.EndOfInput();
        _eofReached = true;
    }

    /// <summary>
    /// Returns the next token, or <c>null</c> if the tokenizer needs more
    /// input. Once an <see cref="EndOfFileToken"/> has been returned, all
    /// subsequent calls also return that singleton.
    /// </summary>
    public HtmlToken? ReadToken()
    {
        while (_emitted.Count == 0)
        {
            if (!Step())
            {
                // Step returned false → blocked on more input or EOF already
                // emitted. The queue check above handles "already emitted".
                return null;
            }
        }
        return _emitted.Dequeue();
    }

    /// <summary>
    /// One state-machine step. Returns true if it consumed a code point or
    /// produced a token; false if the tokenizer is blocked on more input or
    /// has already emitted its final EOF token.
    /// </summary>
    private bool Step()
    {
        if (_eofProcessed) return false;

        // Need a code point to make progress. If the stream is dry and we
        // haven't yet been told it's the end, ask the caller for more.
        if (_stream.Remaining == 0)
        {
            if (!_eofReached) return false;
            return StepEof();
        }

        var c = _stream.Read();
        TrackPosition(c);

        switch (_state)
        {
            case TokenizerState.Data:
                StepData(c);
                return true;

            // Until subsequent agents wire the rest in, every other state is
            // unreachable from M1-01a's only entry (Data). Adding states in
            // wp:M1-01b…g means routing transitions from Data toward them.
            default:
                throw new NotImplementedException(
                    $"Tokenizer state '{_state}' is not implemented yet. " +
                    $"See tasks/M1/wp-M1-01{StateOwner(_state)}-*.md.");
        }
    }

    private bool StepEof()
    {
        // §13.2.5.1 Data state: "EOF → Emit an end-of-file token."
        // For now, every state we implement collapses to: on EOF, emit EOF.
        // States with mid-construction tokens (comment, doctype) will fire
        // their parse errors here once they're added.
        switch (_state)
        {
            case TokenizerState.Data:
                _emitted.Enqueue(EndOfFileToken.Instance);
                _eofProcessed = true;
                return true;
            default:
                throw new NotImplementedException(
                    $"EOF in state '{_state}' is not implemented yet.");
        }
    }

    // -----------------------------------------------------------------------
    // 13.2.5.1 Data state
    // https://html.spec.whatwg.org/multipage/parsing.html#data-state
    //
    // Consume the next input character:
    //   U+0026 AMPERSAND (&)       → set return state Data; switch to
    //                                Character reference state.
    //   U+003C LESS-THAN SIGN (<)  → switch to Tag open state.
    //   U+0000 NULL                → parse error (unexpected-null-character).
    //                                Emit current input character as a
    //                                character token.
    //   EOF                        → emit end-of-file token.
    //   Anything else              → emit the current input character as a
    //                                character token.
    //
    // In M1-01a only:
    //   • '<' and '&' are still routed to "TODO M1-01b/g" via parse-error
    //     placeholders so we can ship a working Data state without the rest.
    // -----------------------------------------------------------------------
    private void StepData(int c)
    {
        switch (c)
        {
            case '&':
                // TODO(wp:M1-01g): set _returnState = Data; transition to
                // TokenizerState.CharacterReference. Until then, emit '&' as
                // a literal and continue — this is incorrect per spec but
                // safe for the M1-01a tests, which avoid entities.
                _emitted.Enqueue(new CharacterToken('&'));
                break;

            case '<':
                // TODO(wp:M1-01b): transition to TokenizerState.TagOpen. The
                // placeholder below emits '<' as a literal so a Data-only
                // tokenizer is observably useful (no NotImplementedException
                // from a Data step). M1-01b replaces this with the real
                // transition; M1-01a tests do not cover '<' beyond asserting
                // it doesn't crash.
                _emitted.Enqueue(new CharacterToken('<'));
                break;

            case 0xFFFD:
                // The preprocessor mapped a NUL → U+FFFD. The spec wants the
                // parse error in the Data state, not in the preprocessor —
                // so report it here on observation.
                _errors.Report(
                    HtmlParseError.UnexpectedNullCharacter, _line, _column);
                _emitted.Enqueue(new CharacterToken(0xFFFD));
                break;

            default:
                _emitted.Enqueue(new CharacterToken(c));
                break;
        }
    }

    private void TrackPosition(int c)
    {
        if (c == '\n')
        {
            _line++;
            _column = 0;
        }
        else
        {
            _column++;
        }
    }

    /// <summary>
    /// Returns the lowercase sub-task letter (a–g) that owns the given state,
    /// to point future implementers at the right tasks/M1/wp-M1-01… file.
    /// </summary>
    private static string StateOwner(TokenizerState s) => s switch
    {
        TokenizerState.TagOpen
            or TokenizerState.EndTagOpen
            or TokenizerState.TagName
            or TokenizerState.BeforeAttributeName
            or TokenizerState.AttributeName
            or TokenizerState.AfterAttributeName
            or TokenizerState.BeforeAttributeValue
            or TokenizerState.AttributeValueDoubleQuoted
            or TokenizerState.AttributeValueSingleQuoted
            or TokenizerState.AttributeValueUnquoted
            or TokenizerState.AfterAttributeValueQuoted
            or TokenizerState.SelfClosingStartTag => "b",
        TokenizerState.Rcdata
            or TokenizerState.Rawtext
            or TokenizerState.Plaintext
            or TokenizerState.RcdataLessThanSign
            or TokenizerState.RcdataEndTagOpen
            or TokenizerState.RcdataEndTagName
            or TokenizerState.RawtextLessThanSign
            or TokenizerState.RawtextEndTagOpen
            or TokenizerState.RawtextEndTagName => "c",
        TokenizerState.ScriptData
            or TokenizerState.ScriptDataLessThanSign
            or TokenizerState.ScriptDataEndTagOpen
            or TokenizerState.ScriptDataEndTagName
            or TokenizerState.ScriptDataEscapeStart
            or TokenizerState.ScriptDataEscapeStartDash
            or TokenizerState.ScriptDataEscaped
            or TokenizerState.ScriptDataEscapedDash
            or TokenizerState.ScriptDataEscapedDashDash
            or TokenizerState.ScriptDataEscapedLessThanSign
            or TokenizerState.ScriptDataEscapedEndTagOpen
            or TokenizerState.ScriptDataEscapedEndTagName
            or TokenizerState.ScriptDataDoubleEscapeStart
            or TokenizerState.ScriptDataDoubleEscaped
            or TokenizerState.ScriptDataDoubleEscapedDash
            or TokenizerState.ScriptDataDoubleEscapedDashDash
            or TokenizerState.ScriptDataDoubleEscapedLessThanSign
            or TokenizerState.ScriptDataDoubleEscapeEnd => "d",
        TokenizerState.BogusComment
            or TokenizerState.MarkupDeclarationOpen
            or TokenizerState.CommentStart
            or TokenizerState.CommentStartDash
            or TokenizerState.Comment
            or TokenizerState.CommentLessThanSign
            or TokenizerState.CommentLessThanSignBang
            or TokenizerState.CommentLessThanSignBangDash
            or TokenizerState.CommentLessThanSignBangDashDash
            or TokenizerState.CommentEndDash
            or TokenizerState.CommentEnd
            or TokenizerState.CommentEndBang
            or TokenizerState.CdataSection
            or TokenizerState.CdataSectionBracket
            or TokenizerState.CdataSectionEnd => "e",
        TokenizerState.Doctype
            or TokenizerState.BeforeDoctypeName
            or TokenizerState.DoctypeName
            or TokenizerState.AfterDoctypeName
            or TokenizerState.AfterDoctypePublicKeyword
            or TokenizerState.BeforeDoctypePublicIdentifier
            or TokenizerState.DoctypePublicIdentifierDoubleQuoted
            or TokenizerState.DoctypePublicIdentifierSingleQuoted
            or TokenizerState.AfterDoctypePublicIdentifier
            or TokenizerState.BetweenDoctypePublicAndSystemIdentifiers
            or TokenizerState.AfterDoctypeSystemKeyword
            or TokenizerState.BeforeDoctypeSystemIdentifier
            or TokenizerState.DoctypeSystemIdentifierDoubleQuoted
            or TokenizerState.DoctypeSystemIdentifierSingleQuoted
            or TokenizerState.AfterDoctypeSystemIdentifier
            or TokenizerState.BogusDoctype => "f",
        TokenizerState.CharacterReference
            or TokenizerState.NamedCharacterReference
            or TokenizerState.AmbiguousAmpersand
            or TokenizerState.NumericCharacterReference
            or TokenizerState.HexadecimalCharacterReferenceStart
            or TokenizerState.DecimalCharacterReferenceStart
            or TokenizerState.HexadecimalCharacterReference
            or TokenizerState.DecimalCharacterReference
            or TokenizerState.NumericCharacterReferenceEnd => "g",
        _ => "?",
    };
}
