using System.Text;
using BenchmarkDotNet.Attributes;
using Starling.Html;
using Starling.Layout;
using Starling.Paint;

namespace Starling.Bench;

// Rasterizes a text-heavy, article-class page end to end through the public
// Painter on the pure-CPU ImageSharp backend. Most bench classes stop at the
// display list because raster timings are host-dependent, but the raster phase
// — specifically per-glyph re-tessellation in raster.replay_items — is the
// single biggest leaf cost on text pages, so this bench exists to make that
// path measurable and to lock in the glyph-outline-cache optimization.
//
// The CPU backend is selected explicitly so the bench is deterministic and
// never touches a GPU (which may be absent or sandboxed on a CI runner).
[MemoryDiagnoser]
public class TextRasterBench
{
    private Starling.Dom.Document _doc = null!;
    private static readonly Size Viewport = new(1280, 900);
    private Painter _painter = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Force the pure-managed CPU rasterizer; the WebGPU default may be
        // unavailable on a headless/sandboxed bench host. PaintBackendSelector
        // reads this once, lazily, so it must be set before the first Render.
        Environment.SetEnvironmentVariable("STARLING_PAINT_BACKEND", "imagesharp");
        _doc = HtmlParser.Parse(BuildTextHeavyHtml());
        _painter = new Painter();
    }

    [Benchmark]
    public int Raster_TextHeavyArticle()
    {
        using var bitmap = _painter.RenderDocument(_doc, Viewport);
        return bitmap.Width;
    }

    // Builds a deterministic ~120-paragraph article: thousands of word
    // fragments drawn from a small vocabulary, so the page has the high
    // token-repetition profile (≈98% repeats) that real prose has — exactly the
    // shape the glyph-outline cache targets.
    private static string BuildTextHeavyHtml()
    {
        string[] words =
        [
            "the", "quick", "brown", "fox", "jumps", "over", "lazy", "dog", "and",
            "then", "runs", "through", "a", "field", "of", "green", "grass", "while",
            "the", "sun", "shines", "brightly", "above", "casting", "shadows", "on",
            "the", "ground", "where", "children", "play", "games", "and", "laugh",
        ];

        var rng = new Random(42);
        var sb = new StringBuilder(64 * 1024);
        sb.Append("<!doctype html><html><head><style>");
        sb.Append("body{font-family:sans-serif;font-size:18px;line-height:1.6;color:#222;");
        sb.Append("max-width:700px;margin:0 auto;padding:40px}h1{font-size:32px}h2{font-size:24px}a{color:#06c}</style></head><body>");
        sb.Append("<h1>The Long Article About Many Words</h1>");
        for (var p = 0; p < 120; p++)
        {
            if (p % 10 == 5)
                sb.Append("<h2>Section heading number ").Append(p).Append("</h2>");
            var n = 40 + rng.Next(50);
            sb.Append("<p>");
            for (var i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(words[rng.Next(words.Length)]);
            }
            sb.Append(". Visit <a href=\"#\">this link</a> for more.</p>");
        }
        sb.Append("</body></html>");
        return sb.ToString();
    }
}
