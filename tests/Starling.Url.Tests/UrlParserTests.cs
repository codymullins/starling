using FluentAssertions;
namespace Starling.Url.Tests;

[TestClass]
public class UrlParserTests
{
    // ----- M0 baseline (still expected to work) ---------------------------

    [TestMethod]
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

    [TestMethod]
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

    [TestMethod]
    public void Parses_http_url_with_explicit_port()
    {
        var r = UrlParser.Parse("http://localhost:8080/api");
        r.IsOk.Should().BeTrue();
        r.Value.Port.Should().Be(8080);
        r.Value.Host.Should().Be("localhost");
    }

    [TestMethod]
    public void Rejects_empty_input()
        => UrlParser.Parse("").IsErr.Should().BeTrue();

    [TestMethod]
    public void File_url_to_filesystem_path_strips_authority()
    {
        var u = UrlParser.Parse("file:///etc/hosts").Value;
        u.ToFileSystemPath().Should().Be("/etc/hosts");
    }

    // ----- M2-01a: WHATWG-faithful behaviors ------------------------------

    [TestMethod]
    public void Scheme_is_lowercased()
    {
        UrlParser.Parse("HTTPS://Example.COM/").Value.Scheme.Should().Be("https");
    }

    [TestMethod]
    public void Host_is_lowercased_for_special_schemes()
    {
        UrlParser.Parse("https://Example.COM/").Value.Host.Should().Be("example.com");
    }

    [TestMethod]
    public void Default_port_collapses_to_null()
    {
        // :443 on https is the default and should not be retained.
        UrlParser.Parse("https://example.com:443/").Value.Port.Should().BeNull();
    }

    [TestMethod]
    public void Non_default_port_is_retained()
    {
        UrlParser.Parse("https://example.com:8443/").Value.Port.Should().Be(8443);
    }

    [TestMethod]
    public void Ftp_is_now_a_recognized_special_scheme()
    {
        // M0 rejected ftp; M2 accepts it as special.
        var r = UrlParser.Parse("ftp://files.example.com/x");
        r.IsOk.Should().BeTrue();
        r.Value.Scheme.Should().Be("ftp");
        r.Value.IsSpecial.Should().BeTrue();
        r.Value.DefaultPort.Should().Be(21);
    }

    [TestMethod]
    public void Websocket_is_a_recognized_special_scheme()
    {
        UrlParser.Parse("wss://example.com/socket").Value.Scheme.Should().Be("wss");
    }

    [TestMethod]
    public void Userinfo_is_parsed()
    {
        var u = UrlParser.Parse("https://alice:secret@example.com/dashboard").Value;
        u.Username.Should().Be("alice");
        u.Password.Should().Be("secret");
        u.Host.Should().Be("example.com");
        u.Path.Should().Be("/dashboard");
    }

    [TestMethod]
    public void Userinfo_username_only()
    {
        var u = UrlParser.Parse("https://alice@example.com/").Value;
        u.Username.Should().Be("alice");
        u.Password.Should().BeNull();
    }

    [TestMethod]
    public void Path_dot_segments_are_normalized()
    {
        var u = UrlParser.Parse("https://example.com/a/b/../c/./d").Value;
        u.Path.Should().Be("/a/c/d");
    }

    [TestMethod]
    public void Double_dot_above_root_clamps_at_root()
    {
        var u = UrlParser.Parse("https://example.com/../../../foo").Value;
        u.Path.Should().Be("/foo");
    }

    [TestMethod]
    public void Path_percent_encodes_spaces_and_specials()
    {
        var u = UrlParser.Parse("https://example.com/hello world").Value;
        u.Path.Should().Be("/hello%20world");
    }

    [TestMethod]
    public void Query_percent_encodes_spaces()
    {
        var u = UrlParser.Parse("https://example.com/?q=hello world").Value;
        u.Query.Should().Be("q=hello%20world");
    }

    [TestMethod]
    public void Fragment_separated_at_hash()
    {
        var u = UrlParser.Parse("https://example.com/x#frag ment").Value;
        u.Path.Should().Be("/x");
        u.Fragment.Should().Be("frag%20ment");
    }

