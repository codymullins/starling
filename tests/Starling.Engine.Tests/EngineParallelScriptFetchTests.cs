using System.Net;
using System.Net.Sockets;
using System.Text;
using AwesomeAssertions;
using SixLabors.ImageSharp;

namespace Starling.Engine.Tests;

/// <summary>
/// External scripts are fetched in parallel (HTML §4.12.1 constrains execution
/// order, not fetch order), but classic scripts still execute in document
/// order. Before this the fetcher awaited each script's download in turn, so a
/// page with N external scripts paid N sequential round-trips.
/// </summary>
[TestClass]
public sealed class EngineParallelScriptFetchTests
{
    [TestMethod]
    public async Task Two_external_scripts_are_fetched_concurrently()
    {
        // Each script response is delayed, so if the fetches overlap the server
        // sees two requests in flight at once; if they were sequential it would
        // never exceed one.
        var routes = new Dictionary<string, Route>
        {
            ["/page.html"] = new("text/html",
                "<!doctype html><html><body><p>hi</p>" +
                "<script src=\"/a.js\"></script><script src=\"/b.js\"></script>" +
                "</body></html>", DelayMs: 0),
            ["/a.js"] = new("text/javascript", "var a = 1;", DelayMs: 150),
            ["/b.js"] = new("text/javascript", "var b = 2;", DelayMs: 150),
        };

        using var server = new ConcurrencyTrackingServer(routes);
        var outcome = await RenderAsync(server, "/page.html");

        outcome.IsOk.Should().BeTrue(outcome.IsErr ? outcome.Error.Message : "");
        server.RequestsServed.Should().Be(3, "document + two scripts");
        server.MaxConcurrency.Should().BeGreaterThanOrEqualTo(2,
            "the two external scripts must be fetched in parallel");
    }

    [TestMethod]
    public async Task Classic_scripts_execute_in_document_order_even_when_the_later_one_downloads_first()
    {
        // b.js returns immediately; a.js is slow. Parallel fetch means b is
        // available first, but document order (a before b) must still win at
        // execution time, yielding "ab".
        var routes = new Dictionary<string, Route>
        {
            ["/page.html"] = new("text/html",
                "<!doctype html><html><body><p id=\"out\"></p>" +
                "<script src=\"/a.js\"></script><script src=\"/b.js\"></script>" +
                "</body></html>", DelayMs: 0),
            ["/a.js"] = new("text/javascript",
                "var o=document.getElementById('out'); o.textContent = o.textContent + 'a';", DelayMs: 150),
            ["/b.js"] = new("text/javascript",
                "var o=document.getElementById('out'); o.textContent = o.textContent + 'b';", DelayMs: 0),
        };

        using var server = new ConcurrencyTrackingServer(routes);
        var outcome = await RenderAsync(server, "/page.html");

        outcome.IsOk.Should().BeTrue(outcome.IsErr ? outcome.Error.Message : "");
        outcome.Value.DisplayText.Should().Contain("ab",
            "classic scripts run in document order regardless of which finished downloading first");
    }

    private static async Task<Starling.Common.Result<RenderOutcome, RenderError>> RenderAsync(
        ConcurrencyTrackingServer server, string path)
    {
        var tempPng = Path.Combine(Path.GetTempPath(), $"starling-parscript-{Guid.NewGuid():N}.png");
        try
        {
            var engine = new StarlingEngine();
            return await engine.RenderAsync(
                $"http://127.0.0.1:{server.Port}{path}",
                new RenderOptions(new Size(800, 600), 16f),
                tempPng,
                CancellationToken.None);
        }
        finally
        {
            try { if (File.Exists(tempPng)) File.Delete(tempPng); } catch { /* best-effort */ }
        }
    }

    private readonly record struct Route(string ContentType, string Body, int DelayMs);

    /// <summary>
    /// Keep-alive loopback HTTP/1.1 server that delays each response and tracks
    /// the peak number of simultaneously in-flight requests, so a test can prove
    /// the client fetched things in parallel.
    /// </summary>
    private sealed class ConcurrencyTrackingServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Dictionary<string, Route> _routes;
        private int _inFlight;
        private int _maxConcurrency;
        private int _requestsServed;

        public int Port { get; }
        public int MaxConcurrency => Volatile.Read(ref _maxConcurrency);
        public int RequestsServed => Volatile.Read(ref _requestsServed);

        public ConcurrencyTrackingServer(Dictionary<string, Route> routes)
        {
            _routes = routes;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = AcceptLoopAsync();
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false); }
                catch { break; }
                _ = HandleConnectionAsync(client);
            }
        }

        private async Task HandleConnectionAsync(TcpClient client)
        {
            using (client)
            await using (var stream = client.GetStream())
            {
                while (!_cts.IsCancellationRequested)
                {
                    var path = await ReadRequestPathAsync(stream, _cts.Token).ConfigureAwait(false);
                    if (path is null) return;

                    Interlocked.Increment(ref _requestsServed);
                    var inFlight = Interlocked.Increment(ref _inFlight);
                    UpdateMax(inFlight);
                    try
                    {
                        var route = _routes.TryGetValue(path, out var r)
                            ? r
                            : new Route("text/plain", "not found", 0);
                        if (route.DelayMs > 0)
                            await Task.Delay(route.DelayMs, _cts.Token).ConfigureAwait(false);

                        var status = _routes.ContainsKey(path) ? "200 OK" : "404 Not Found";
                        var bodyBytes = Encoding.UTF8.GetBytes(route.Body);
                        var header =
                            $"HTTP/1.1 {status}\r\n" +
                            $"Content-Type: {route.ContentType}\r\n" +
                            $"Content-Length: {bodyBytes.Length}\r\n" +
                            "Connection: keep-alive\r\n\r\n";
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), _cts.Token).ConfigureAwait(false);
                        await stream.WriteAsync(bodyBytes, _cts.Token).ConfigureAwait(false);
                        await stream.FlushAsync(_cts.Token).ConfigureAwait(false);
                    }
                    catch { return; }
                    finally { Interlocked.Decrement(ref _inFlight); }
                }
            }
        }

        private void UpdateMax(int observed)
        {
            int prev;
            while ((prev = Volatile.Read(ref _maxConcurrency)) < observed
                   && Interlocked.CompareExchange(ref _maxConcurrency, observed, prev) != prev)
            {
                // retry
            }
        }

        private static async Task<string?> ReadRequestPathAsync(NetworkStream stream, CancellationToken ct)
        {
            var sb = new StringBuilder();
            var buf = new byte[1024];
            while (!sb.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                int n;
                try { n = await stream.ReadAsync(buf, ct).ConfigureAwait(false); }
                catch { return null; }
                if (n == 0) return null;
                sb.Append(Encoding.ASCII.GetString(buf, 0, n));
            }
            var firstLine = sb.ToString().Split("\r\n", StringSplitOptions.None)[0];
            var parts = firstLine.Split(' ');
            return parts.Length >= 2 ? parts[1] : "/";
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { /* best-effort */ }
            _cts.Dispose();
        }
    }
}
