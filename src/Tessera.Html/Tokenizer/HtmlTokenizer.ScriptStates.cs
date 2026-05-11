namespace Tessera.Html.Tokenizer;

/// <summary>
/// ScriptData state family from WHATWG HTML §13.2.5.4 and §13.2.5.15–31.
/// Owned by wp:M1-01d.
/// </summary>
public sealed partial class HtmlTokenizer
{
    private void DispatchScriptState(TokenizerState state, int c)
    {
        switch (state)
        {
            case TokenizerState.ScriptData:                           StepScriptData(c); break;
            case TokenizerState.ScriptDataLessThanSign:               StepScriptDataLt(c); break;
            case TokenizerState.ScriptDataEndTagOpen:                 StepScriptDataEndTagOpen(c); break;
            case TokenizerState.ScriptDataEndTagName:                 StepScriptDataEndTagName(c); break;
            case TokenizerState.ScriptDataEscapeStart:                StepScriptDataEscapeStart(c); break;
            case TokenizerState.ScriptDataEscapeStartDash:            StepScriptDataEscapeStartDash(c); break;
            case TokenizerState.ScriptDataEscaped:                    StepScriptDataEscaped(c); break;
            case TokenizerState.ScriptDataEscapedDash:                StepScriptDataEscapedDash(c); break;
            case TokenizerState.ScriptDataEscapedDashDash:            StepScriptDataEscapedDashDash(c); break;
            case TokenizerState.ScriptDataEscapedLessThanSign:        StepScriptDataEscapedLt(c); break;
            case TokenizerState.ScriptDataEscapedEndTagOpen:          StepScriptDataEscapedEndTagOpen(c); break;
            case TokenizerState.ScriptDataEscapedEndTagName:          StepScriptDataEscapedEndTagName(c); break;
            case TokenizerState.ScriptDataDoubleEscapeStart:          StepScriptDataDoubleEscapeStart(c); break;
            case TokenizerState.ScriptDataDoubleEscaped:              StepScriptDataDoubleEscaped(c); break;
            case TokenizerState.ScriptDataDoubleEscapedDash:          StepScriptDataDoubleEscapedDash(c); break;
            case TokenizerState.ScriptDataDoubleEscapedDashDash:      StepScriptDataDoubleEscapedDashDash(c); break;
            case TokenizerState.ScriptDataDoubleEscapedLessThanSign:  StepScriptDataDoubleEscapedLt(c); break;
            case TokenizerState.ScriptDataDoubleEscapeEnd:            StepScriptDataDoubleEscapeEnd(c); break;
            default: throw new InvalidOperationException(
                $"DispatchScriptState invoked for unrelated state '{state}'.");
        }
    }

    private void StepScriptEof()
    {
        switch (_state)
        {
            case TokenizerState.ScriptDataLessThanSign:
                _emitted.Enqueue(new CharacterToken('<'));
                break;
            case TokenizerState.ScriptDataEndTagOpen:
            case TokenizerState.ScriptDataEscapedEndTagOpen:
                _emitted.Enqueue(new CharacterToken('<'));
                _emitted.Enqueue(new CharacterToken('/'));
                break;
            case TokenizerState.ScriptDataEndTagName:
            case TokenizerState.ScriptDataEscapedEndTagName:
                FlushEndTagAttemptAsCharacters();
                break;
            case TokenizerState.ScriptDataEscapeStart:
            case TokenizerState.ScriptDataEscapeStartDash:
            case TokenizerState.ScriptDataEscaped:
            case TokenizerState.ScriptDataEscapedDash:
            case TokenizerState.ScriptDataEscapedDashDash:
            case TokenizerState.ScriptDataEscapedLessThanSign:
            case TokenizerState.ScriptDataDoubleEscapeStart:
            case TokenizerState.ScriptDataDoubleEscaped:
            case TokenizerState.ScriptDataDoubleEscapedDash:
            case TokenizerState.ScriptDataDoubleEscapedDashDash:
            case TokenizerState.ScriptDataDoubleEscapedLessThanSign:
            case TokenizerState.ScriptDataDoubleEscapeEnd:
                _errors.Report(HtmlParseError.EofInScriptHtmlCommentLikeText, _line, _column);
                break;
        }

        _emitted.Enqueue(EndOfFileToken.Instance);
    }

    // 13.2.5.4 Script data state
    private void StepScriptData(int c)
    {
        switch (c)
        {
            case '<':
                _state = TokenizerState.ScriptDataLessThanSign;
                return;
            case 0:
                _errors.Report(HtmlParseError.UnexpectedNullCharacter, _line, _column);
                _emitted.Enqueue(new CharacterToken(0xFFFD));
                return;
            default:
                _emitted.Enqueue(new CharacterToken(c));
                return;
        }
    }

