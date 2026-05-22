using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using AwesomeAssertions;
using Starling.Gui.Chrome;
using Starling.Gui.Theme;

namespace Starling.Gui.Headless.Tests;

/// <summary>
/// Coverage for the URL-bar lock popover (connection security): protocol +
/// certificate summary plus the fail-closed policy note. Builds the popover card
/// directly from a <see cref="UrlBar"/> so no live window/click is needed.
/// </summary>
public class SecurityPopoverTests
{
    private static List<string> CardText(SiteSecurity security)
    {
        var bar = new UrlBar(new ThemeManager());
        bar.SetSecurity(security);
        var card = bar.BuildSecurityCardForTest();
        return card.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Select(tb => tb.Text ?? string.Empty)
            .Where(s => s.Length > 0)
            .ToList();
    }

    [AvaloniaFact]
    public void Secure_h2_site_shows_protocol_and_valid_certificate()
    {
        var texts = CardText(new SiteSecurity(
            Encrypted: true, Secure: true, Protocol: "HTTP/2", Certificate: true,
            CertSubject: "www.example.com", CertIssuer: "ISRG Root X1",
            CertNotBefore: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            CertNotAfter: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        texts.Should().Contain(t => t.Contains("Connection is secure", StringComparison.Ordinal));
        texts.Should().Contain(t => t.Contains("HTTP/2", StringComparison.Ordinal));
        texts.Should().Contain(t => t.Contains("Valid", StringComparison.Ordinal));
        // Certificate details are present in the tree (collapsed by default).
        texts.Should().Contain(t => t.Contains("www.example.com", StringComparison.Ordinal));
        texts.Should().Contain(t => t.Contains("ISRG Root X1", StringComparison.Ordinal));
        texts.Should().Contain("Valid from");
        texts.Should().Contain("Valid to");
        // Validity dates render (shown in local time, so assert on the year range).
        texts.Should().Contain(t => t.Contains("2025", StringComparison.Ordinal) || t.Contains("2026", StringComparison.Ordinal));
        // Fail-closed policy note is always shown.
        texts.Should().Contain(t => t.Contains("invalid certificates", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void Plain_http_site_is_marked_not_secure_with_no_certificate()
    {
        var texts = CardText(new SiteSecurity(
            Encrypted: false, Secure: false, Protocol: "HTTP/1.1", Certificate: false,
            CertSubject: null, CertIssuer: null, CertNotBefore: null, CertNotAfter: null));

        texts.Should().Contain(t => t.Contains("Not secure", StringComparison.Ordinal));
        texts.Should().Contain(t => t.Contains("HTTP/1.1", StringComparison.Ordinal));
        texts.Should().Contain(t => t.Contains("None", StringComparison.Ordinal));
    }
}
