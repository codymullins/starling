using FluentAssertions;
using Xunit;

namespace Tessera.Url.Tests;

public class UrlParserTests
{
    [Fact]
    public void Parses_file_url_with_absolute_path()
    {
        var r = UrlParser.Parse("file:///tmp/hello.html");
        r.IsOk.Should().BeTrue();
        var u = r.Value;
        u.Scheme.Should().Be("file");
        u.Host.Should().BeNull();
        u.Path.Should().Be("/tmp/hello.html");
        u.IsFile.Should().BeTrue();
    }

    [Fact]
    public void Parses_https_url_with_default_port()
    {
        var r = UrlParser.Parse("https://example.com/foo?bar=1#frag");
        r.IsOk.Should().BeTrue();
        var u = r.Value;
        u.Scheme.Should().Be("https");
        u.Host.Should().Be("example.com");
        u.Port.Should().BeNull();
        u.Path.Should().Be("/foo");
        u.Query.Should().Be("bar=1");
        u.Fragment.Should().Be("frag");
    }

    [Fact]
    public void Parses_http_url_with_explicit_port()
    {
        var r = UrlParser.Parse("http://localhost:8080/api");
        r.IsOk.Should().BeTrue();
        r.Value.Port.Should().Be(8080);
        r.Value.Host.Should().Be("localhost");
    }

    [Fact]
    public void Rejects_unknown_scheme()
    {
        var r = UrlParser.Parse("ftp://example.com/x");
        r.IsErr.Should().BeTrue();
        r.Error.Should().Be(UrlParser.ParseError.UnsupportedScheme);
    }

    [Fact]
    public void Rejects_empty_input()
        => UrlParser.Parse("").IsErr.Should().BeTrue();

    [Fact]
    public void File_url_to_filesystem_path_strips_authority()
    {
        var u = UrlParser.Parse("file:///etc/hosts").Value;
        u.ToFileSystemPath().Should().Be("/etc/hosts");
    }
}
