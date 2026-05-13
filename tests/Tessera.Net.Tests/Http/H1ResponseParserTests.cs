using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Tessera.Net.Http;
using Tessera.Net.Http.H1;
using Xunit;

namespace Tessera.Net.Tests.Http;

public class H1ResponseParserTests
{
    private static MemoryStream Bytes(string s) => new(Encoding.ASCII.GetBytes(s));

    private static async Task<HttpResponse> Parse(string text, H1ResponseParser? parser = null)
    {
        var r = await (parser ?? new H1ResponseParser()).ParseAsync(Bytes(text), TestContext.Current.CancellationToken);
        r.IsOk.Should().BeTrue($"parser returned {(r.IsOk ? "Ok" : r.Error.ToString())}");
        return r.Value;
    }

    [Fact]
    public async Task Parses_minimal_response_with_content_length()
    {
        var resp = await Parse("HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhello");
        resp.HttpVersion.Should().Be("HTTP/1.1");
        resp.StatusCode.Should().Be(200);
        resp.ReasonPhrase.Should().Be("OK");
        resp.Headers.GetFirst("Content-Length").Should().Be("5");
        Encoding.ASCII.GetString(resp.Body.Span).Should().Be("hello");
    }

    [Fact]
    public async Task Parses_response_with_empty_body()
    {
        var resp = await Parse("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");
        resp.Body.Length.Should().Be(0);
    }

    [Fact]
    public async Task Handles_status_204_with_no_body_even_without_content_length()
    {
        var resp = await Parse("HTTP/1.1 204 No Content\r\n\r\n");
        resp.StatusCode.Should().Be(204);
        resp.Body.Length.Should().Be(0);
    }

    [Fact]
    public async Task Handles_status_304_with_no_body()
    {
        var resp = await Parse("HTTP/1.1 304 Not Modified\r\nETag: \"abc\"\r\n\r\n");
        resp.StatusCode.Should().Be(304);
        resp.Body.Length.Should().Be(0);
    }

    [Fact]
    public async Task Skips_1xx_informational_responses()
    {
        var raw =
            "HTTP/1.1 100 Continue\r\n\r\n" +
            "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nhi";
        var resp = await Parse(raw);
        resp.StatusCode.Should().Be(200);
        Encoding.ASCII.GetString(resp.Body.Span).Should().Be("hi");
    }

    [Fact]
    public async Task Reads_chunked_body()
    {
        var raw =
            "HTTP/1.1 200 OK\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "\r\n" +
            "5\r\nhello\r\n" +
            "6\r\n world\r\n" +
            "1\r\n!\r\n" +
            "0\r\n\r\n";
        var resp = await Parse(raw);
        Encoding.ASCII.GetString(resp.Body.Span).Should().Be("hello world!");
    }

    [Fact]
    public async Task Reads_close_delimited_body_when_no_framing_headers()
    {
        var raw = "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\n\r\nclose-delimited body";
        var resp = await Parse(raw);
        Encoding.ASCII.GetString(resp.Body.Span).Should().Be("close-delimited body");
    }

    [Fact]
    public async Task Decodes_gzip_content_encoding()
    {
        var payload = Encoding.UTF8.GetBytes("compressed text body");
        using var compressedStream = new MemoryStream();
        using (var gz = new GZipStream(compressedStream, CompressionLevel.Fastest, leaveOpen: true))
            gz.Write(payload);
        var compressed = compressedStream.ToArray();

        var head = $"HTTP/1.1 200 OK\r\nContent-Encoding: gzip\r\nContent-Length: {compressed.Length}\r\n\r\n";
        var headBytes = Encoding.ASCII.GetBytes(head);

        var combined = new byte[headBytes.Length + compressed.Length];
        Buffer.BlockCopy(headBytes, 0, combined, 0, headBytes.Length);
        Buffer.BlockCopy(compressed, 0, combined, headBytes.Length, compressed.Length);

        var parser = new H1ResponseParser();
        var result = await parser.ParseAsync(new MemoryStream(combined), TestContext.Current.CancellationToken);
        result.IsOk.Should().BeTrue();
        result.Value.Body.ToArray().Should().Equal(payload);
    }

