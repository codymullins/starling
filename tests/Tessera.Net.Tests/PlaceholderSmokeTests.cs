using FluentAssertions;
using Xunit;

namespace Tessera.Net.Tests;

public class PlaceholderSmokeTests
{
    [Fact]
    public void NotImplementedFetcher_loudly_refuses()
    {
        var fetcher = new NotImplementedFetcher();
        var ct = TestContext.Current.CancellationToken;
        var act = async () => await fetcher.GetAsync(
            new Tessera.Url.Url("https", "example.com", null, "/", null, null), ct);
        act.Should().ThrowAsync<NotSupportedException>();
    }
}
