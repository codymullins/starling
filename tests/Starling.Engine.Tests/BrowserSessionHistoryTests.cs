using System.Collections.Concurrent;
using System.Text;
using AwesomeAssertions;
using SixLabors.ImageSharp;

namespace Starling.Engine.Tests;

/// <summary>
/// History tracking across navigation patterns: settled-then-navigate (each
/// page fully loads before the next click) and rapid-fire-overlap (next nav
/// starts while previous loads are still in flight). Both must leave history
/// as A → B → C so Back walks back through B and then A.
/// </summary>
[TestClass]
public class BrowserSessionHistoryTests
{
    [TestMethod]
    public async Task Sequential_navigations_record_history_and_back_walks_through_each()
    {
        using var server = await StubHttpServer.StartAsync(req =>
            BuildResponse(BodyFor(RequestPath(req)), "text/html; charset=utf-8"));

        using var session = new BrowserSession();
        var urlA = $"http://localhost:{server.Port}/a";
        var urlB = $"http://localhost:{server.Port}/b";
        var urlC = $"http://localhost:{server.Port}/c";
        var options = new RenderOptions(new Size(320, 180), 16f);

        var a = await session.NavigateInteractiveAsync(urlA, options);
        a.IsOk.Should().BeTrue(a.IsErr ? a.Error.Message : "");
        session.History.Current.Should().Be(urlA);

        var b = await session.NavigateInteractiveAsync(urlB, options);
        b.IsOk.Should().BeTrue(b.IsErr ? b.Error.Message : "");
        session.History.Current.Should().Be(urlB);

        var c = await session.NavigateInteractiveAsync(urlC, options);
        c.IsOk.Should().BeTrue(c.IsErr ? c.Error.Message : "");
        session.History.Current.Should().Be(urlC);

        session.History.Entries.Should().Equal(urlA, urlB, urlC);
        session.History.CanGoBack.Should().BeTrue();

        var backToB = await session.BackInteractiveAsync(options);
        backToB.IsOk.Should().BeTrue(backToB.IsErr ? backToB.Error.Message : "");
        session.History.Current.Should().Be(urlB);

        var backToA = await session.BackInteractiveAsync(options);
        backToA.IsOk.Should().BeTrue(backToA.IsErr ? backToA.Error.Message : "");
        session.History.Current.Should().Be(urlA);
        session.History.CanGoBack.Should().BeFalse();
    }

