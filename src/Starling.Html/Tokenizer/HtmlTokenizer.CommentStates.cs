namespace Tessera.Html.Tokenizer;

/// <summary>
/// Comment + CDATA + BogusComment + MarkupDeclarationOpen states from
/// WHATWG HTML §13.2.5.41–.58 and the CDATA states §13.2.5.69–.71. Owned
/// by wp:M1-01e.
/// </summary>
/// <remarks>
/// MarkupDeclarationOpen does bounded multi-character lookahead (up to 7
/// chars for <c>[CDATA[</c> / <c>DOCTYPE</c>). We accumulate observed chars
/// in <c>_tempBuffer</c> and decide once enough is seen, falling back to
/// bogus-comment when no branch is still viable. The accumulated buffer
/// is replayed through the bogus-comment handler so we don't lose chars
/// on the fall-through path.
/// </remarks>
public sealed partial class HtmlTokenizer
{
    private void DispatchCommentState(TokenizerState state, int c)
    {
        switch (state)
        {
            case TokenizerState.MarkupDeclarationOpen:        StepMarkupDeclarationOpen(c); break;
            case TokenizerState.CommentStart:                 StepCommentStart(c); break;
            case TokenizerState.CommentStartDash:             StepCommentStartDash(c); break;
            case TokenizerState.Comment:                      StepComment(c); break;
            case TokenizerState.CommentLessThanSign:          StepCommentLt(c); break;
            case TokenizerState.CommentLessThanSignBang:      StepCommentLtBang(c); break;
            case TokenizerState.CommentLessThanSignBangDash:  StepCommentLtBangDash(c); break;
            case TokenizerState.CommentLessThanSignBangDashDash: StepCommentLtBangDashDash(c); break;
            case TokenizerState.CommentEndDash:               StepCommentEndDash(c); break;
            case TokenizerState.CommentEnd:                   StepCommentEnd(c); break;
            case TokenizerState.CommentEndBang:               StepCommentEndBang(c); break;
            case TokenizerState.BogusComment:                 StepBogusComment(c); break;
            case TokenizerState.CdataSection:                 StepCdataSection(c); break;
            case TokenizerState.CdataSectionBracket:          StepCdataSectionBracket(c); break;
            case TokenizerState.CdataSectionEnd:              StepCdataSectionEnd(c); break;
            default: throw new InvalidOperationException(
                $"DispatchCommentState invoked for unrelated state '{state}'.");
        }
    }

    private void StepCommentEof()
    {
        switch (_state)
        {
            case TokenizerState.MarkupDeclarationOpen:
                // No spec entry but the reasonable behavior is: take the
                // "anything else" branch and emit the buffered chars as
                // bogus-comment data + EOF.
                _errors.Report(HtmlParseError.IncorrectlyOpenedComment, _line, _column);
                _commentData.Clear();
                _commentData.Append(_tempBuffer);
                _tempBuffer.Clear();
                _emitted.Enqueue(new CommentToken(_commentData.ToString()));
                _commentData.Clear();
                _emitted.Enqueue(EndOfFileToken.Instance);
                return;

            case TokenizerState.CommentStart:
            case TokenizerState.CommentStartDash:
            case TokenizerState.Comment:
            case TokenizerState.CommentLessThanSign:
            case TokenizerState.CommentLessThanSignBang:
            case TokenizerState.CommentLessThanSignBangDash:
            case TokenizerState.CommentLessThanSignBangDashDash:
            case TokenizerState.CommentEndDash:
            case TokenizerState.CommentEnd:
            case TokenizerState.CommentEndBang:
                _errors.Report(HtmlParseError.EofInComment, _line, _column);
                _emitted.Enqueue(new CommentToken(_commentData.ToString()));
                _commentData.Clear();
                _emitted.Enqueue(EndOfFileToken.Instance);
                return;

            case TokenizerState.BogusComment:
                _emitted.Enqueue(new CommentToken(_commentData.ToString()));
                _commentData.Clear();
                _emitted.Enqueue(EndOfFileToken.Instance);
                return;

            case TokenizerState.CdataSection:
            case TokenizerState.CdataSectionBracket:
            case TokenizerState.CdataSectionEnd:
                _errors.Report(HtmlParseError.EofInCdata, _line, _column);
                if (_state == TokenizerState.CdataSectionBracket)
                {
                    _emitted.Enqueue(new CharacterToken(']'));
                }
                else if (_state == TokenizerState.CdataSectionEnd)
                {
                    _emitted.Enqueue(new CharacterToken(']'));
                    _emitted.Enqueue(new CharacterToken(']'));
                }
                _emitted.Enqueue(EndOfFileToken.Instance);
                return;
        }
    }

