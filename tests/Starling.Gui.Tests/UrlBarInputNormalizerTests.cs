using FluentAssertions;
using Starling.Gui;
using Xunit;

namespace Starling.Gui.Tests;

/// <summary>
/// Coverage for URL-bar input normalization. The behavior mirrors Chrome's
/// omnibox and Firefox's URIFixup: typing a bare hostname like
/// <c>google.com</c> should navigate to <c>https://google.com</c>, while
/// <c>localhost</c> and IPv4 literals default to <c>http://</c>.
/// </summary>
public sealed class UrlBarInputNormalizerTests
{
    // ---------- Schemeless hostnames default to https -----------------------

    [Theory]
    [InlineData("google.com", "https://google.com")]
    [InlineData("www.google.com", "https://www.google.com")]
    [InlineData("example.co.uk", "https://example.co.uk")]
    [InlineData("google.com/", "https://google.com/")]
    [InlineData("google.com/search?q=cats", "https://google.com/search?q=cats")]
    [InlineData("example.com:8443", "https://example.com:8443")]
    [InlineData("example.com:8443/path", "https://example.com:8443/path")]
    [InlineData("example.com#frag", "https://example.com#frag")]
    public void Schemeless_hostname_defaults_to_https(string input, string expected)
    {
        UrlBarInputNormalizer.Normalize(input).Should().Be(expected);
    }

    // ---------- Trimming and casing ----------------------------------------

    [Theory]
    [InlineData("  google.com  ", "https://google.com")]
    [InlineData("\tgoogle.com\n", "https://google.com")]
    public void Surrounding_whitespace_is_trimmed(string input, string expected)
    {
        UrlBarInputNormalizer.Normalize(input).Should().Be(expected);
    }

    // ---------- Already-qualified URLs pass through ------------------------

    [Theory]
    [InlineData("https://google.com")]
    [InlineData("http://example.com/page")]
    [InlineData("file:///tmp/page.html")]
    [InlineData("about:blank")]
    [InlineData("data:text/html,<p>hi</p>")]
    [InlineData("ftp://files.example.com/")]
    public void Inputs_with_an_explicit_scheme_pass_through(string input)
    {
        UrlBarInputNormalizer.Normalize(input).Should().Be(input);
    }

    [Fact]
    public void Http_input_is_not_silently_upgraded_to_https()
    {
        // The user typed http:// explicitly — respect it. (HSTS upgrades are
        // the engine's responsibility, not the URL bar's.)
        UrlBarInputNormalizer.Normalize("http://example.com")
            .Should().Be("http://example.com");
    }

    // ---------- localhost and loopback default to http ---------------------

    [Theory]
    [InlineData("localhost", "http://localhost")]
    [InlineData("localhost:8080", "http://localhost:8080")]
    [InlineData("localhost:3000/api/health", "http://localhost:3000/api/health")]
    [InlineData("LOCALHOST", "http://LOCALHOST")]
    public void Localhost_defaults_to_http(string input, string expected)
    {
        UrlBarInputNormalizer.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("127.0.0.1", "http://127.0.0.1")]
    [InlineData("127.0.0.1:8080", "http://127.0.0.1:8080")]
    [InlineData("192.168.1.1/admin", "http://192.168.1.1/admin")]
    public void Ipv4_literals_default_to_http(string input, string expected)
    {
        UrlBarInputNormalizer.Normalize(input).Should().Be(expected);
    }

    // ---------- Protocol-relative ------------------------------------------

    [Fact]
    public void Protocol_relative_input_is_promoted_to_https()
    {
        UrlBarInputNormalizer.Normalize("//cdn.example.com/asset.js")
            .Should().Be("https://cdn.example.com/asset.js");
    }

    // ---------- Things that are not URLs at all ----------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Empty_or_whitespace_input_returns_null(string? input)
    {
        UrlBarInputNormalizer.Normalize(input).Should().BeNull();
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("just-a-word")]
    [InlineData("foo_bar")]
    public void Bare_word_with_no_dot_or_port_returns_null(string input)
    {
        // A real browser would route this to its search provider. This
        // engine has no search provider yet, so the shell surfaces an error
        // rather than navigating to a guessed URL.
        UrlBarInputNormalizer.Normalize(input).Should().BeNull();
    }

    [Theory]
    [InlineData("hello world")]
    [InlineData("two words")]
    [InlineData("multi word search query")]
    public void Multi_word_input_returns_null(string input)
    {
        UrlBarInputNormalizer.Normalize(input).Should().BeNull();
    }

    [Theory]
    [InlineData("/foo")]
    [InlineData("/foo/bar")]
    public void Path_only_input_returns_null(string input)
    {
        // No host to navigate to — refuse rather than guessing.
        UrlBarInputNormalizer.Normalize(input).Should().BeNull();
    }

    // ---------- Regression for the original report -------------------------

    [Fact]
    public void User_typing_google_com_navigates_to_https_google_com()
    {
        // The literal example from the bug report: typing a bare hostname
        // into the URL bar should not require the user to prepend "https://".
        UrlBarInputNormalizer.Normalize("google.com").Should().Be("https://google.com");
    }
}
