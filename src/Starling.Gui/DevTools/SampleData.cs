using Tessera.Gui.Theme;

namespace Tessera.Gui.DevTools;

// ─── Models ────────────────────────────────────────────────────────────────

public enum LogLevel { Error, Warn, Info, Log, Debug }

/// <summary>One structured Console row — mirrors a <c>LOGS</c> entry in devtools.jsx.</summary>
public sealed record LogEntry(
    string Time, LogLevel Level, string Source, Category Cat, string Message,
    string? Tag = null, bool IsObject = false);

public sealed record PerfFrame(double T, double D, int Fps, bool Jank = false);
public sealed record PerfMarker(double T, string Label, string Hint);
public sealed record PerfThread(string Name, IReadOnlyList<IReadOnlyList<TimingBar>> Rows);

/// <summary>The Performance recording — mirrors the <c>PERF</c> object in devtools.jsx.</summary>
public sealed record PerfSample(
    double TotalMs,
    IReadOnlyList<PerfThread> Threads,
    IReadOnlyList<PerfFrame> Frames,
    IReadOnlyList<PerfMarker> Markers);

public enum ParseState { Parsed, Parsing, Fetching, Queued }
public sealed record ParserNode(
    int Depth, string Tag, ParseState State, string? Text = null, string? Resource = null);

public sealed record StackFrame(string Function, string Source, bool IsTop = false, bool Exception = false);
public sealed record HeapSegment(string Label, double Fraction, double Kb, Category Cat);
public sealed record GcEvent(double Kb, bool Major);
public sealed record IpcChannel(string From, string To, int Msgs, Category Cat);

// ─── Sample data ───────────────────────────────────────────────────────────

/// <summary>
/// Static mock data for the DevTools panels — the C# port of the <c>PERF</c>,
/// <c>LOGS</c> and per-card arrays in <c>design/devtools.jsx</c>. When real
/// engine instrumentation lands it replaces this class, not the panels.
///
/// Every category is a real <see cref="Category"/> value — the devtools.jsx
/// <c>'ipc'</c> / <c>'console'</c> / <c>'boot'</c> strings (which had no palette
/// entry) are mapped to their nearest real category per HANDOFF §2.4.
/// </summary>
public static class SampleData
{
    private static TimingBar Bar(double t, double d, Category cat, string label)
        => new(t, d, cat, label);

    public static readonly PerfSample Perf = new(
        TotalMs: 612,
        Threads: new[]
        {
            new PerfThread("Main", new[]
            {
                new[]
                {
                    Bar(0, 6, Category.Net, "send req"),
                    Bar(6, 18, Category.Net, "TLS handshake"),
                    Bar(24, 12, Category.Net, "header read"),
                    Bar(36, 82, Category.Html, "parse HTML"),
                    Bar(118, 14, Category.Css, "parse css"),
                    Bar(132, 38, Category.Js, "eval app.js"),
                    Bar(170, 4, Category.Gc, "minor GC"),
                    Bar(174, 46, Category.Css, "recalc style"),
                    Bar(220, 64, Category.Layout, "layout flow"),
                    Bar(284, 28, Category.Paint, "paint"),
                    Bar(312, 14, Category.Paint, "composite"),
                    Bar(380, 22, Category.Js, "rAF cb"),
                    Bar(402, 8, Category.Css, "recalc"),
                    Bar(410, 12, Category.Layout, "incr layout"),
                    Bar(422, 18, Category.Paint, "paint"),
                    Bar(440, 8, Category.Paint, "composite"),
                    Bar(500, 4, Category.Js, "timer"),
                    Bar(504, 26, Category.Gc, "major GC · 4.2 MB"),
                    Bar(530, 32, Category.Layout, "layout"),
                    Bar(562, 20, Category.Paint, "paint"),
                    Bar(582, 12, Category.Paint, "composite"),
                },
                new[]
                {
                    Bar(132, 12, Category.Js, "init()"),
                    Bar(144, 18, Category.Js, "hydrate(root)"),
                    Bar(162, 8, Category.Js, "queueMicrotask"),
                    Bar(380, 14, Category.Js, "render()"),
                    Bar(394, 8, Category.Js, "diff()"),
                },
                new[]
                {
                    Bar(146, 6, Category.Js, "build tree"),
                    Bar(152, 10, Category.Js, "attach"),
                    Bar(382, 10, Category.Js, "reconcile"),
                },
            }),
            new PerfThread("Loader", new[]
            {
                new[]
                {
                    Bar(0, 24, Category.Net, "DNS · words.html"),
                    Bar(24, 36, Category.Net, "TLS"),
                    Bar(60, 58, Category.Net, "GET words.html"),
                    Bar(118, 38, Category.Net, "GET style.css"),
                    Bar(118, 96, Category.Net, "GET app.js"),
                    Bar(214, 142, Category.Net, "GET hero.webp"),
                    Bar(360, 22, Category.Net, "GET font.woff2"),
                },
            }),
            new PerfThread("Compositor", new[]
            {
                new[]
                {
                    Bar(312, 14, Category.Paint, "first paint"),
                    Bar(440, 8, Category.Paint, "commit"),
                    Bar(582, 12, Category.Paint, "commit"),
                },
            }),
        },
        Frames: new[]
        {
            new PerfFrame(0, 326, 60),
            new PerfFrame(326, 122, 60),
            new PerfFrame(448, 164, 47, Jank: true),
        },
        Markers: new[]
        {
            new PerfMarker(36, "FB", "first byte"),
            new PerfMarker(312, "FCP", "first contentful paint"),
            new PerfMarker(448, "LCP", "largest contentful paint"),
            new PerfMarker(600, "TTI", "time to interactive"),
        });

