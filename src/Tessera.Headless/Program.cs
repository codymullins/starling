using SixLabors.ImageSharp;
using Tessera.Common.Diagnostics;
using Tessera.Engine;

namespace Tessera.Headless;

/// <summary>
/// Agent-friendly CLI per browser-plan/02_PROJECT_SETUP.md §Headless CLI shape.
///
/// M0 implements <c>render</c> fully; <c>tokenize</c>, <c>parse</c>, <c>style</c>,
/// <c>layout</c>, and <c>js</c> are stubs that print a "not yet" message and
/// return exit code 2 (per Unix convention: misuse of a builtin / not-yet).
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
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
            "tokenize" or "parse" or "style" or "layout" or "js"
                => StubSubcommand(sub),
            "-h" or "--help" or "help" => UsageOk(),
            _ => UnknownSubcommand(sub),
        };
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
            $"`tessera {name}` is not yet implemented in M0. See browser-plan/13_MILESTONES.md.");
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
            "  tessera tokenize <file>      (M1)\n" +
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