    [TestMethod]
    public async Task Overlapping_navigations_record_history_in_arrival_order()
    {
        // One gate per path so the test controls when each in-flight response
        // is delivered. The handler runs on the server's connection task and
        // blocks there until the gate is released. Because the stub server
        // serves each accepted connection on its own task (and these nav
        // requests come in on three separate connections — no keep-alive),
        // all three can sit blocked at the same time.
        var gates = new ConcurrentDictionary<string, TaskCompletionSource>();
        TaskCompletionSource GateFor(string path) => gates.GetOrAdd(
            path,
            _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        using var server = await StubHttpServer.StartAsync(req =>
        {
            var path = RequestPath(req);
            GateFor(path).Task.GetAwaiter().GetResult();
            return BuildResponse(BodyFor(path), "text/html; charset=utf-8");
        });

        using var session = new BrowserSession();
        var urlA = $"http://localhost:{server.Port}/a";
        var urlB = $"http://localhost:{server.Port}/b";
        var urlC = $"http://localhost:{server.Port}/c";
        var options = new RenderOptions(new Size(320, 180), 16f);

        // Kick all three off before any can complete. Each task is parked
        // inside the engine's HTML fetch waiting on the matching gate.
        var taskA = session.NavigateInteractiveAsync(urlA, options);
        var taskB = session.NavigateInteractiveAsync(urlB, options);
        var taskC = session.NavigateInteractiveAsync(urlC, options);

        // Release in arrival order and await each before releasing the next.
        // History.Navigate runs inside NavigateInteractiveAsync after
        // LayoutPageAsync returns; sequencing the completions sequences the
        // recorded order without relying on completion-time luck.
        GateFor("/a").SetResult();
        var a = await taskA;
        a.IsOk.Should().BeTrue(a.IsErr ? a.Error.Message : "");

        GateFor("/b").SetResult();
        var b = await taskB;
        b.IsOk.Should().BeTrue(b.IsErr ? b.Error.Message : "");

        GateFor("/c").SetResult();
        var c = await taskC;
        c.IsOk.Should().BeTrue(c.IsErr ? c.Error.Message : "");

        session.History.Entries.Should().Equal(urlA, urlB, urlC);
        session.History.Current.Should().Be(urlC);

        // Back walks C → B → A. Each Back re-fetches via the same gated
        // server; the gates are already released so the refetch returns
        // immediately.
        var backToB = await session.BackInteractiveAsync(options);
        backToB.IsOk.Should().BeTrue(backToB.IsErr ? backToB.Error.Message : "");
        session.History.Current.Should().Be(urlB);

        var backToA = await session.BackInteractiveAsync(options);
        backToA.IsOk.Should().BeTrue(backToA.IsErr ? backToA.Error.Message : "");
        session.History.Current.Should().Be(urlA);
        session.History.CanGoBack.Should().BeFalse();
    }

    [TestMethod]
    public async Task First_painted_page_stays_in_history_when_load_is_canceled_before_settling()
    {
        // Mirrors the GUI bug: user sees B render (first-paint fires),
        // then clicks C before B's deferred phase finishes. MainWindow
        // cancels B's CT when the next nav starts, so B's task throws
        // mid-flight. History must still include B — the user saw it.
        //
        // Each page carries a <script> so the engine takes the progressive
        // (first-paint + deferred) path. The server gates HTML responses
        // per path so the test controls when each nav can commit.
        var gates = new ConcurrentDictionary<string, TaskCompletionSource>();
        TaskCompletionSource GateFor(string path) => gates.GetOrAdd(
            path,
            _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        using var server = await StubHttpServer.StartAsync(req =>
        {
            var path = RequestPath(req);
            GateFor(path).Task.GetAwaiter().GetResult();
            var body = Encoding.UTF8.GetBytes(
                $"<!doctype html><body><p>{path}</p><script>void 0;</script></body>");
            return BuildResponse(body, "text/html; charset=utf-8");
        });

        using var session = new BrowserSession();
        var urlA = $"http://localhost:{server.Port}/a";
        var urlB = $"http://localhost:{server.Port}/b";
        var urlC = $"http://localhost:{server.Port}/c";
        var options = new RenderOptions(new Size(320, 180), 16f);

        GateFor("/a").SetResult();
        var a = await session.NavigateInteractiveAsync(urlA, options, onFirstPaint: _ => { });
        a.IsOk.Should().BeTrue(a.IsErr ? a.Error.Message : "");

        // Start B, wait for its first-paint (= the moment the user "sees"
        // the page), then cancel — emulating the user clicking C before
        // B's deferred phase finishes.
        var ctsB = new CancellationTokenSource();
        var firstPaintB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var taskB = session.NavigateInteractiveAsync(
            urlB, options, ctsB.Token, _ => firstPaintB.TrySetResult());
        GateFor("/b").SetResult();
        await firstPaintB.Task;
        ctsB.Cancel();
        try { await taskB; } catch (OperationCanceledException) { /* expected */ }

        GateFor("/c").SetResult();
        var c = await session.NavigateInteractiveAsync(urlC, options, onFirstPaint: _ => { });
        c.IsOk.Should().BeTrue(c.IsErr ? c.Error.Message : "");

        session.History.Entries.Should().Equal(urlA, urlB, urlC);

        var back = await session.BackInteractiveAsync(options);
        back.IsOk.Should().BeTrue(back.IsErr ? back.Error.Message : "");
        session.History.Current.Should().Be(urlB);
    }

    private static string RequestPath(string request)
    {
        // Request line: "METHOD SP PATH SP HTTP/1.1\r\n..."
        var firstSp = request.IndexOf(' ');
        var secondSp = request.IndexOf(' ', firstSp + 1);
        return request[(firstSp + 1)..secondSp];
    }

    private static byte[] BodyFor(string path)
        => Encoding.UTF8.GetBytes($"<!doctype html><body><p>{path}</p></body>");

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
