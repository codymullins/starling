using System.Text;
using FluentAssertions;
namespace Starling.Net.Tests.Http;

/// <summary>
/// Live-network acceptance test for M2-05. Skipped by default. To run:
/// <code>STARLING_LIVE_HTTP_TESTS=1 dotnet test</code>.
/// </summary>
[TestClass]
public class LiveHttpsTests
{
    [TestMethod]
    public async Task GET_example_com_returns_200_and_HTML_body()
    {
        if (Environment.GetEnvironmentVariable("STARLING_LIVE_HTTP_TESTS") != "1")
            return;

        using var client = new StarlingHttpClient(new StarlingHttpClientOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(15),
            RequestTimeout = TimeSpan.FromSeconds(30),
        });

        var result = await client.GetAsync("https://example.com/", CancellationToken.None);

        result.IsOk.Should().BeTrue($"GET failed: {(result.IsOk ? "" : result.Error.ToString())}");
        var resp = result.Value;
        resp.StatusCode.Should().Be(200);

        var text = Encoding.UTF8.GetString(resp.Body.Span);
        text.Should().Contain("<html", "the body should be HTML");
        text.Should().Contain("Example Domain");
    }
}
