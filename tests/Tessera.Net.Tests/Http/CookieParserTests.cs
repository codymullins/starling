using FluentAssertions;
using Tessera.Net.Http.Cookies;
using Xunit;

namespace Tessera.Net.Tests.Http;

public class CookieParserTests
{
    [Fact]
    public void Parses_simple_pair()
    {
        var c = CookieParser.Parse("session=abc123");
        c.Should().NotBeNull();
        c!.Name.Should().Be("session");
        c.Value.Should().Be("abc123");
    }

    [Fact]
    public void Trims_double_quotes_from_value()
    {
        var c = CookieParser.Parse("k=\"v with spaces\"");
        c.Should().NotBeNull();
        c!.Value.Should().Be("v with spaces");
    }

    [Fact]
    public void Parses_attributes_in_any_order()
    {
        var c = CookieParser.Parse(
            "session=abc; Secure; Domain=example.com; Path=/api; Max-Age=3600; HttpOnly; SameSite=Strict");
        c.Should().NotBeNull();
        c!.Domain.Should().Be("example.com");
        c.Path.Should().Be("/api");
        c.MaxAge.Should().Be(3600);
        c.Secure.Should().BeTrue();
        c.HttpOnly.Should().BeTrue();
        c.SameSite.Should().Be(SameSiteMode.Strict);
    }

    [Fact]
    public void Strips_leading_dot_from_domain()
    {
        var c = CookieParser.Parse("k=v; Domain=.EXAMPLE.com");
        c!.Domain.Should().Be("example.com");
    }

    [Fact]
    public void Returns_null_for_missing_equals_sign()
    {
        CookieParser.Parse("noEqualsHere").Should().BeNull();
    }

    [Fact]
    public void Returns_null_for_empty_name()
    {
        CookieParser.Parse("=value").Should().BeNull();
    }

    [Fact]
    public void Parses_negative_max_age_as_immediate_expiry()
    {
        var c = CookieParser.Parse("k=v; Max-Age=-1");
        c!.MaxAge.Should().Be(-1);
    }

    [Theory]
    [InlineData("Wed, 09 Jun 2021 10:18:14 GMT")]
    [InlineData("Sun, 06 Nov 1994 08:49:37 GMT")]
    public void Parses_rfc1123_expires(string raw)
    {
        var c = CookieParser.Parse($"k=v; Expires={raw}");
        c!.Expires.Should().NotBeNull();
        c.Expires!.Value.Year.Should().BeOneOf(1994, 2021);
    }

    [Fact]
    public void Unknown_attribute_is_ignored()
    {
        var c = CookieParser.Parse("k=v; FuzzyAttribute=42; Path=/");
        c!.Path.Should().Be("/");
    }

    [Fact]
    public void Path_must_start_with_slash_else_ignored()
    {
        var c = CookieParser.Parse("k=v; Path=relative");
        c!.Path.Should().BeNull();
    }

    [Fact]
    public void SameSite_None_is_recognized()
    {
        var c = CookieParser.Parse("k=v; Secure; SameSite=None");
        c!.SameSite.Should().Be(SameSiteMode.None);
    }

    [Fact]
    public void SameSite_unknown_value_falls_back_to_lax()
    {
        var c = CookieParser.Parse("k=v; SameSite=garbage");
        c!.SameSite.Should().Be(SameSiteMode.Lax);
    }
}