    [Fact]
    public async Task Decodes_chunked_then_gzip()
    {
        var payload = Encoding.UTF8.GetBytes("the standard chunked + gzip combo most CDNs send");
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            gz.Write(payload);
        var compressed = ms.ToArray();

        // Chunk the compressed bytes in two halves.
        var half = compressed.Length / 2;
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nContent-Encoding: gzip\r\n\r\n");

        var head = Encoding.ASCII.GetBytes(sb.ToString());
        var first = new byte[half];
        Buffer.BlockCopy(compressed, 0, first, 0, half);
        var second = new byte[compressed.Length - half];
        Buffer.BlockCopy(compressed, half, second, 0, second.Length);

        using var stream = new MemoryStream();
        stream.Write(head);
        stream.Write(Encoding.ASCII.GetBytes($"{first.Length:x}\r\n"));
        stream.Write(first);
        stream.Write(Encoding.ASCII.GetBytes("\r\n"));
        stream.Write(Encoding.ASCII.GetBytes($"{second.Length:x}\r\n"));
        stream.Write(second);
        stream.Write(Encoding.ASCII.GetBytes("\r\n"));
        stream.Write(Encoding.ASCII.GetBytes("0\r\n\r\n"));
        stream.Position = 0;

        var parser = new H1ResponseParser();
        var result = await parser.ParseAsync(stream, TestContext.Current.CancellationToken);
        result.IsOk.Should().BeTrue();
        result.Value.Body.ToArray().Should().Equal(payload);
    }

    [Fact]
    public async Task Multivalued_set_cookie_headers_are_all_visible()
    {
        var raw =
            "HTTP/1.1 200 OK\r\n" +
            "Set-Cookie: a=1\r\n" +
            "Set-Cookie: b=2\r\n" +
            "Content-Length: 0\r\n\r\n";
        var resp = await Parse(raw);
        resp.Headers.GetAll("Set-Cookie").Should().Equal(new[] { "a=1", "b=2" });
    }

    [Fact]
    public async Task Bad_status_line_returns_error()
    {
        var parser = new H1ResponseParser();
        var r = await parser.ParseAsync(Bytes("nope nope nope\r\n\r\n"), TestContext.Current.CancellationToken);
        r.IsErr.Should().BeTrue();
        r.Error.Should().Be(HttpError.BadStatusLine);
    }

    [Fact]
    public async Task Missing_colon_in_header_returns_BadHeader()
    {
        var parser = new H1ResponseParser();
        var r = await parser.ParseAsync(
            Bytes("HTTP/1.1 200 OK\r\nNoColonHere\r\n\r\n"),
            TestContext.Current.CancellationToken);
        r.IsErr.Should().BeTrue();
        r.Error.Should().Be(HttpError.BadHeader);
    }

    [Fact]
    public async Task Header_block_too_large_returns_HeadersTooLarge()
    {
        var giantHeader = "X-Big: " + new string('a', 70_000) + "\r\n";
        var raw = "HTTP/1.1 200 OK\r\n" + giantHeader + "Content-Length: 0\r\n\r\n";
        var parser = new H1ResponseParser { MaxHeaderBlockBytes = 32 * 1024 };
        var r = await parser.ParseAsync(Bytes(raw), TestContext.Current.CancellationToken);
        r.IsErr.Should().BeTrue();
        r.Error.Should().Be(HttpError.HeadersTooLarge);
    }

    [Fact]
    public async Task Truncated_body_returns_UnexpectedEof()
    {
        var parser = new H1ResponseParser();
        // Promise 10 bytes, deliver 3.
        var r = await parser.ParseAsync(
            Bytes("HTTP/1.1 200 OK\r\nContent-Length: 10\r\n\r\nabc"),
            TestContext.Current.CancellationToken);
        r.IsErr.Should().BeTrue();
        r.Error.Should().Be(HttpError.UnexpectedEof);
    }