    // -----------------------------------------------------------------------
    // 13.2.5.42 Markup declaration open state — bounded multi-char lookahead.
    // -----------------------------------------------------------------------
    private void StepMarkupDeclarationOpen(int c)
    {
        _tempBuffer.Append((char)c);
        var p = _tempBuffer.ToString();

        var dashViable = "--".StartsWith(p, StringComparison.Ordinal);
        var doctypeViable = "DOCTYPE".StartsWith(p, StringComparison.OrdinalIgnoreCase);
        var cdataViable = "[CDATA[".StartsWith(p, StringComparison.Ordinal);

        if (dashViable && p.Length == 2)
        {
            _commentData.Clear();
            _tempBuffer.Clear();
            _state = TokenizerState.CommentStart;
            return;
        }
        if (doctypeViable && p.Length == 7)
        {
            _tempBuffer.Clear();
            _state = TokenizerState.Doctype;
            return;
        }
        if (cdataViable && p.Length == 7)
        {
            // No foreign content yet (tree builder lands in M1-02). Spec says
            // outside foreign content this is cdata-in-html-content parse
            // error → comment "[CDATA[" → bogus comment state.
            _errors.Report(HtmlParseError.CdataInHtmlContent, _line, _column);
            _commentData.Clear();
            _commentData.Append("[CDATA[");
            _tempBuffer.Clear();
            _state = TokenizerState.BogusComment;
            return;
        }
        if (dashViable || doctypeViable || cdataViable)
        {
            return; // need more input
        }

        // No branch viable. Fall back to bogus comment, replaying the
        // accumulated chars through the bogus-comment handler so they
        // become part of the comment data.
        _errors.Report(HtmlParseError.IncorrectlyOpenedComment, _line, _column);
        var saved = _tempBuffer.ToString();
        _tempBuffer.Clear();
        _commentData.Clear();
        _state = TokenizerState.BogusComment;
        foreach (var ch in saved)
        {
            StepBogusComment(ch);
            if (_eofProcessed) return;
        }
    }

    // -----------------------------------------------------------------------
    // 13.2.5.43 Comment start state
    // -----------------------------------------------------------------------
    private void StepCommentStart(int c)
    {
        switch (c)
        {
            case '-':
                _state = TokenizerState.CommentStartDash;
                return;
            case '>':
                _errors.Report(HtmlParseError.AbruptClosingOfEmptyComment, _line, _column);
                _state = TokenizerState.Data;
                _emitted.Enqueue(new CommentToken(_commentData.ToString()));
                _commentData.Clear();
                return;
        }
        Reconsume(c, TokenizerState.Comment);
    }

    // 13.2.5.44 Comment start dash state
    private void StepCommentStartDash(int c)
    {
        switch (c)
        {
            case '-':
                _state = TokenizerState.CommentEnd;
                return;
            case '>':
                _errors.Report(HtmlParseError.AbruptClosingOfEmptyComment, _line, _column);
                _state = TokenizerState.Data;
                _emitted.Enqueue(new CommentToken(_commentData.ToString()));
                _commentData.Clear();
                return;
        }
        _commentData.Append('-');
        Reconsume(c, TokenizerState.Comment);
    }

    // 13.2.5.45 Comment state
    private void StepComment(int c)
    {
        switch (c)
        {
            case '<':
                _commentData.Append('<');
                _state = TokenizerState.CommentLessThanSign;
                return;
            case '-':
                _state = TokenizerState.CommentEndDash;
                return;
            case 0:
                _errors.Report(HtmlParseError.UnexpectedNullCharacter, _line, _column);
                _commentData.Append((char)0xFFFD);
                return;
        }
        _commentData.Append((char)c);
    }

    // 13.2.5.46 Comment less-than sign state
    private void StepCommentLt(int c)
    {
        if (c == '!')
        {
            _commentData.Append('!');
            _state = TokenizerState.CommentLessThanSignBang;
            return;
        }
        if (c == '<')
        {
            _commentData.Append('<');
            return;
        }
        Reconsume(c, TokenizerState.Comment);
    }

