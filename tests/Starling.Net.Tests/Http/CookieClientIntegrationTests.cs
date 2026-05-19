using System.Text;
using FluentAssertions;
using Starling.Net.Http.Cookies;
namespace Starling.Net.Tests.Http;

[TestClass]
public class CookieClientIntegrationTests
{
    [TestMethod]
    public async Task Cookies_set_by_first_response_are_echoed_on_second_request()
    {
        var receivedCookieHeaders = new List<string>();

        using var server = await StubHttpServer.StartAsync(req =>
        {
            // Capture incoming Cookie header (or "" if absent).
            var lines = req.Split("\r\n");
            var cookieHdr = lines.FirstOrDefault(l => l.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase));
            receivedCookieHeaders.Add(cookieHdr is null ? "" : cookieHdr["Cookie:".Length..].Trim());

            // First response sets a cookie; subsequent ones reflect it.
            var head =
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: text/plain\r\n" +
                "Set-Cookie: session=abc123\r\n" +
                "Set-Cookie: lang=en; Path=/\r\n" +
                "Content-Length: 2\r\n" +
                "Connection: close\r\n\r\nok";
            return Encoding.ASCII.GetBytes(head);
        });

        var jar = new CookieJar();
        using var client = new StarlingHttpClient(new StarlingHttpClientOptions { CookieJar = jar });

        var ct = CancellationToken.None;
        var url = $"http://localhost:{server.Port}/";
        var first = await client.GetAsync(url, ct);
        first.IsOk.Should().BeTrue();

        var second = await client.GetAsync(url, ct);
        second.IsOk.Should().BeTrue();

        // First request had no cookies; second sent both back.
        receivedCookieHeaders.Should().HaveCount(2);
        receivedCookieHeaders[0].Should().Be("");
        receivedCookieHeaders[1].Should().Contain("session=abc123").And.Contain("lang=en");
    }

    [TestMethod]
    public async Task HttpOnly_attribute_is_stored_but_does_not_affect_outgoing_header()
    {
        using var server = await StubHttpServer.StartAsync(_ =>
        {
            var head =
                "HTTP/1.1 200 OK\r\n" +
                "Set-Cookie: secret=xyz; HttpOnly\r\n" +
                "Content-Length: 0\r\n" +
                "Connection: close\r\n\r\n";
            return Encoding.ASCII.GetBytes(head);
        });

        var jar = new CookieJar();
        using var client = new StarlingHttpClient(new StarlingHttpClientOptions { CookieJar = jar });

        var ct = CancellationToken.None;
        var url = $"http://localhost:{server.Port}/";
        var r = await client.GetAsync(url, ct);
        r.IsOk.Should().BeTrue();
        // HttpOnly only hides the cookie from JS — over the wire it is still
        // sent on subsequent same-origin HTTP requests.
        jar.BuildCookieHeader(global::Starling.Url.UrlParser.Parse(url).Value)
            .Should().Be("secret=xyz");
    }
}
