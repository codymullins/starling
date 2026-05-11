using System.Text;
using Tessera.Html.InputStream;

namespace Tessera.Html.Tokenizer;

/// <summary>
/// WHATWG HTML tokenizer. Partial class — each sub-task (wp:M1-01a…g) adds
/// its state cluster as its own partial file so concurrent agents avoid
/// merge conflicts on shared structure.
/// </summary>
/// <remarks>
/// <para>
/// API shape:
/// <list type="bullet">
///   <item>Push-driven: <see cref="Feed(System.ReadOnlySpan{char})"/> +
///         <see cref="EndOfInput"/>. The engine feeds bytes as they arrive
///         on the network; the tokenizer is restartable across chunks.</item>
///   <item>Pull-mode token reader: <see cref="ReadToken"/> returns the next
///         token, or <c>null</c> if blocked on more input.</item>
/// </list>
/// </para>
/// <para>
/// State implementation status:
/// <list type="bullet">
///   <item>M1-01a: <c>Data</c>, EOF handling, scaffolding.</item>
///   <item>M1-01b: tag + attribute states (this partial extends Dispatch).</item>
///   <item>M1-01c…g: remaining clusters (RCDATA/RAWTEXT, ScriptData,
///         Comment/CDATA, Doctype, Character references).</item>
/// </list>
/// </para>
/// </remarks>
public sealed partial class HtmlTokenizer
{
    private readonly PreprocessedStream _stream = new();
    private readonly Queue<HtmlToken> _emitted = new();
    private readonly IParseErrorSink _errors;

    private TokenizerState _state = TokenizerState.Data;
    private bool _eofReached;
    private bool _eofProcessed;

    // Reconsume: when a state says "reconsume in state X", we set _state = X
    // and push the just-consumed code point back via _reconsume so the next
    // Step() picks it up instead of advancing the input. -2 = no reconsume.
    private const int NoReconsume = -2;
    private int _reconsume = NoReconsume;

    // Position tracking (1-based) for parse-error reporting.
    private int _line = 1;
    private int _column = 0;

    // --- Tag/attribute builder (populated by M1-01b states) -----------------
    private bool _tagIsEnd;
    private bool _tagSelfClosing;
    private readonly StringBuilder _tagName = new();
    private readonly List<HtmlAttribute> _tagAttrs = [];
    private readonly StringBuilder _attrName = new();
    private readonly StringBuilder _attrValue = new();

    public HtmlTokenizer(IParseErrorSink? errorSink = null)
    {
        _errors = errorSink ?? IParseErrorSink.Null;
    }

    /// <summary>The current state. Exposed for tests; not for general use.</summary>
    internal TokenizerState State => _state;

    /// <summary>Push more input.</summary>
    public void Feed(ReadOnlySpan<char> chars) => _stream.Feed(chars);

    /// <summary>Signal end-of-input.</summary>
    public void EndOfInput()
    {
        _stream.EndOfInput();
        _eofReached = true;
    }

    /// <summary>
    /// Returns the next token, or <c>null</c> if the tokenizer needs more
    /// input. After an <see cref="EndOfFileToken"/> is returned, subsequent
    /// calls return <c>null</c> (EOF is terminal).
    /// </summary>
    public HtmlToken? ReadToken()
    {
        while (_emitted.Count == 0)
        {
            if (!Step()) return null;
        }
        return _emitted.Dequeue();
    }

    /// <summary>
    /// One state-machine step. Returns true if it consumed a code point or
    /// produced a token; false if blocked on more input or EOF processed.
    /// </summary>
    private bool Step()
    {
        if (_eofProcessed) return false;

        int c;
        if (_reconsume != NoReconsume)
        {
            c = _reconsume;
            _reconsume = NoReconsume;
        }
        else if (_stream.Remaining == 0)
        {
            if (!_eofReached) return false;
            return StepEof();
        }
        else
        {
            c = _stream.Read();
            TrackPosition(c);
        }

        Dispatch(_state, c);
        return true;
    }

    /// <summary>
    /// Route the code point to the current state's handler. Each cluster of
    /// states lives in a partial file; this switch is the rendezvous.
    /// </summary>
    private void Dispatch(TokenizerState state, int c)
    {
        switch (state)
        {
            case TokenizerState.Data:
                StepData(c);
                return;

            // M1-01b: tag + attribute states.
            case TokenizerState.TagOpen:
            case TokenizerState.EndTagOpen:
            case TokenizerState.TagName:
            case TokenizerState.BeforeAttributeName:
            case TokenizerState.AttributeName:
            case TokenizerState.AfterAttributeName:
            case TokenizerState.BeforeAttributeValue:
            case TokenizerState.AttributeValueDoubleQuoted:
            case TokenizerState.AttributeValueSingleQuoted:
            case TokenizerState.AttributeValueUnquoted:
            case TokenizerState.AfterAttributeValueQuoted:
            case TokenizerState.SelfClosingStartTag:
                DispatchTagState(state, c);
                return;

            default:
                throw new NotImplementedException(
                    $"Tokenizer state '{state}' not implemented yet. " +
                    $"See tasks/M1/wp-M1-01{StateOwner(state)}-*.md.");
        }
    }

    private bool StepEof()
    {
        switch (_state)
        {
            case TokenizerState.Data:
                _emitted.Enqueue(EndOfFileToken.Instance);
                _eofProcessed = true;
                return true;

            // M1-01b EOF handling (delegated to TagStates partial).
            case TokenizerState.TagOpen:
            case TokenizerState.EndTagOpen:
            case TokenizerState.TagName:
            case TokenizerState.BeforeAttributeName:
            case TokenizerState.AttributeName:
            case TokenizerState.AfterAttributeName:
            case TokenizerState.BeforeAttributeValue:
            case TokenizerState.AttributeValueDoubleQuoted:
            case TokenizerState.AttributeValueSingleQuoted:
            case TokenizerState.AttributeValueUnquoted:
            case TokenizerState.AfterAttributeValueQuoted:
            case TokenizerState.SelfClosingStartTag:
                StepTagEof();
                _eofProcessed = true;
                return true;

            default:
                throw new NotImplementedException(
                    $"EOF in state '{_state}' not implemented yet.");
        }
    }

    // -----------------------------------------------------------------------
    // 13.2.5.1 Data state
    // https://html.spec.whatwg.org/multipage/parsing.html#data-state
    // -----------------------------------------------------------------------
    private void StepData(int c)
    {
        switch (c)
        {
            case '&':
                // TODO(wp:M1-01g): set return state Data, switch to
                // CharacterReference. Until then, emit '&' as a literal so
                // a Data+Tag-only tokenizer is observably useful.
                _emitted.Enqueue(new CharacterToken('&'));
                break;

            case '<':
                _state = TokenizerState.TagOpen;
                break;

            case 0:
                // §13.2.5.1: "unexpected-null-character parse error. Emit the
                // current input character as a character token."
                _errors.Report(
                    HtmlParseError.UnexpectedNullCharacter, _line, _column);
                _emitted.Enqueue(new CharacterToken(0));
                break;

            default:
                _emitted.Enqueue(new CharacterToken(c));
                break;
        }
    }

    private void Reconsume(int c, TokenizerState next)
    {
        _state = next;
        _reconsume = c;
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

    /// <summary>Maps a state to the sub-task letter that owns it.</summary>
    private static string StateOwner(TokenizerState s) => s switch
    {
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