    // 13.2.5.15 Script data less-than sign state
    private void StepScriptDataLt(int c)
    {
        switch (c)
        {
            case '/':
                _tempBuffer.Clear();
                _state = TokenizerState.ScriptDataEndTagOpen;
                return;
            case '!':
                _state = TokenizerState.ScriptDataEscapeStart;
                _emitted.Enqueue(new CharacterToken('<'));
                _emitted.Enqueue(new CharacterToken('!'));
                return;
            default:
                _emitted.Enqueue(new CharacterToken('<'));
                Reconsume(c, TokenizerState.ScriptData);
                return;
        }
    }

    // 13.2.5.16 Script data end tag open state
    private void StepScriptDataEndTagOpen(int c)
    {
        if (IsAsciiAlpha(c))
        {
            StartNewTag(isEnd: true);
            Reconsume(c, TokenizerState.ScriptDataEndTagName);
            return;
        }

        _emitted.Enqueue(new CharacterToken('<'));
        _emitted.Enqueue(new CharacterToken('/'));
        Reconsume(c, TokenizerState.ScriptData);
    }

    // 13.2.5.17 Script data end tag name state
    private void StepScriptDataEndTagName(int c) =>
        StepEndTagNameCommon(c, returnState: TokenizerState.ScriptData);

    // 13.2.5.18 Script data escape start state
    private void StepScriptDataEscapeStart(int c)
    {
        if (c == '-')
        {
            _state = TokenizerState.ScriptDataEscapeStartDash;
            _emitted.Enqueue(new CharacterToken('-'));
            return;
        }

        Reconsume(c, TokenizerState.ScriptData);
    }

    // 13.2.5.19 Script data escape start dash state
    private void StepScriptDataEscapeStartDash(int c)
    {
        if (c == '-')
        {
            _state = TokenizerState.ScriptDataEscapedDashDash;
            _emitted.Enqueue(new CharacterToken('-'));
            return;
        }

        Reconsume(c, TokenizerState.ScriptData);
    }

    // 13.2.5.20 Script data escaped state
    private void StepScriptDataEscaped(int c)
    {
        switch (c)
        {
            case '-':
                _state = TokenizerState.ScriptDataEscapedDash;
                _emitted.Enqueue(new CharacterToken('-'));
                return;
            case '<':
                _state = TokenizerState.ScriptDataEscapedLessThanSign;
                return;
            case 0:
                _errors.Report(HtmlParseError.UnexpectedNullCharacter, _line, _column);
                _emitted.Enqueue(new CharacterToken(0xFFFD));
                return;
            default:
                _emitted.Enqueue(new CharacterToken(c));
                return;
        }
    }

    // 13.2.5.21 Script data escaped dash state
    private void StepScriptDataEscapedDash(int c)
    {
        switch (c)
        {
            case '-':
                _state = TokenizerState.ScriptDataEscapedDashDash;
                _emitted.Enqueue(new CharacterToken('-'));
                return;
            case '<':
                _state = TokenizerState.ScriptDataEscapedLessThanSign;
                return;
            case 0:
                _state = TokenizerState.ScriptDataEscaped;
                _errors.Report(HtmlParseError.UnexpectedNullCharacter, _line, _column);
                _emitted.Enqueue(new CharacterToken(0xFFFD));
                return;
            default:
                _state = TokenizerState.ScriptDataEscaped;
                _emitted.Enqueue(new CharacterToken(c));
                return;
        }
    }

    // 13.2.5.22 Script data escaped dash dash state
    private void StepScriptDataEscapedDashDash(int c)
    {
        switch (c)
        {
            case '-':
                _emitted.Enqueue(new CharacterToken('-'));
                return;
            case '<':
                _state = TokenizerState.ScriptDataEscapedLessThanSign;
                return;
            case '>':
                _state = TokenizerState.ScriptData;
                _emitted.Enqueue(new CharacterToken('>'));
                return;
            case 0:
                _state = TokenizerState.ScriptDataEscaped;
                _errors.Report(HtmlParseError.UnexpectedNullCharacter, _line, _column);
                _emitted.Enqueue(new CharacterToken(0xFFFD));
                return;
            default:
                _state = TokenizerState.ScriptDataEscaped;
                _emitted.Enqueue(new CharacterToken(c));
                return;
        }
    }

    // 13.2.5.23 Script data escaped less-than sign state
    private void StepScriptDataEscapedLt(int c)
    {
        if (c == '/')
        {
            _tempBuffer.Clear();
            _state = TokenizerState.ScriptDataEscapedEndTagOpen;
            return;
        }

        if (IsAsciiAlpha(c))
        {
            _tempBuffer.Clear();
            AppendToTempLower(c);
            _state = TokenizerState.ScriptDataDoubleEscapeStart;
            _emitted.Enqueue(new CharacterToken('<'));
            _emitted.Enqueue(new CharacterToken(c));
            return;
        }

        _emitted.Enqueue(new CharacterToken('<'));
        Reconsume(c, TokenizerState.ScriptDataEscaped);
    }

    // 13.2.5.24 Script data escaped end tag open state
    private void StepScriptDataEscapedEndTagOpen(int c)
    {
        if (IsAsciiAlpha(c))
        {
            StartNewTag(isEnd: true);
            Reconsume(c, TokenizerState.ScriptDataEscapedEndTagName);
            return;
        }

        _emitted.Enqueue(new CharacterToken('<'));
        _emitted.Enqueue(new CharacterToken('/'));
        Reconsume(c, TokenizerState.ScriptDataEscaped);
    }

