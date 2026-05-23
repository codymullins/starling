using AwesomeAssertions;
using SixLabors.ImageSharp;
using Starling.Dom.Events;

namespace Starling.Engine.Tests;

/// <summary>
/// Covers the live-page JS context retained past first paint (<see cref="PageScripting"/>):
/// the interactive load path keeps the realm alive so the shell can dispatch DOM
/// events into page listeners and pump timers/microtasks against wall-clock time.
/// </summary>
[TestClass]
public sealed class LivePageScriptingTests
{
    private static readonly RenderOptions Options = new(new Size(800, 600), 16f);

    [TestMethod]
    public async Task Interactive_load_retains_a_live_scripting_context()
    {
        using var page = await LoadInteractiveAsync(
            "<!doctype html><html><body><input id='q'><script>window.x=1;</script></body></html>");
        page.Scripting.Should().NotBeNull("a page with scripts loaded interactively stays live");
    }

    [TestMethod]
    public async Task Dispatching_an_input_event_runs_the_page_listener()
    {
        const string html = """
            <!doctype html><html><body>
              <input id='q' type='text'>
              <div id='out'>none</div>
              <script>
                var q = document.getElementById('q');
                q.addEventListener('input', function () {
                  document.getElementById('out').textContent = 'sync:' + q.value;
                });
              </script>
            </body></html>
            """;
        using var page = await LoadInteractiveAsync(html);
        var input = page.Document.GetElementById("q")!;
        var output = page.Document.GetElementById("out")!;

        // The shell sets the live value, then dispatches the DOM input event.
        input.InputValue = "hi";
        var mutated = page.Scripting!.DispatchEvent(
            input, new InputEvent("input", new EventInit(Bubbles: true)) { Data = "i", InputType = "insertText" });

        mutated.Should().BeTrue("the listener mutated the DOM");
        output.TextContent.Should().Be("sync:hi");
    }

    [TestMethod]
    public async Task Pumping_runs_a_timer_scheduled_after_load()
    {
        // The setTimeout is scheduled by the event listener — i.e. AFTER load —
        // so only the live post-load pump can fire it (load-time pumping is done).
        const string html = """
            <!doctype html><html><body>
              <input id='q' type='text'>
              <div id='out'>none</div>
              <script>
                document.getElementById('q').addEventListener('input', function () {
                  setTimeout(function () {
                    document.getElementById('out').setAttribute('data-async', 'yes');
                  }, 50);
                });
              </script>
            </body></html>
            """;
        using var page = await LoadInteractiveAsync(html);
        var input = page.Document.GetElementById("q")!;
        var output = page.Document.GetElementById("out")!;

        page.Scripting!.DispatchEvent(input, new InputEvent("input", new EventInit(Bubbles: true)));
        output.GetAttribute("data-async").Should().BeNull("the timer hasn't come due yet");

        // Advance the live clock past the 50ms delay.
        var fired = page.Scripting!.PumpFrame(100);

        fired.Should().BeTrue("pumping past the delay fires the timer and mutates the DOM");
        output.GetAttribute("data-async").Should().Be("yes");
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<LaidOutPage> LoadInteractiveAsync(string html)
    {
        var fixture = Path.Combine(Path.GetTempPath(), $"starling-live-{Guid.NewGuid():N}.html");
        File.WriteAllText(fixture, html);
        var engine = new StarlingEngine();
        // onFirstPaint non-null selects the progressive/interactive path that
        // retains the live realm on the returned page.
        var result = await engine.LayoutPageAsync(
            "file://" + fixture.Replace('\\', '/'), Options, CancellationToken.None, onFirstPaint: _ => { });
        result.IsOk.Should().BeTrue(result.IsErr ? result.Error.Message : "");
        return result.Value;
    }
}