    public static readonly IReadOnlyList<LogEntry> Logs = new[]
    {
        new LogEntry("00:00.012", LogLevel.Info, "starling", Category.Idle, "engine ready · M3 (flow-layout, async-loader, ipc-sandbox)"),
        new LogEntry("00:00.024", LogLevel.Info, "loader", Category.Net, "GET justinjackson.ca/words.html", "200 · 4.2kB · 318ms"),
        new LogEntry("00:00.036", LogLevel.Info, "parser", Category.Html, "tokens=412 nodes=87 errors=0"),
        new LogEntry("00:00.118", LogLevel.Warn, "parser", Category.Html, "unmatched <em> at line 18 · auto-closed"),
        new LogEntry("00:00.132", LogLevel.Log, "page", Category.Js, "[app] booted in 4.2ms"),
        new LogEntry("00:00.146", LogLevel.Log, "page", Category.Js, "{ user: { id: 4082, plan: 'free' }, flags: ['ab.cta-v2', 'metrics'] }", IsObject: true),
        new LogEntry("00:00.170", LogLevel.Debug, "gc", Category.Gc, "minor · 1.8MB → 1.2MB · 4.1ms"),
        new LogEntry("00:00.220", LogLevel.Info, "layout", Category.Layout, "flow pass · 87 nodes · 64.1ms"),
        new LogEntry("00:00.221", LogLevel.Warn, "layout", Category.Layout, "forced reflow from app.js:42 — read offsetHeight inside RAF"),
        new LogEntry("00:00.284", LogLevel.Info, "paint", Category.Paint, "first paint · 28.0ms · 4 layers"),
        new LogEntry("00:00.382", LogLevel.Log, "page", Category.Js, "fetch(\"/api/me\") → 200", "34ms"),
        new LogEntry("00:00.504", LogLevel.Error, "js", Category.Js, "TypeError: Cannot read property 'tag' of undefined"),
        new LogEntry("00:00.504", LogLevel.Error, "js", Category.Js, "    at Hero.render (app.js:142:18)"),
        new LogEntry("00:00.530", LogLevel.Info, "ipc", Category.Net, "WebContent → UI · paint-ack #218 · 0.4ms"),
    };

    public static readonly IReadOnlyList<ParserNode> ParserTree = new[]
    {
        new ParserNode(0, "html", ParseState.Parsed),
        new ParserNode(1, "head", ParseState.Parsed),
        new ParserNode(2, "title", ParseState.Parsed, Text: "Words"),
        new ParserNode(2, "link rel=stylesheet", ParseState.Fetching, Resource: "style.css"),
        new ParserNode(1, "body", ParseState.Parsing),
        new ParserNode(2, "article", ParseState.Parsing),
        new ParserNode(3, "h1", ParseState.Parsed, Text: "This."),
        new ParserNode(3, "p", ParseState.Parsed, Text: "This is your website."),
        new ParserNode(3, "p", ParseState.Queued),
        new ParserNode(3, "p", ParseState.Queued),
    };

    public static readonly IReadOnlyList<StackFrame> CallStack = new[]
    {
        new StackFrame("Hero.render", "app.js:142", IsTop: true, Exception: true),
        new StackFrame("App.render", "app.js:88"),
        new StackFrame("hydrate", "react.js:1284"),
        new StackFrame("<rAF>", "libstarling/event"),
    };

    public static readonly IReadOnlyList<HeapSegment> Heap = new[]
    {
        new HeapSegment("JS objects", 0.38, 6.2, Category.Js),
        new HeapSegment("strings", 0.24, 3.9, Category.Css),
        new HeapSegment("DOM", 0.14, 2.3, Category.Html),
        new HeapSegment("buffers", 0.10, 1.6, Category.Net),
    };

    public static readonly IReadOnlyList<GcEvent> GcEvents = new[]
    {
        new GcEvent(120, false),
        new GcEvent(240, false),
        new GcEvent(80, false),
        new GcEvent(1200, true),
        new GcEvent(180, false),
        new GcEvent(200, false),
        new GcEvent(4200, true),
    };

    public static readonly IReadOnlyList<IpcChannel> IpcChannels = new[]
    {
        new IpcChannel("WebContent", "UI", 218, Category.Paint),
        new IpcChannel("UI", "WebContent", 47, Category.Js),
        new IpcChannel("Loader", "WebContent", 12, Category.Net),
        new IpcChannel("WebContent", "Sandbox", 4, Category.Gc),
    };

    /// <summary>The URL-bar mini-load-chart phases — mirrors <c>app.jsx</c>'s
    /// FrameB phases, now <em>with</em> labels so the tooltip / a11y text reads
    /// (punch-list item 4).</summary>
    public static readonly IReadOnlyList<TimingBar> LoadPhases = new[]
    {
        new TimingBar(0, 24, Category.Net, "DNS"),
        new TimingBar(24, 36, Category.Net, "TLS"),
        new TimingBar(60, 58, Category.Net, "GET html"),
        new TimingBar(118, 82, Category.Html, "parse"),
        new TimingBar(200, 38, Category.Js, "script"),
        new TimingBar(238, 46, Category.Css, "style"),
        new TimingBar(284, 64, Category.Layout, "layout"),
        new TimingBar(348, 28, Category.Paint, "paint"),
    };

    public const double LoadTotalMs = 376;
}