    [Fact]
    public async Task Body_size_cap_returns_BodyTooLarge_for_content_length()
    {
        var parser = new H1ResponseParser { MaxBodyBytes = 4 };
        var r = await parser.ParseAsync(
            Bytes("HTTP/1.1 200 OK\r\nContent-Length: 8\r\n\r\n12345678"),
            TestContext.Current.CancellationToken);
        r.IsErr.Should().BeTrue();
        r.Error.Should().Be(HttpError.BodyTooLarge);
    }

    [Fact]
    public async Task Unknown_content_encoding_returns_UnsupportedEncoding()
    {
        var parser = new H1ResponseParser();
        var r = await parser.ParseAsync(
            Bytes("HTTP/1.1 200 OK\r\nContent-Encoding: compress\r\nContent-Length: 3\r\n\r\nabc"),
            TestContext.Current.CancellationToken);
        r.IsErr.Should().BeTrue();
        r.Error.Should().Be(HttpError.UnsupportedEncoding);
    }

    [Fact]
    public async Task Reason_phrase_can_be_empty()
    {
        var resp = await Parse("HTTP/1.1 200 \r\nContent-Length: 0\r\n\r\n");
        resp.StatusCode.Should().Be(200);
        resp.ReasonPhrase.Should().Be("");
    }

    [Fact]
    public async Task IndicatesKeepAlive_defaults_to_true_for_HTTP11_without_Connection_header()
    {
        var resp = await Parse("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");
        H1ResponseParser.IndicatesKeepAlive(resp).Should().BeTrue();
    }

    [Fact]
    public async Task IndicatesKeepAlive_false_when_HTTP11_response_has_Connection_close()
    {
        var resp = await Parse("HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 0\r\n\r\n");
        H1ResponseParser.IndicatesKeepAlive(resp).Should().BeFalse();
    }

    [Fact]
    public async Task IndicatesKeepAlive_false_for_HTTP10_without_explicit_keep_alive()
    {
        var resp = await Parse("HTTP/1.0 200 OK\r\nContent-Length: 0\r\n\r\n");
        H1ResponseParser.IndicatesKeepAlive(resp).Should().BeFalse();
    }

    [Fact]
    public async Task IndicatesKeepAlive_true_for_HTTP10_with_explicit_keep_alive()
    {
        var resp = await Parse("HTTP/1.0 200 OK\r\nConnection: keep-alive\r\nContent-Length: 0\r\n\r\n");
        H1ResponseParser.IndicatesKeepAlive(resp).Should().BeTrue();
    }

    [Fact]
    public async Task HasDefiniteBodyFraming_true_for_Content_Length()
    {
        var resp = await Parse("HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhello");
        H1ResponseParser.HasDefiniteBodyFraming(resp).Should().BeTrue();
    }

    [Fact]
    public async Task HasDefiniteBodyFraming_true_for_chunked()
    {
        var resp = await Parse(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nhello\r\n0\r\n\r\n");
        H1ResponseParser.HasDefiniteBodyFraming(resp).Should().BeTrue();
    }

    [Fact]
    public async Task HasDefiniteBodyFraming_true_for_204()
    {
        var resp = await Parse("HTTP/1.1 204 No Content\r\n\r\n");
        H1ResponseParser.HasDefiniteBodyFraming(resp).Should().BeTrue();
    }

    [Fact]
    public async Task HasDefiniteBodyFraming_false_for_close_delimited_body()
    {
        // No Content-Length, no Transfer-Encoding, non-204/304 status.
        // The parser reads to EOF; the connection cannot be safely pooled.
        var resp = await Parse("HTTP/1.0 200 OK\r\n\r\nhello");
        H1ResponseParser.HasDefiniteBodyFraming(resp).Should().BeFalse();
    }
}