    // 13.2.5.25 Script data escaped end tag name state
    private void StepScriptDataEscapedEndTagName(int c) =>
        StepEndTagNameCommon(c, returnState: TokenizerState.ScriptDataEscaped);

    // 13.2.5.26 Script data double escape start state
    private void StepScriptDataDoubleEscapeStart(int c) =>
        StepScriptDataDoubleEscapeBoundary(
            c,
            ifScript: TokenizerState.ScriptDataDoubleEscaped,
            otherwise: TokenizerState.ScriptDataEscaped);

    // 13.2.5.27 Script data double escaped state
    private void StepScriptDataDoubleEscaped(int c)
    {
        switch (c)
        {
            case '-':
                _state = TokenizerState.ScriptDataDoubleEscapedDash;
                _emitted.Enqueue(new CharacterToken('-'));
                return;
            case '<':
                _state = TokenizerState.ScriptDataDoubleEscapedLessThanSign;
                _emitted.Enqueue(new CharacterToken('<'));
                return;
            case 0:
                _errors.Report(HtmlParseError.UnexpectedNullCharacter, _line, _column);
                _emitted.Enqueue(new CharacterToken(0xFFFD));
                return;
            default:
                _emitted.Enqueue(new CharacterToken(c));
                return;
        }
    }

    // 13.2.5.28 Script data double escaped dash state
    private void StepScriptDataDoubleEscapedDash(int c)
    {
        switch (c)
        {
            case '-':
                _state = TokenizerState.ScriptDataDoubleEscapedDashDash;
                _emitted.Enqueue(new CharacterToken('-'));
                return;
            case '<':
                _state = TokenizerState.ScriptDataDoubleEscapedLessThanSign;
                _emitted.Enqueue(new CharacterToken('<'));
                return;
            case 0:
                _state = TokenizerState.ScriptDataDoubleEscaped;
                _errors.Report(HtmlParseError.UnexpectedNullCharacter, _line, _column);
                _emitted.Enqueue(new CharacterToken(0xFFFD));
                return;
            default:
                _state = TokenizerState.ScriptDataDoubleEscaped;
                _emitted.Enqueue(new CharacterToken(c));
                return;
        }
    }

    // 13.2.5.29 Script data double escaped dash dash state
    private void StepScriptDataDoubleEscapedDashDash(int c)
    {
        switch (c)
        {
            case '-':
                _emitted.Enqueue(new CharacterToken('-'));
                return;
            case '<':
                _state = TokenizerState.ScriptDataDoubleEscapedLessThanSign;
                _emitted.Enqueue(new CharacterToken('<'));
                return;
            case '>':
                _state = TokenizerState.ScriptData;
                _emitted.Enqueue(new CharacterToken('>'));
                return;
            case 0:
                _state = TokenizerState.ScriptDataDoubleEscaped;
                _errors.Report(HtmlParseError.UnexpectedNullCharacter, _line, _column);
                _emitted.Enqueue(new CharacterToken(0xFFFD));
                return;
            default:
                _state = TokenizerState.ScriptDataDoubleEscaped;
                _emitted.Enqueue(new CharacterToken(c));
                return;
        }
    }

    // 13.2.5.30 Script data double escaped less-than sign state
    private void StepScriptDataDoubleEscapedLt(int c)
    {
        if (c == '/')
        {
            _tempBuffer.Clear();
            _state = TokenizerState.ScriptDataDoubleEscapeEnd;
            _emitted.Enqueue(new CharacterToken('/'));
            return;
        }

        Reconsume(c, TokenizerState.ScriptDataDoubleEscaped);
    }

    // 13.2.5.31 Script data double escape end state
    private void StepScriptDataDoubleEscapeEnd(int c) =>
        StepScriptDataDoubleEscapeBoundary(
            c,
            ifScript: TokenizerState.ScriptDataEscaped,
            otherwise: TokenizerState.ScriptDataDoubleEscaped);

    private void StepScriptDataDoubleEscapeBoundary(
        int c,
        TokenizerState ifScript,
        TokenizerState otherwise)
    {
        if (IsAsciiWhitespace(c) || c == '/' || c == '>')
        {
            _state = TempBufferIsScript() ? ifScript : otherwise;
            _emitted.Enqueue(new CharacterToken(c));
            return;
        }

        if (IsAsciiAlpha(c))
        {
            AppendToTempLower(c);
            _emitted.Enqueue(new CharacterToken(c));
            return;
        }

        Reconsume(c, otherwise);
    }

    private void AppendToTempLower(int c)
    {
        _tempBuffer.Append(IsAsciiUpper(c) ? (char)(c + 0x20) : (char)c);
    }

    private bool TempBufferIsScript()
        => _tempBuffer.Length == 6 && _tempBuffer.ToString() == "script";
}
