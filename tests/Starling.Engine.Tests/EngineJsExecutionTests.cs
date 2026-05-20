using System.Net;
using System.Text;
using AwesomeAssertions;
using SixLabors.ImageSharp;
namespace Starling.Engine.Tests;

/// <summary>
/// Engine-level coverage for the JS-execution hook landed alongside the
/// google.com B7 smoke test: page scripts now run between HTML parse and
/// layout, so DOM mutations performed by scripts surface in the rendered
/// <see cref="RenderOutcome.DisplayText"/> and in the painted bitmap.
/// </summary>
[TestClass]
public sealed class EngineJsExecutionTests
{
    private static readonly RenderOptions DefaultOptions =
        new(new Size(800, 600), 16f);

    [TestMethod]
    public async Task Inline_script_mutating_body_text_is_visible_in_display_text()
    {
        // The script replaces a placeholder string after parse. If the engine
        // executes the script before extracting display text the assertion
        // passes; if it skips JS we'd see the original placeholder.
        var html = @"<!doctype html><html><body>
            <p id='out'>placeholder</p>
            <script>document.getElementById('out').textContent = 'after-js';</script>
        </body></html>";

        var outcome = await RenderHtmlAsync(html);
        outcome.DisplayText.Should().Contain("after-js");
        outcome.DisplayText.Should().NotContain("placeholder");
    }

    [TestMethod]
    public async Task Inline_script_appending_a_new_element_is_visible_in_display_text()
    {
        var html = @"<!doctype html><html><body>
            <div id='host'></div>
            <script>
                var d = document.createElement('div');
                d.textContent = 'injected-by-js';
                document.getElementById('host').appendChild(d);
            </script>
        </body></html>";

        var outcome = await RenderHtmlAsync(html);
        outcome.DisplayText.Should().Contain("injected-by-js");
    }

    [TestMethod]
    public async Task DOMContentLoaded_listener_fires_during_engine_render()
    {
        // Listener attached during initial script run mutates the DOM when
        // DOMContentLoaded dispatches — the engine fires DOMContentLoaded
        // before draining microtasks, so the mutation lands ahead of paint.
        var html = @"<!doctype html><html><body>
            <p id='out'>still-loading</p>
            <script>
                document.addEventListener('DOMContentLoaded', function() {
                    document.getElementById('out').textContent = 'dom-ready';
                });
            </script>
        </body></html>";

        var outcome = await RenderHtmlAsync(html);
        outcome.DisplayText.Should().Contain("dom-ready");
        outcome.DisplayText.Should().NotContain("still-loading");
    }

    [TestMethod]
    public async Task Fetch_completion_landing_during_microtask_drain_is_visible_in_display_text()
    {
        // The engine's PumpPendingAsync re-enters the VM after off-thread
        // fetch completions enqueue resolve jobs. This pins that the pump
        // settles before display-text extraction.
        await using var server = await JsonResultsServer.StartAsync(
            "[\"alpha-result\",\"beta-result\",\"gamma-result\"]");

        var html = $@"<!doctype html><html><body>
            <div id='results'></div>
            <script>
                fetch('{server.BaseUrl}/api/results')
                  .then(function(r) {{ return r.json(); }})
                  .then(function(items) {{
                      var host = document.getElementById('results');
                      items.forEach(function(t) {{
                          var d = document.createElement('div');
                          d.textContent = t;
                          host.appendChild(d);
                      }});
                  }});
            </script>
        </body></html>";

        var outcome = await RenderHtmlAsync(html);
        outcome.DisplayText.Should().Contain("alpha-result");
        outcome.DisplayText.Should().Contain("beta-result");
        outcome.DisplayText.Should().Contain("gamma-result");
    }

    [TestMethod]
    public async Task Script_with_throw_does_not_abort_render()
    {
        // First script throws; second script still runs and its mutation is
        // observed. The engine routes JsThrow through diagnostics rather
        // than failing the render.
        var html = @"<!doctype html><html><body>
            <p id='out'>before</p>
            <script>throw new Error('boom from script #1');</script>
            <script>document.getElementById('out').textContent = 'after-recovery';</script>
        </body></html>";

        var outcome = await RenderHtmlAsync(html);
        outcome.DisplayText.Should().Contain("after-recovery");
    }

