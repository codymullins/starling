using AwesomeAssertions;
using SixLabors.ImageSharp;
using Starling.Common.Diagnostics;

namespace Starling.Engine.Tests;

/// <summary>
/// A page's <c>console.*</c> level must survive into the engine's diagnostics so
/// DevTools' ConsolePanel can colour errors and have its level-filter pills
/// match. The engine maps each <c>ConsoleLevel</c> to the matching
/// <see cref="DiagLevel"/> (error→Error, warn→Warn, debug/trace→Debug, the rest
/// →Info) and logs under the "engine.js" area.
/// </summary>
[TestClass]
public sealed class EngineConsoleLevelTests
{
    [TestMethod]
    public async Task Console_levels_map_to_matching_diag_levels()
    {
        var html = """
            <!doctype html><html><body><script>
              console.log('a log');
              console.info('an info');
              console.warn('a warn');
              console.error('an error');
              console.debug('a debug');
              console.trace('a trace');
            </script></body></html>
            """;

        var diag = await RenderAsync(html);

        DiagLevel LevelOf(string needle) =>
            diag.JsLogs.Single(l => l.Message.Contains(needle, StringComparison.Ordinal)).Level;

        LevelOf("a log").Should().Be(DiagLevel.Info);
        LevelOf("an info").Should().Be(DiagLevel.Info);
        LevelOf("a warn").Should().Be(DiagLevel.Warn);
        LevelOf("an error").Should().Be(DiagLevel.Error);
        LevelOf("a debug").Should().Be(DiagLevel.Debug);
        LevelOf("a trace").Should().Be(DiagLevel.Debug);
    }

    private static async Task<ConsoleRecordingDiagnostics> RenderAsync(string html)
    {
        var tempHtml = Path.Combine(Path.GetTempPath(), $"starling-console-{Guid.NewGuid():N}.html");
        var tempPng = Path.Combine(Path.GetTempPath(), $"starling-console-{Guid.NewGuid():N}.png");
        await File.WriteAllTextAsync(tempHtml, html, CancellationToken.None);
        var diag = new ConsoleRecordingDiagnostics();
        try
        {
            var engine = new StarlingEngine(diagnostics: diag);
            var url = new Uri(tempHtml).AbsoluteUri;
            var result = await engine.RenderAsync(
                url, new RenderOptions(new Size(800, 600), 16f), tempPng, CancellationToken.None);
            result.IsOk.Should().BeTrue(result.IsErr ? result.Error.Message : "");
            return diag;
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

    /// <summary>Captures every <c>_diag.Log</c> call made under the "engine.js" area.</summary>
    private sealed class ConsoleRecordingDiagnostics : IDiagnostics
    {
        private readonly List<(DiagLevel Level, string Message)> _jsLogs = new();

        public IReadOnlyList<(DiagLevel Level, string Message)> JsLogs
        {
            get { lock (_jsLogs) return _jsLogs.ToList(); }
        }

        public void Log(DiagLevel level, string area, string message)
        {
            if (area == "engine.js")
                lock (_jsLogs) _jsLogs.Add((level, message));
        }

        public IDisposable Span(string area, string operation) => NoopSpan.Instance;
        public void Counter(string name, double value) { }
        public void Snapshot(string label, ReadOnlySpan<byte> bytes) { }
        public void LogException(string area, Exception exception, string? message = null) { }

        private sealed class NoopSpan : IDisposable
        {
            public static readonly NoopSpan Instance = new();
            public void Dispose() { }
        }
    }
}