    // 13.2.5.47 Comment less-than sign bang state
    private void StepCommentLtBang(int c)
    {
        if (c == '-')
        {
            _state = TokenizerState.CommentLessThanSignBangDash;
            return;
        }
        Reconsume(c, TokenizerState.Comment);
    }

    // 13.2.5.48 Comment less-than sign bang dash state
    private void StepCommentLtBangDash(int c)
    {
        if (c == '-')
        {
            _state = TokenizerState.CommentLessThanSignBangDashDash;
            return;
        }
        Reconsume(c, TokenizerState.CommentEndDash);
    }

    // 13.2.5.49 Comment less-than sign bang dash dash state
    private void StepCommentLtBangDashDash(int c)
    {
        if (c == '>')
        {
            Reconsume(c, TokenizerState.CommentEnd);
            return;
        }
        _errors.Report(HtmlParseError.NestedComment, _line, _column);
        Reconsume(c, TokenizerState.CommentEnd);
    }

    // 13.2.5.50 Comment end dash state
    private void StepCommentEndDash(int c)
    {
        if (c == '-')
        {
            _state = TokenizerState.CommentEnd;
            return;
        }
        _commentData.Append('-');
        Reconsume(c, TokenizerState.Comment);
    }

    // 13.2.5.51 Comment end state
    private void StepCommentEnd(int c)
    {
        switch (c)
        {
            case '>':
                _state = TokenizerState.Data;
                _emitted.Enqueue(new CommentToken(_commentData.ToString()));
                _commentData.Clear();
                return;
            case '!':
                _state = TokenizerState.CommentEndBang;
                return;
            case '-':
                _commentData.Append('-');
                return;
        }
        _commentData.Append('-');
        _commentData.Append('-');
        Reconsume(c, TokenizerState.Comment);
    }

    // 13.2.5.52 Comment end bang state
    private void StepCommentEndBang(int c)
    {
        switch (c)
        {
            case '-':
                _commentData.Append("--!");
                _state = TokenizerState.CommentEndDash;
                return;
            case '>':
                _errors.Report(HtmlParseError.IncorrectlyClosedComment, _line, _column);
                _state = TokenizerState.Data;
                _emitted.Enqueue(new CommentToken(_commentData.ToString()));
                _commentData.Clear();
                return;
        }
        _commentData.Append("--!");
        Reconsume(c, TokenizerState.Comment);
    }

    // -----------------------------------------------------------------------
    // 13.2.5.41 Bogus comment state — consume everything until '>' or EOF as
    // comment data, mapping NULL → U+FFFD.
    // -----------------------------------------------------------------------
    private void StepBogusComment(int c)
    {
        switch (c)
        {
            case '>':
                _state = TokenizerState.Data;
                _emitted.Enqueue(new CommentToken(_commentData.ToString()));
                _commentData.Clear();
                return;
            case 0:
                _errors.Report(HtmlParseError.UnexpectedNullCharacter, _line, _column);
                _commentData.Append((char)0xFFFD);
                return;
        }
        _commentData.Append((char)c);
    }

    // -----------------------------------------------------------------------
    // 13.2.5.69 CDATA section state — only reachable from foreign content.
    // -----------------------------------------------------------------------
    private void StepCdataSection(int c)
    {
        if (c == ']')
        {
            _state = TokenizerState.CdataSectionBracket;
            return;
        }
        // CDATA emits chars verbatim; NULL is not transformed.
        _emitted.Enqueue(new CharacterToken(c));
    }

    // 13.2.5.70 CDATA section bracket state
    private void StepCdataSectionBracket(int c)
    {
        if (c == ']')
        {
            _state = TokenizerState.CdataSectionEnd;
            return;
        }
        _emitted.Enqueue(new CharacterToken(']'));
        Reconsume(c, TokenizerState.CdataSection);
    }

    // 13.2.5.71 CDATA section end state
    private void StepCdataSectionEnd(int c)
    {
        switch (c)
        {
            case ']':
                _emitted.Enqueue(new CharacterToken(']'));
                return;
            case '>':
                _state = TokenizerState.Data;
                return;
        }
        _emitted.Enqueue(new CharacterToken(']'));
        _emitted.Enqueue(new CharacterToken(']'));
        Reconsume(c, TokenizerState.CdataSection);
    }
}