    [TestMethod]
    public async Task setTimeout_zero_callback_is_visible_in_display_text()
    {
        // Bundlers commonly defer init via setTimeout(fn, 0). PumpPendingAsync
        // advances the simulated event-loop clock so the chained callback
        // fires before display-text extraction.
        var html = @"<!doctype html><html><body>
            <p id='out'>before-timeout</p>
            <script>
                setTimeout(function() {
                    document.getElementById('out').textContent = 'after-timeout';
                }, 0);
            </script>
        </body></html>";

        var outcome = await RenderHtmlAsync(html);
        outcome.DisplayText.Should().Contain("after-timeout");
        outcome.DisplayText.Should().NotContain("before-timeout");
    }

    [TestMethod]
    public async Task Chained_setTimeouts_with_delays_all_settle_within_budget()
    {
        // Three nested setTimeouts each with a non-zero delay — only land
        // if PumpPendingAsync advances simulated time across multiple steps.
        var html = @"<!doctype html><html><body>
            <p id='out'>0</p>
            <script>
                function tick(remaining) {
                    document.getElementById('out').textContent = String(3 - remaining);
                    if (remaining > 0) setTimeout(function() { tick(remaining - 1); }, 25);
                }
                setTimeout(function() { tick(3); }, 25);
            </script>
        </body></html>";

        var outcome = await RenderHtmlAsync(html);
        outcome.DisplayText.Should().Contain("3");
    }

    [TestMethod]
    public async Task requestAnimationFrame_callback_runs_during_render()
    {
        // Pages that bootstrap via rAF (instead of setTimeout) must settle
        // during PumpPendingAsync — AnimationFrameBinding is installed and
        // AdvanceBy routes through RunFrame so the rAF queue drains.
        var html = @"<!doctype html><html><body>
            <p id='out'>before-raf</p>
            <script>
                requestAnimationFrame(function() {
                    document.getElementById('out').textContent = 'after-raf';
                });
            </script>
        </body></html>";

        var outcome = await RenderHtmlAsync(html);
        outcome.DisplayText.Should().Contain("after-raf");
        outcome.DisplayText.Should().NotContain("before-raf");
    }

    [TestMethod]
    public async Task cancelAnimationFrame_prevents_callback_during_render()
    {
        var html = @"<!doctype html><html><body>
            <p id='out'>start</p>
            <script>
                var id = requestAnimationFrame(function() {
                    document.getElementById('out').textContent = 'should-not-run';
                });
                cancelAnimationFrame(id);
                requestAnimationFrame(function() {
                    document.getElementById('out').textContent = 'sibling-ran';
                });
            </script>
        </body></html>";

        var outcome = await RenderHtmlAsync(html);
        outcome.DisplayText.Should().Contain("sibling-ran");
        outcome.DisplayText.Should().NotContain("should-not-run");
    }

    [TestMethod]
    public async Task Chained_requestAnimationFrame_loop_settles_within_budget()
    {
        // A rAF chain — like a fade-in driven by a counter — must tick
        // multiple frames during PumpPendingAsync. Each nested rAF lands
        // in the next frame's queue.
        var html = @"<!doctype html><html><body>
            <p id='out'>0</p>
            <script>
                function tick(remaining) {
                    document.getElementById('out').textContent = String(3 - remaining);
                    if (remaining > 0) requestAnimationFrame(function() { tick(remaining - 1); });
                }
                requestAnimationFrame(function() { tick(3); });
            </script>
        </body></html>";

        var outcome = await RenderHtmlAsync(html);
        outcome.DisplayText.Should().Contain("3");
    }

