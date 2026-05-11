using System.Text;
using FluentAssertions;
using Tessera.Net.Http;
using Tessera.Net.Http.H1;
using Xunit;
using TesseraUrl = global::Tessera.Url.Url;
using TesseraUrlParser = global::Tessera.Url.UrlParser;

namespace Tessera.Net.Tests.Http;

public class H1RequestWriterTests
{
    private static TesseraUrl ParseUrl(string s)
    {
        var r = TesseraUrlParser.Parse(s);
        r.IsOk.Should().BeTrue($"failed to parse {s}");
        return r.Value;
    }

    [Fact]
    public void Writes_request_line_with_origin_form_path()
    {
        var req = HttpRequest.Get(ParseUrl("https://example.com/foo/bar?x=1"));
        var writer = new H1RequestWriter();

        var bytes = writer.SerializeHead(req);
        var text = Encoding.ASCII.GetString(bytes);

        text.Should().StartWith("GET /foo/bar?x=1 HTTP/1.1\r\n");
    }

    [Fact]
    public void Defaults_path_to_slash_when_url_path_is_empty()
    {
        var req = HttpRequest.Get(ParseUrl("https://example.com"));
        var writer = new H1RequestWriter();

        var text = Encoding.ASCII.GetString(writer.SerializeHead(req));
        text.Should().StartWith("GET / HTTP/1.1\r\n");
    }

    [Fact]
    public void Adds_default_headers_when_missing()
    {
        var req = HttpRequest.Get(ParseUrl("https://example.com/"));
        var writer = new H1RequestWriter();

        var text = Encoding.ASCII.GetString(writer.SerializeHead(req));

        text.Should().Contain("Host: example.com\r\n");
        text.Should().Contain("User-Agent: Tessera/0.1");
        text.Should().Contain("Accept: text/html");
        text.Should().Contain("Accept-Encoding: gzip, br, deflate\r\n");
        text.Should().Contain("Connection: keep-alive\r\n");
        text.Should().EndWith("\r\n\r\n");
    }

    [Fact]
    public void Includes_explicit_port_in_host_header_when_not_default()
    {
        var req = HttpRequest.Get(ParseUrl("http://example.com:8080/"));
        var writer = new H1RequestWriter();

        var text = Encoding.ASCII.GetString(writer.SerializeHead(req));
        text.Should().Contain("Host: example.com:8080\r\n");
    }

    [Fact]
    public void Omits_default_port_in_host_header()
    {
        var req = HttpRequest.Get(ParseUrl("https://example.com:443/"));
        var writer = new H1RequestWriter();

        var text = Encoding.ASCII.GetString(writer.SerializeHead(req));
        text.Should().Contain("Host: example.com\r\n");
        text.Should().NotContain(":443");
    }

    [Fact]
    public void User_provided_headers_override_defaults()
    {
        var headers = new HttpHeaders();
        headers.Add("Accept-Encoding", "identity");
        headers.Add("User-Agent", "Custom/1.0");

        var req = HttpRequest.Get(ParseUrl("https://example.com/"), headers);
        var writer = new H1RequestWriter();

        var text = Encoding.ASCII.GetString(writer.SerializeHead(req));

        text.Should().Contain("Accept-Encoding: identity\r\n");
        text.Should().Contain("User-Agent: Custom/1.0\r\n");
        text.Should().NotContain("Accept-Encoding: gzip");
        text.Should().NotContain("Tessera/0.1");
    }

    [Fact]
    public void Adds_content_length_when_body_present_and_caller_did_not_specify()
    {
        var url = ParseUrl("https://example.com/post");
        var body = Encoding.UTF8.GetBytes("hello=world");
        var req = new HttpRequest("POST", url, headers: null, body: body);

        var writer = new H1RequestWriter();
        var text = Encoding.ASCII.GetString(writer.SerializeHead(req));

        text.Should().Contain($"Content-Length: {body.Length}\r\n");
    }

    [Fact]
    public void Omits_content_length_when_caller_specified_transfer_encoding()
    {
        var url = ParseUrl("https://example.com/post");
        var body = Encoding.UTF8.GetBytes("ignored");
        var headers = new HttpHeaders();
        headers.Add("Transfer-Encoding", "chunked");
        var req = new HttpRequest("POST", url, headers, body);

        var writer = new H1RequestWriter();
        var text = Encoding.ASCII.GetString(writer.SerializeHead(req));

        text.Should().NotContain("Content-Length:");
        text.Should().Contain("Transfer-Encoding: chunked\r\n");
    }

    [Fact]
    public async Task WriteAsync_emits_header_block_then_body()
    {
        var url = ParseUrl("https://example.com/api");
        var body = Encoding.UTF8.GetBytes("{}");
        var headers = new HttpHeaders();
        headers.Add("Content-Type", "application/json");

        var req = new HttpRequest("POST", url, headers, body);

        using var ms = new MemoryStream();
        await new H1RequestWriter().WriteAsync(req, ms, TestContext.Current.CancellationToken);

        var text = Encoding.UTF8.GetString(ms.ToArray());

        text.Should().StartWith("POST /api HTTP/1.1\r\n");
        text.Should().Contain("Content-Type: application/json\r\n");
        text.Should().Contain($"Content-Length: {body.Length}\r\n");
        text.Should().EndWith("\r\n\r\n{}");
    }

    [Fact]
    public void Sends_query_string_when_present()
    {
        var req = HttpRequest.Get(ParseUrl("https://example.com/search?q=hello+world&n=10"));
        var writer = new H1RequestWriter();

        var text = Encoding.ASCII.GetString(writer.SerializeHead(req));
        text.Should().StartWith("GET /search?q=hello+world&n=10 HTTP/1.1\r\n");
    }
}