    [TestMethod]
    public void IPv4_octal_form_is_canonicalized()
    {
        // 0177 = 127 (octal), 0.0.1 = 0.0.1
        var u = UrlParser.Parse("http://0177.0.0.1/").Value;
        u.Host.Should().Be("127.0.0.1");
    }

    [TestMethod]
    public void IPv4_hex_form_is_canonicalized()
    {
        var u = UrlParser.Parse("http://0x7F.0.0.1/").Value;
        u.Host.Should().Be("127.0.0.1");
    }

    [TestMethod]
    public void IPv4_single_number_form_is_canonicalized()
    {
        // 2130706433 = 127.0.0.1 as a single 32-bit number
        var u = UrlParser.Parse("http://2130706433/").Value;
        u.Host.Should().Be("127.0.0.1");
    }

    [TestMethod]
    public void Non_special_scheme_with_opaque_path()
    {
        var u = UrlParser.Parse("mailto:alice@example.com").Value;
        u.Scheme.Should().Be("mailto");
        u.IsSpecial.Should().BeFalse();
        // mailto: doesn't use authority — the rest is an opaque path.
        u.Host.Should().BeNull();
        u.Path.Should().Be("alice@example.com");
    }

    [TestMethod]
    public void Backslash_in_special_scheme_acts_as_slash()
    {
        var u = UrlParser.Parse("http://example.com\\foo\\bar").Value;
        u.Path.Should().Be("/foo/bar");
    }

    [TestMethod]
    public void File_with_localhost_host_collapses_to_null()
    {
        var u = UrlParser.Parse("file://localhost/etc/hosts").Value;
        u.Host.Should().BeNull();
        u.ToFileSystemPath().Should().Be("/etc/hosts");
    }

    [TestMethod]
    public void Extra_slashes_after_special_scheme_are_ignored()
    {
        // Per WHATWG §4.4.1 special-authority-ignore-slashes, `http:///foo`
        // skips the extra '/' and parses 'foo' as the host. Matches
        // Chromium/Firefox/Node URL.
        var u = UrlParser.Parse("http:///foo").Value;
        u.Host.Should().Be("foo");
    }

    [TestMethod]
    public void Invalid_port_returns_error()
    {
        UrlParser.Parse("http://example.com:99999/").IsErr.Should().BeTrue();
    }

    [TestMethod]
    public void Tabs_and_newlines_are_stripped_before_parsing()
    {
        var u = UrlParser.Parse("https://exam\tple.com/fo\no").Value;
        u.Host.Should().Be("example.com");
        u.Path.Should().Be("/foo");
    }

    [TestMethod]
    public void ToString_round_trips_for_normalized_urls()
    {
        UrlParser.Parse("https://example.com/foo?bar#baz").Value.ToString()
            .Should().Be("https://example.com/foo?bar#baz");
        UrlParser.Parse("https://alice:secret@example.com/").Value.ToString()
            .Should().Be("https://alice:secret@example.com/");
    }

    [TestMethod]
    public void Relative_filename_resolves_against_file_base()
    {
        var baseUrl = UrlParser.Parse("file:///tmp/pages/page.html").Value;
        var u = UrlParser.Parse("swatch.png", baseUrl).Value;
        u.Scheme.Should().Be("file");
        u.Path.Should().Be("/tmp/pages/swatch.png");
    }

    [TestMethod]
    public void Relative_filename_resolves_against_https_base()
    {
        var baseUrl = UrlParser.Parse("https://example.com/dir/index.html").Value;
        var u = UrlParser.Parse("logo.png", baseUrl).Value;
        u.Scheme.Should().Be("https");
        u.Host.Should().Be("example.com");
        u.Path.Should().Be("/dir/logo.png");
    }

    [TestMethod]
    public void Absolute_path_resolves_against_base_host()
    {
        var baseUrl = UrlParser.Parse("https://example.com/dir/index.html").Value;
        var u = UrlParser.Parse("/other/img.png", baseUrl).Value;
        u.Host.Should().Be("example.com");
        u.Path.Should().Be("/other/img.png");
    }
}