    [TestMethod]
    public async Task getBoundingClientRect_returns_nonzero_dimensions_for_styled_box()
    {
        // The engine pre-lays-out the document before running scripts, so
        // getBoundingClientRect should report the styled width/height of
        // the target div (not 0/0 like a never-laid-out doc would).
        var html = @"<!doctype html><html>
          <head><style>#probe { display:block; width:140px; height:60px; background:#abc; }</style></head>
          <body>
            <div id='probe'>probe</div>
            <p id='out'>?</p>
            <script>
                var r = document.getElementById('probe').getBoundingClientRect();
                document.getElementById('out').textContent =
                    'w=' + Math.round(r.width) + ' h=' + Math.round(r.height);
            </script>
          </body></html>";

        var outcome = await RenderHtmlAsync(html);
        outcome.DisplayText.Should().Contain("w=140");
        outcome.DisplayText.Should().Contain("h=60");
    }

    [TestMethod]
    public async Task offsetWidth_and_offsetHeight_track_layout()
    {
        var html = @"<!doctype html><html>
          <head><style>#probe { display:block; width:80px; height:30px; }</style></head>
          <body>
            <div id='probe'></div>
            <p id='out'>?</p>
            <script>
                var probe = document.getElementById('probe');
                document.getElementById('out').textContent =
                    'ow=' + probe.offsetWidth + ' oh=' + probe.offsetHeight;
            </script>
          </body></html>";

        var outcome = await RenderHtmlAsync(html);
        outcome.DisplayText.Should().Contain("ow=80");
        outcome.DisplayText.Should().Contain("oh=30");
    }

    [TestMethod]
    public async Task getComputedStyle_returns_resolved_css_property_value()
    {
        // The cascade snapshot the engine builds before scripts run feeds
        // getComputedStyle. The kebab-case getter and the camelCase
        // accessor should both report the styled value.
        var html = @"<!doctype html><html>
          <head><style>#probe { color:#ff0000; font-size:24px; display:block; }</style></head>
          <body>
            <div id='probe'>probe</div>
            <p id='out'>?</p>
            <script>
                var cs = window.getComputedStyle(document.getElementById('probe'));
                document.getElementById('out').textContent =
                    'd=' + cs.getPropertyValue('display') +
                    ' fs=' + cs.fontSize;
            </script>
          </body></html>";

        var outcome = await RenderHtmlAsync(html);
        outcome.DisplayText.Should().Contain("d=block");
        outcome.DisplayText.Should().Contain("fs=24px");
    }

    [TestMethod]
    public async Task getBoundingClientRect_reflects_element_appended_in_same_script_run()
    {
        // Live re-layout: a script appends a sized element, then immediately
        // reads its geometry. The read must trigger a lazy re-layout and
        // report the new element's box, not the pre-script snapshot (which had
        // no such element and would yield 0/0).
        var html = @"<!doctype html><html>
          <head><style>.sized { display:block; width:120px; height:40px; }</style></head>
          <body>
            <p id='out'>?</p>
            <script>
                var d = document.createElement('div');
                d.id = 'fresh';
                d.className = 'sized';
                document.body.appendChild(d);
                var r = document.getElementById('fresh').getBoundingClientRect();
                document.getElementById('out').textContent =
                    'w=' + Math.round(r.width) + ' h=' + Math.round(r.height);
            </script>
          </body></html>";

        var outcome = await RenderHtmlAsync(html);
        outcome.DisplayText.Should().Contain("w=120");
        outcome.DisplayText.Should().Contain("h=40");
    }

