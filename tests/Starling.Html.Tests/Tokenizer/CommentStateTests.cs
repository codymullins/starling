using FluentAssertions;
using Tessera.Html.Tokenizer;
using Xunit;

namespace Tessera.Html.Tests.Tokenizer;

/// <summary>
/// Comment + CDATA + BogusComment + MarkupDeclarationOpen — wp:M1-01e.
/// </summary>
public class CommentStateTests
{
    [Fact]
    public void Simple_comment()
    {
        Tokenize("<!--hello-->").Should().Equal(
            new CommentToken("hello"),
            EndOfFileToken.Instance);
    }

    [Fact]
    public void Empty_comment()
    {
        Tokenize("<!---->").Should().Equal(
            new CommentToken(""),
            EndOfFileToken.Instance);
    }

    [Fact]
    public void Comment_with_dashes_inside()
    {
        Tokenize("<!-- a -- b -->").Should().Equal(
            new CommentToken(" a -- b "),
            EndOfFileToken.Instance);
    }

    [Fact]
    public void Comment_with_internal_lt_chars()
    {
        Tokenize("<!-- <p> -->").Should().Equal(
            new CommentToken(" <p> "),
            EndOfFileToken.Instance);
    }

    [Fact]
    public void Abrupt_closing_of_empty_comment_short()
    {
        // <!--> per §13.2.5.43 abrupt-closing-of-empty-comment
        var sink = new RecordingSink();
        var t = new HtmlTokenizer(sink);
        t.Feed("<!-->");
        t.EndOfInput();

        DrainAll(t).Should().Equal(
            new CommentToken(""),
            EndOfFileToken.Instance);
        sink.Errors.Should().ContainSingle()
            .Which.code.Should().Be(HtmlParseError.AbruptClosingOfEmptyComment);
    }

    [Fact]
    public void Abrupt_closing_of_empty_comment_long()
    {
        // <!---> per §13.2.5.44 (start-dash → abrupt)
        var sink = new RecordingSink();
        var t = new HtmlTokenizer(sink);
        t.Feed("<!--->");
        t.EndOfInput();

        DrainAll(t).Should().Equal(
            new CommentToken(""),
            EndOfFileToken.Instance);
        sink.Errors.Should().ContainSingle()
            .Which.code.Should().Be(HtmlParseError.AbruptClosingOfEmptyComment);
    }

    [Fact]
    public void Incorrectly_closed_comment()
    {
        // <!-- foo --!> per §13.2.5.52
        var sink = new RecordingSink();
        var t = new HtmlTokenizer(sink);
        t.Feed("<!-- foo --!>");
        t.EndOfInput();

        DrainAll(t).Should().Equal(
            new CommentToken(" foo "),
            EndOfFileToken.Instance);
        sink.Errors.Should().ContainSingle()
            .Which.code.Should().Be(HtmlParseError.IncorrectlyClosedComment);
    }

    [Fact]
    public void Incorrectly_opened_comment_falls_into_bogus()
    {
        // <!foo>  — not "--", "DOCTYPE", or "[CDATA[" → bogus comment with
        // data starting from 'f'. (The 'f' is what we read first in
        // MarkupDeclarationOpen; we've decided no branch is viable.)
        var sink = new RecordingSink();
        var t = new HtmlTokenizer(sink);
        t.Feed("<!foo>");
        t.EndOfInput();

        DrainAll(t).Should().Equal(
            new CommentToken("foo"),
            EndOfFileToken.Instance);
        sink.Errors.Should().ContainSingle()
            .Which.code.Should().Be(HtmlParseError.IncorrectlyOpenedComment);
    }

    [Fact]
    public void Bogus_comment_from_question_mark_tag_open()
    {
        // <?foo>  — TagOpen sees '?', emits parse error, creates empty
        // comment, reconsumes in BogusComment.
        var sink = new RecordingSink();
        var t = new HtmlTokenizer(sink);
        t.Feed("<?foo>");
        t.EndOfInput();

        DrainAll(t).Should().Equal(
            new CommentToken("?foo"),
            EndOfFileToken.Instance);
        sink.Errors.Should().ContainSingle()
            .Which.code.Should().Be(HtmlParseError.UnexpectedQuestionMarkInsteadOfTagName);
    }

    [Fact]
    public void Cdata_in_html_content_becomes_bogus_comment()
    {
        // Without foreign content, <![CDATA[…]]> is a parse error and the
        // text becomes the comment data.
        var sink = new RecordingSink();
        var t = new HtmlTokenizer(sink);
        t.Feed("<![CDATA[x]]>");
        t.EndOfInput();

        DrainAll(t).Should().Equal(
            new CommentToken("[CDATA[x]]"),
            EndOfFileToken.Instance);
        sink.Errors.Should().ContainSingle()
            .Which.code.Should().Be(HtmlParseError.CdataInHtmlContent);
    }

    [Fact]
    public void Eof_inside_open_comment_emits_partial_comment_and_parse_error()
    {
        var sink = new RecordingSink();
        var t = new HtmlTokenizer(sink);
        t.Feed("<!-- unfinished");
        t.EndOfInput();

        DrainAll(t).Should().Equal(
            new CommentToken(" unfinished"),
            EndOfFileToken.Instance);
        sink.Errors.Should().ContainSingle()
            .Which.code.Should().Be(HtmlParseError.EofInComment);
    }

    [Fact]
    public void Null_in_comment_maps_to_replacement_character()
    {
        var sink = new RecordingSink();
        var t = new HtmlTokenizer(sink);
        t.Feed("<!--a\0b-->");
        t.EndOfInput();

        DrainAll(t).Should().Equal(
            new CommentToken("a�b"),
            EndOfFileToken.Instance);
        sink.Errors.Should().ContainSingle()
            .Which.code.Should().Be(HtmlParseError.UnexpectedNullCharacter);
    }

    // Helpers ---------------------------------------------------------------

    private static List<HtmlToken> Tokenize(string s)
    {
        var t = new HtmlTokenizer();
        t.Feed(s);
        t.EndOfInput();
        return DrainAll(t);
    }

    private static List<HtmlToken> DrainAll(HtmlTokenizer t)
    {
        var tokens = new List<HtmlToken>();
        while (true)
        {
            var tok = t.ReadToken();
            if (tok is null) return tokens;
            tokens.Add(tok);
            if (tok is EndOfFileToken) return tokens;
        }
    }

    private sealed class RecordingSink : IParseErrorSink
    {
        public List<(HtmlParseError code, int line, int column)> Errors { get; } = [];

        public void Report(HtmlParseError code, int line, int column)
            => Errors.Add((code, line, column));
    }
}
