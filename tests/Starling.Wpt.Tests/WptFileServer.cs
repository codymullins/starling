using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Starling.Wpt.Tests;

/// <summary>
/// A minimal static file server over the vendored WPT checkout, on a loopback
/// port. WPT tests reference resources by server-absolute path
/// (<c>/resources/testharness.js</c>, <c>/common/…</c>), so they must be served
/// over HTTP rather than file:// — this mirrors the official <c>wpt serve</c>,
/// just without its dynamic handlers (.py / pipes), which the chosen subset
/// doesn't need.
///
/// The one piece of magic: requests for <c>/resources/testharnessreport.js</c>
/// are answered with <see cref="ReportJs"/> instead of the file on disk. That
/// file is the official, intended extension point for a test environment to
/// report results; ours forwards them into a DOM attribute the runner reads back.
/// </summary>
internal sealed class WptFileServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly string _root;
    private readonly CancellationTokenSource _cts = new();

    public string BaseUrl { get; }

    public WptFileServer(string suiteRoot)
    {
        _root = Path.GetFullPath(suiteRoot);
        var port = FreeTcpPort();
        BaseUrl = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add(BaseUrl + "/");
        _listener.Start();
        _ = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
            catch { break; } // listener stopped / disposed
            try { Handle(ctx); }
            catch
            {
                try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { /* client gone */ }
            }
        }
    }

    private void Handle(HttpListenerContext ctx)
    {
        var path = Uri.UnescapeDataString(ctx.Request.Url!.AbsolutePath);

        // Inject the result-capturing report script (the official hook).
        if (path == "/resources/testharnessreport.js")
        {
            Write(ctx, Encoding.UTF8.GetBytes(ReportJs), "text/javascript");
            return;
        }

        var rel = path.TrimStart('/');
        var full = Path.GetFullPath(Path.Combine(_root, rel));
        // Path-traversal guard: stay within the suite root.
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && full != _root)
        {
            ctx.Response.StatusCode = 403; ctx.Response.Close(); return;
        }
        if (!File.Exists(full))
        {
            ctx.Response.StatusCode = 404; ctx.Response.Close(); return;
        }
        Write(ctx, File.ReadAllBytes(full), ContentType(full));
    }

    private static void Write(HttpListenerContext ctx, byte[] body, string contentType)
    {
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = contentType;
        ctx.Response.ContentLength64 = body.Length;
        ctx.Response.OutputStream.Write(body, 0, body.Length);
        ctx.Response.OutputStream.Close();
        ctx.Response.Close();
    }

    private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".xml" or ".xht" or ".xhtml" => "application/xhtml+xml; charset=utf-8",
        ".txt" => "text/plain; charset=utf-8",
        ".png" => "image/png",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        _ => "application/octet-stream",
    };

    private static int FreeTcpPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { /* already stopped */ }
        try { _listener.Close(); } catch { /* already closed */ }
        _cts.Dispose();
    }

    /// <summary>
    /// Our <c>testharnessreport.js</c>. testharness.js loads first and defines
    /// <c>add_completion_callback</c>; this registers a callback that serializes
    /// the harness + subtest results into a single JSON blob on
    /// <c>&lt;html data-wpt-results&gt;</c>, which the host reads back after load.
    /// Numeric status codes are testharness's own (harness: 0 OK / 1 ERROR /
    /// 2 TIMEOUT / 3 PRECONDITION_FAILED; subtest: 0 PASS / 1 FAIL / 2 TIMEOUT /
    /// 3 NOTRUN / 4 PRECONDITION_FAILED).
    /// </summary>
    internal const string ReportJs = """
        (function () {
          // Configure testharness for an embedded runner (setup() is global from
          // testharness.js and runs before the test body):
          //   output:false           — skip visual result rendering, whose DOM
          //     APIs (insertAdjacentText, …) would throw inside a completion
          //     callback and abort the chain before ours; we read results
          //     programmatically, so it's pure overhead.
          //   explicit_timeout:true  — disable testharness's own auto-timeout.
          //     The host drives the event loop with advancing virtual time, so
          //     the auto-timeout would trip before a load-driven async test calls
          //     done(); the per-file worker-thread timeout IS our timeout
          //     mechanism (the documented use of explicit_timeout).
          if (typeof setup === 'function') {
            try { setup({ output: false, explicit_timeout: true }); } catch (e) { /* older harness */ }
          }
          // With explicit_timeout the harness completes only when timeout() is
          // called — so provide our own, but schedule it from INSIDE the load
          // handler so the timer does not exist during the engine's pre-load
          // quiescence pump (which collapses virtual time and would fire it
          // prematurely, marking everything timeout — the very bug we're fixing).
          // Scheduled post-load, it fires only after load-driven tests have had
          // their turn, so a file with one hung async test still reports its
          // completed siblings (pass/fail) with only the stuck ones as timeout.
          if (typeof window !== 'undefined' && window.addEventListener && typeof timeout === 'function') {
            window.addEventListener('load', function () {
              setTimeout(function () { try { timeout(); } catch (e) { /* already complete */ } }, 4000);
            });
          }
          if (typeof add_completion_callback !== 'function') return;
          add_completion_callback(function (tests, status) {
            try {
              var payload = {
                status: status.status,
                message: status.message || null,
                tests: (tests || []).map(function (t) {
                  return { name: String(t.name), status: t.status, message: t.message || null };
                })
              };
              document.documentElement.setAttribute('data-wpt-results', JSON.stringify(payload));
            } catch (e) {
              document.documentElement.setAttribute(
                'data-wpt-results', '{"status":1,"message":"report-error","tests":[]}');
            }
          });
        })();
        """;
}