    [TestMethod]
    public async Task offsetTop_of_following_sibling_reflects_content_inserted_before_it()
    {
        // Live re-layout: inserting a sized block before an existing element
        // pushes that element down. Reading its offsetTop after the mutation
        // must reflect the new layout, not the pre-script position.
        var html = @"<!doctype html><html>
          <head><style>.spacer { display:block; height:50px; } #anchor { display:block; }</style></head>
          <body>
            <div id='anchor'>anchor</div>
            <p id='out'>?</p>
            <script>
                var anchor = document.getElementById('anchor');
                var before = anchor.offsetTop;
                var s = document.createElement('div');
                s.className = 'spacer';
                document.body.insertBefore(s, anchor);
                var after = anchor.offsetTop;
                document.getElementById('out').textContent =
                    'before=' + Math.round(before) + ' after=' + Math.round(after);
            </script>
          </body></html>";

        var outcome = await RenderHtmlAsync(html);
        // The spacer is 50px tall, so the anchor moves down by 50px after the
        // insertion. The exact "before" depends on body margins; the load-
        // bearing assertion is that "after" is 50px greater than "before".
        var match = System.Text.RegularExpressions.Regex.Match(
            outcome.DisplayText, @"before=(\d+) after=(\d+)");
        match.Success.Should().BeTrue($"display text was '{outcome.DisplayText}'");
        var before = int.Parse(match.Groups[1].Value);
        var after = int.Parse(match.Groups[2].Value);
        (after - before).Should().Be(50);
    }

    [TestMethod]
    public async Task Module_and_unknown_type_scripts_are_skipped()
    {
        // type="module" is not yet supported; a stray module script must not
        // crash the pipeline and the classic sibling must still run.
        var html = @"<!doctype html><html><body>
            <p id='out'>start</p>
            <script type='module'>document.getElementById('out').textContent = 'module-ran';</script>
            <script type='application/ld+json'>{ ""@context"": ""bogus"" }</script>
            <script>document.getElementById('out').textContent = 'classic-ran';</script>
        </body></html>";

        var outcome = await RenderHtmlAsync(html);
        outcome.DisplayText.Should().Contain("classic-ran");
        outcome.DisplayText.Should().NotContain("module-ran");
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private static async Task<RenderOutcome> RenderHtmlAsync(string html)
    {
        var tempHtml = Path.Combine(Path.GetTempPath(), $"starling-js-{Guid.NewGuid():N}.html");
        var tempPng = Path.Combine(Path.GetTempPath(), $"starling-js-{Guid.NewGuid():N}.png");
        await File.WriteAllTextAsync(tempHtml, html, CancellationToken.None);
        try
        {
            var engine = new StarlingEngine();
            var url = new Uri(tempHtml).AbsoluteUri;
            var result = await engine.RenderAsync(url, DefaultOptions, tempPng,
                CancellationToken.None);
            result.IsOk.Should().BeTrue(result.IsErr ? result.Error.Message : "");
            return result.Value;
        }
        finally
        {
            TryDelete(tempHtml);
            TryDelete(tempPng);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }

    /// <summary>HttpListener-backed JSON endpoint shared by the fetch test.</summary>
    private sealed class JsonResultsServer : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;
        public string BaseUrl { get; }

        private JsonResultsServer(HttpListener listener, string baseUrl, string json)
        {
            _listener = listener;
            BaseUrl = baseUrl;
            _loop = Task.Run(() => LoopAsync(json));
        }

        public static Task<JsonResultsServer> StartAsync(string json)
        {
            var prefix = $"http://127.0.0.1:{FindFreePort()}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            listener.Start();
            return Task.FromResult(new JsonResultsServer(listener, prefix.TrimEnd('/'), json));
        }

        private async Task LoopAsync(string json)
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
                catch { return; }
                _ = Task.Run(() =>
                {
                    try
                    {
                        ctx.Response.StatusCode = 200;
                        ctx.Response.ContentType = "application/json";
                        var bytes = Encoding.UTF8.GetBytes(json);
                        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                    }
                    catch { /* swallow */ }
                    finally
                    {
                        try { ctx.Response.OutputStream.Close(); } catch { }
                        try { ctx.Response.Close(); } catch { }
                    }
                });
            }
        }

        private static int FindFreePort()
        {
            var sock = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            sock.Start();
            var port = ((IPEndPoint)sock.LocalEndpoint).Port;
            sock.Stop();
            return port;
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
            try { _listener.Close(); } catch { }
            try { await _loop.ConfigureAwait(false); } catch { }
        }
    }
}
