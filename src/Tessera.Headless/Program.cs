using SixLabors.ImageSharp;
using Tessera.Common.Diagnostics;
using Tessera.Engine;
using Tessera.Html.Tokenizer;
using Tessera.Telemetry;

namespace Tessera.Headless;

/// <summary>
/// Agent-friendly CLI per browser-plan/02_PROJECT_SETUP.md §Headless CLI shape.
///
/// Static rendering and tokenizer inspection are implemented; <c>parse</c>,
/// <c>style</c>, <c>layout</c>, and <c>js</c> still print a "not yet" message
/// and return exit code 2 (per Unix convention: misuse of a builtin / not-yet).
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        // Wire OTel before we do anything observable. When launched by Aspire
        // (`dotnet run --project Tessera.AppHost`), OTEL_EXPORTER_OTLP_ENDPOINT
        // is set and traces/metrics/logs flow to the Aspire dashboard. When
        // run directly, the providers are still wired but the exporter is a
        // no-op.
        using var telemetry = OtelBootstrap.Initialize("tessera-headless");

        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        var sub = args[0];
        var rest = args[1..];

        return sub switch
        {
            "render" => Render(rest),
            "tokenize" => Tokenize(rest),
            "parse" or "style" or "layout" or "js"
                => StubSubcommand(sub),
            "-h" or "--help" or "help" => UsageOk(),
            _ => UnknownSubcommand(sub),
        };
    }

    /// <summary>
    /// Dumps the WHATWG HTML tokenizer's output for the given file. Useful as
    /// a debugging tool and as a demo of M1-01a–c work. Subsequent agents
    /// (M1-01d–g) extend coverage as new states land.
    /// </summary>
    private static int Tokenize(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("error: `tokenize` requires a file path.");
            Console.Error.WriteLine("usage: tessera tokenize <file>");
            return 1;
        }

        var path = args[0];
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"error: cannot read '{path}': {ex.Message}");
            return 1;
        }

        var sink = new ConsoleParseErrorSink();
        var t = new HtmlTokenizer(sink);
        t.Feed(text);
        t.EndOfInput();

        Console.WriteLine($"tokenizing {path} ({text.Length} bytes)");
        Console.WriteLine(new string('-', 60));

        var n = 0;
        while (true)
        {
            HtmlToken? tok;
            try { tok = t.ReadToken(); }
            catch (NotImplementedException ex)
            {
                Console.WriteLine($"… tokenizer reached an unimplemented state: {ex.Message}");
                Console.WriteLine($"   (this file exercises tokenizer states the open M1-01* sub-tasks own)");
                break;
            }
            if (tok is null) break;
            Console.WriteLine($"{++n,4}: {FormatToken(tok)}");
            if (tok is EndOfFileToken) break;
        }

        if (sink.Count > 0)
        {
            Console.WriteLine(new string('-', 60));
            Console.WriteLine($"{sink.Count} parse error(s) reported.");
        }
        return 0;
    }

    private static string FormatToken(HtmlToken tok) => tok switch
    {
        CharacterToken { CodePoint: var cp } => cp switch
        {
            '\n' => "Character('\\n')",
            '\r' => "Character('\\r')",
            '\t' => "Character('\\t')",
            < 32 or 0x7F => $"Character(U+{cp:X4})",
            _ => $"Character('{(char)cp}')",
        },
        StartTagToken st => $"StartTag <{st.Name}{FormatAttrs(st.Attributes)}{(st.SelfClosing ? "/" : "")}>",
        EndTagToken et => $"EndTag </{et.Name}>",
        CommentToken c => $"Comment(\"{c.Data.Replace("\\", "\\\\").Replace("\"", "\\\"")}\")",
        DoctypeToken d => $"Doctype(name={d.Name ?? "(null)"})",
        EndOfFileToken => "EOF",
        _ => tok.ToString() ?? "(unknown)",
    };

    private static string FormatAttrs(IReadOnlyList<HtmlAttribute> attrs)
    {
        if (attrs.Count == 0) return "";
        var parts = attrs.Select(a => $" {a.Name}=\"{a.Value}\"");
        return string.Concat(parts);
    }

    /// <summary>Parse-error sink that prints to stderr. Useful in the CLI.</summary>
    private sealed class ConsoleParseErrorSink : IParseErrorSink
    {
        public int Count { get; private set; }

        public void Report(HtmlParseError code, int line, int column)
        {
            Count++;
            Console.Error.WriteLine($"  parse error at {line}:{column} — {code}");
        }
    }

    private static int Render(string[] args)
    {
        // tessera render <url-or-file> [-o out.png] [--viewport WxH]
        if (args.Length == 0)
        {
            Console.Error.WriteLine("error: `render` requires a URL or file path.");
            Console.Error.WriteLine("usage: tessera render <url-or-file> [-o out.png] [--viewport WxH] [--font-size N]");
            return 1;
        }

        var input = args[0];
        var output = "out.png";
        var viewport = new Size(800, 600);
        var fontSize = 32f;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-o" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                case "--viewport" when i + 1 < args.Length:
                    if (!TryParseViewport(args[++i], out viewport))
                    {
                        Console.Error.WriteLine($"error: invalid --viewport '{args[i]}'. Use WxH, e.g. 1024x768.");
                        return 1;
                    }
                    break;
                case "--font-size" when i + 1 < args.Length:
                    if (!float.TryParse(args[++i], System.Globalization.CultureInfo.InvariantCulture, out fontSize)
                        || fontSize <= 0)
                    {
                        Console.Error.WriteLine($"error: invalid --font-size '{args[i]}'.");
                        return 1;
                    }
                    break;
                default:
                    Console.Error.WriteLine($"error: unknown render option '{args[i]}'.");
                    return 1;
            }
        }

        // Allow bare paths in addition to file:// URLs — agent ergonomics.
        var url = NormalizeUrlOrPath(input);

        var engine = new TesseraEngine(diagnostics: new ConsoleDiagnostics());
        var result = engine.Render(url, new RenderOptions(viewport, fontSize), output);

        return result.Match(
            ok =>
            {
                Console.WriteLine($"rendered {ok.OutputPath} ({ok.Width}x{ok.Height})");
                return 0;
            },
            err =>
            {
                Console.Error.WriteLine($"error: {err.Message}");
                return 1;
            });
    }

    private static int StubSubcommand(string name)
    {
        Console.Error.WriteLine(
            $"`tessera {name}` is not yet implemented. See browser-plan/13_MILESTONES.md.");
        return 2;
    }

    private static int UnknownSubcommand(string sub)
    {
        Console.Error.WriteLine($"error: unknown subcommand '{sub}'.");
        PrintUsage();
        return 1;
    }

    private static int UsageOk()
    {
        PrintUsage();
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            "tessera — headless browser CLI\n" +
            "\n" +
            "usage:\n" +
            "  tessera render <url-or-file> [-o out.png] [--viewport WxH] [--font-size N]\n" +
            "  tessera tokenize <file>      (M1; partial — Data/tag/RCDATA/RAWTEXT/PLAINTEXT)\n" +
            "  tessera parse    <file>      (M1)\n" +
            "  tessera style    <file>      (M1)\n" +
            "  tessera layout   <file>      (M1)\n" +
            "  tessera js       <file>      (M3)\n");
    }

    private static bool TryParseViewport(string s, out Size size)
    {
        size = default;
        var x = s.IndexOf('x', StringComparison.OrdinalIgnoreCase);
        if (x <= 0 || x == s.Length - 1) return false;
        if (!int.TryParse(s[..x], out var w) || !int.TryParse(s[(x + 1)..], out var h)) return false;
        if (w <= 0 || h <= 0) return false;
        size = new Size(w, h);
        return true;
    }

    private static string NormalizeUrlOrPath(string input)
    {
        if (input.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) return input;
        if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return input;
        if (input.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return input;
        // Bare path → file://
        var full = Path.GetFullPath(input);
        return "file://" + (full.StartsWith('/') ? full : "/" + full.Replace('\\', '/'));
    }
}
