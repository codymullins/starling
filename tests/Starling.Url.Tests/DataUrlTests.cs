using AwesomeAssertions;
namespace Starling.Url.Tests;

/// <summary>
/// <see cref="DataUrl.TryDecode"/> coverage. Data URLs are the inline-bytes
/// alternative to a network image fetch — Google's homepage in particular
/// inlines a number of icons this way, and we depend on this decoder to
/// keep them visible.
/// </summary>
[TestClass]
public sealed class DataUrlTests
{
    [TestMethod]
    public void Decodes_base64_png_payload()
    {
        // 1x1 transparent PNG.
        const string src = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";

        var parsed = UrlParser.Parse(src);
        parsed.IsOk.Should().BeTrue();
        var url = parsed.Value;
        url.IsData.Should().BeTrue();

        DataUrl.TryDecode(url, out var payload).Should().BeTrue();
        payload.MediaType.Should().Be("image/png");
        // PNG magic bytes: 89 50 4E 47 0D 0A 1A 0A
        payload.Bytes.Should().StartWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
    }

    [TestMethod]
    public void Decodes_percent_encoded_text_payload()
    {
        const string src = "data:text/plain,Hello%20World%21";

        var parsed = UrlParser.Parse(src);
        parsed.IsOk.Should().BeTrue();

        DataUrl.TryDecode(parsed.Value, out var payload).Should().BeTrue();
        payload.MediaType.Should().Be("text/plain");
        System.Text.Encoding.ASCII.GetString(payload.Bytes).Should().Be("Hello World!");
    }

    [TestMethod]
    public void Empty_mediatype_defaults_to_text_plain()
    {
        var parsed = UrlParser.Parse("data:,abc");
        parsed.IsOk.Should().BeTrue();
        DataUrl.TryDecode(parsed.Value, out var payload).Should().BeTrue();
        payload.MediaType.Should().Be("text/plain;charset=US-ASCII");
        System.Text.Encoding.ASCII.GetString(payload.Bytes).Should().Be("abc");
    }

    [TestMethod]
    public void Rejects_non_data_scheme()
    {
        var parsed = UrlParser.Parse("https://example.com/img.png");
        parsed.IsOk.Should().BeTrue();
        DataUrl.TryDecode(parsed.Value, out _).Should().BeFalse();
    }

    [TestMethod]
    public void Rejects_malformed_data_url_with_no_comma()
    {
        var parsed = UrlParser.Parse("data:image/png");
        parsed.IsOk.Should().BeTrue();
        DataUrl.TryDecode(parsed.Value, out _).Should().BeFalse();
    }

    [TestMethod]
    public void Rejects_malformed_base64_payload()
    {
        // `!` is not a base64 character.
        var parsed = UrlParser.Parse("data:image/png;base64,!!!notbase64!!!");
        parsed.IsOk.Should().BeTrue();
        DataUrl.TryDecode(parsed.Value, out _).Should().BeFalse();
    }

    [TestMethod]
    public void Real_google_homepage_thumbnail_decodes_successfully()
    {
        // Verbatim from `curl https://www.google.com/` — a placeholder
        // thumbnail spacer that appears as <img src="data:image/gif;base64,...">.
        const string src = "data:image/gif;base64,R0lGODlhAQABAID/AMDAwAAAACH5BAEAAAAALAAAAAABAAEAAAICRAEAOw==";

        var parsed = UrlParser.Parse(src);
        parsed.IsOk.Should().BeTrue();
        DataUrl.TryDecode(parsed.Value, out var payload).Should().BeTrue();
        payload.MediaType.Should().Be("image/gif");
        // GIF89a magic bytes.
        payload.Bytes.Should().StartWith(new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a' });
    }

    [TestMethod]
    public void Rejects_base64_payload_over_custom_limit()
    {
        var parsed = UrlParser.Parse("data:text/plain;base64,SGVsbG8=");
        parsed.IsOk.Should().BeTrue();

        // "Hello" is 5 bytes decoded; cap at 4.
        DataUrl.TryDecode(parsed.Value, out _, maxBytes: 4).Should().BeFalse();
    }

    [TestMethod]
    public void Rejects_percent_payload_over_custom_limit()
    {
        var parsed = UrlParser.Parse("data:text/plain,Hello");
        parsed.IsOk.Should().BeTrue();

        DataUrl.TryDecode(parsed.Value, out _, maxBytes: 4).Should().BeFalse();
    }

    [TestMethod]
    public void Accepts_payload_at_custom_limit_boundary()
    {
        var parsed = UrlParser.Parse("data:text/plain,Hello");
        parsed.IsOk.Should().BeTrue();

        DataUrl.TryDecode(parsed.Value, out var payload, maxBytes: 5).Should().BeTrue();
        System.Text.Encoding.ASCII.GetString(payload.Bytes).Should().Be("Hello");
    }
}
