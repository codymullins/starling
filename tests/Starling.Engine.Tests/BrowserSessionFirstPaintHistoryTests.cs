using System.Text;
using AwesomeAssertions;
using SixLabors.ImageSharp;

namespace Starling.Engine.Tests;

/// <summary>
/// Regression for the bookmark-click URL bar flicker.
///
/// Symptom: clicking bookmark B while on A makes the URL bar flicker
/// B → A → B, or (when the deferred phase mutates nothing) stick on A.
///
/// Cause: <see cref="BrowserSession.NavigateInteractiveAsync"/> calls
/// <c>History.Navigate(url)</c> only after <c>LayoutPageAsync</c> returns,
/// while <c>onFirstPaint</c> fires from inside <c>LayoutPageAsync</c>. So
/// at first-paint time <c>History.Current</c> still names the previous
/// page. The shell's first-paint handler must read the page's own URL
/// (<see cref="LaidOutPage.Url"/>) — the only source that's already
/// correct mid-navigation.
/// </summary>
[TestClass]
public class BrowserSessionFirstPaintHistoryTests
{
    [TestMethod]
    public async Task First_paint_page_carries_navigations_url_even_while_history_is_stale()
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

        // History is still stale at first-paint — Navigate(url) runs only
        // after LayoutPageAsync returns. Pin that so future refactors don't
        // quietly reorder it (and so this test documents the contract).
        historyAtFirstPaint.Should().Be(firstUrl,
            "History.Navigate runs only after LayoutPageAsync settles");

        // The page reliably carries the navigation's target URL at first-paint.
        // The GUI shell uses this in ApplyShownPage to refresh the URL bar;
        // reading History.Current there caused the flicker.
        pageUrlAtFirstPaint.Should().Be(secondUrl,
            "LaidOutPage.Url is the URL bar's source of truth at first-paint");
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
