using System.Text;
using AwesomeAssertions;
using SixLabors.ImageSharp;

namespace Starling.Engine.Tests;

/// <summary>
/// Pins the contract that <see cref="LaidOutPage.Url"/> and
/// <see cref="NavigationHistory.Current"/> both name the new page by the
/// time first-paint fires. <see cref="BrowserSession.NavigateInteractiveAsync"/>
/// commits to history at first-paint (the navigation-commit point), so the
/// shell can read either source when refreshing the URL bar without
/// flickering back to the previous page.
/// </summary>
[TestClass]
public class BrowserSessionFirstPaintHistoryTests
{
    [TestMethod]
    public async Task First_paint_observes_the_new_url_in_both_page_and_history()
    {
        // Include an inline script so the engine takes the progressive
        // (first-paint + deferred) path — onFirstPaint only fires when the
        // page has scripts (engine treats script-free pages as one-shot).
        using var server = await StubHttpServer.StartAsync(_ =>
            BuildResponse(
                Encoding.UTF8.GetBytes("<body><p>hi</p><script>void 0;</script></body>"),
                "text/html; charset=utf-8"));

        using var session = new BrowserSession();
        var firstUrl = $"http://localhost:{server.Port}/first";
        var secondUrl = $"http://localhost:{server.Port}/second";

        // Seed history so the second navigation has a non-empty previous URL —
        // the stomp condition in the original shell code only triggered when
        // there was a prior entry to read.
        var first = await session.NavigateInteractiveAsync(
            firstUrl,
            new RenderOptions(new Size(320, 180), 16f),
            CancellationToken.None);
        first.IsOk.Should().BeTrue(first.IsErr ? first.Error.Message : "");
        session.History.Current.Should().Be(firstUrl);

        // Capture both signals at the moment first-paint fires.
        string? historyAtFirstPaint = null;
        string? pageUrlAtFirstPaint = null;
        var second = await session.NavigateInteractiveAsync(
            secondUrl,
            new RenderOptions(new Size(320, 180), 16f),
            CancellationToken.None,
            onFirstPaint: page =>
            {
                pageUrlAtFirstPaint = page.Url;
                historyAtFirstPaint = session.History.Current;
            });
        second.IsOk.Should().BeTrue(second.IsErr ? second.Error.Message : "");

        // History commits at first-paint — the navigation-commit point —
        // so the shell can read History.Current here without flickering
        // back to the previous URL. Pin that so future refactors don't
        // quietly defer the commit back to the post-settle path.
        historyAtFirstPaint.Should().Be(secondUrl,
            "History commits at first-paint, not after the deferred phase");

        // The page also carries the navigation's target URL at first-paint;
        // either source is safe for the URL bar.
        pageUrlAtFirstPaint.Should().Be(secondUrl,
            "LaidOutPage.Url names the new page at first-paint");
    }

    private static byte[] BuildResponse(byte[] body, string contentType)
    {
        var head = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n\r\n");
        var combined = new byte[head.Length + body.Length];
        Buffer.BlockCopy(head, 0, combined, 0, head.Length);
        Buffer.BlockCopy(body, 0, combined, head.Length, body.Length);
        return combined;
    }
}
