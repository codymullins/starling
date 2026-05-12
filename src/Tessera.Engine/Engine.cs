using System.Text;
using SixLabors.ImageSharp;
using Tessera.Common;
using Tessera.Common.Diagnostics;
using Tessera.Dom;
using Tessera.Net;
using Tessera.Paint;
using Tessera.Url;
using TesseraUrl = global::Tessera.Url.Url;

namespace Tessera.Engine;

/// <summary>
/// M0/M2 engine façade. One call: load a URL, parse the body's text, paint it
/// onto a bitmap. The full Browser / Page / Frame composition per
/// 01_ARCHITECTURE.md §E lands once layout/paint reach M1 fidelity.
/// </summary>
/// <remarks>
/// As of M2-07 the engine knows how to fetch <c>http://</c> and <c>https://</c>
/// via <see cref="TesseraHttpClient"/>. Rendering itself is still M0-fidelity
/// (text-on-white) until the in-flight M1 layout/paint pipeline lands.
/// </remarks>
public sealed class TesseraEngine
{
    private readonly IDiagnostics _diag;
    private readonly Painter _painter;
    private readonly Func<TesseraHttpClient> _httpFactory;

    public TesseraEngine(IDiagnostics? diagnostics = null, Painter? painter = null,
        Func<TesseraHttpClient>? httpFactory = null)
    {
        _diag = diagnostics ?? NoopDiagnostics.Instance;
        _painter = painter ?? new Painter();
        _httpFactory = httpFactory ?? (() => new TesseraHttpClient());
    }

    /// <summary>
    /// Render <paramref name="url"/> into a PNG written to <paramref name="outputPath"/>.
    /// Returns <c>true</c> on success.
    /// </summary>
    /// <remarks>
    /// In M0 only <c>file://</c> URLs are resolvable. <c>http(s)://</c> wait on
    /// M2 (see 03_NETWORKING.md). The renderer pulls <see cref="Node.TextContent"/>
    /// from <c>&lt;body&gt;</c> (or from <see cref="Document"/> if no body) and
    /// hands it to <see cref="Painter"/>. No CSS, no layout — just text on a
    /// white canvas.
    /// </remarks>
    public Result<RenderOutcome, RenderError> Render(string url, RenderOptions options, string outputPath)
        => RenderAsync(url, options, outputPath, CancellationToken.None).GetAwaiter().GetResult();

    public async Task<Result<RenderOutcome, RenderError>> RenderAsync(
        string url, RenderOptions options, string outputPath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(outputPath);

        using var _ = _diag.Span("engine", $"render {url} -> {outputPath}");

        var parsed = UrlParser.Parse(url);
        if (parsed.IsErr)
            return Result<RenderOutcome, RenderError>.Err(new RenderError($"URL parse failed: {parsed.Error}"));

        var u = parsed.Value;
        string html;
        try
        {
            if (u.IsFile)
            {
                var path = u.ToFileSystemPath();
                if (!File.Exists(path))
                    return Result<RenderOutcome, RenderError>.Err(new RenderError($"File not found: {path}"));
                html = File.ReadAllText(path);
            }
            else if (u.IsHttp || u.IsHttps)
            {
                var fetched = await FetchHtmlAsync(u, ct).ConfigureAwait(false);
                if (fetched.IsErr)
                    return Result<RenderOutcome, RenderError>.Err(fetched.Error);
                html = fetched.Value;
            }
            else
            {
                return Result<RenderOutcome, RenderError>.Err(new RenderError(
                    $"Unsupported scheme '{u.Scheme}' for M0."));
            }
        }
        catch (IOException ex)
        {
            return Result<RenderOutcome, RenderError>.Err(new RenderError(ex.Message));
        }

        var doc = Html.HtmlParser.Parse(html);
        var displayText = ExtractDisplayText(doc);

        using var image = _painter.RenderText(displayText, options.Viewport, options.FontSize);

        try
        {
            EnsureOutputDirectory(outputPath);
            image.SaveAsPng(outputPath);
        }
        catch (IOException ex)
        {
            return Result<RenderOutcome, RenderError>.Err(new RenderError($"Save failed: {ex.Message}"));
        }

        _diag.Log(DiagLevel.Info, "engine",
            $"Wrote {outputPath} ({image.Width}x{image.Height}, text length={displayText.Length}).");

        return Result<RenderOutcome, RenderError>.Ok(
            new RenderOutcome(outputPath, image.Width, image.Height, displayText));
    }

    private static void EnsureOutputDirectory(string outputPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    private async Task<Result<string, RenderError>> FetchHtmlAsync(TesseraUrl url, CancellationToken ct)
    {
        using var http = _httpFactory();
        var response = await http.GetAsync(url, ct).ConfigureAwait(false);
        if (response.IsErr)
            return Result<string, RenderError>.Err(new RenderError(
                $"Network error fetching {url}: {response.Error}"));

        var resp = response.Value;
        if (resp.StatusCode is < 200 or >= 400)
            return Result<string, RenderError>.Err(new RenderError(
                $"HTTP {resp.StatusCode} {resp.ReasonPhrase} from {url}"));

        var contentType = resp.Headers.GetFirst("Content-Type");
        var encoding = ResolveEncoding(contentType, resp.Body.Span);
        return Result<string, RenderError>.Ok(encoding.GetString(resp.Body.Span));
    }

    /// <summary>
    /// Charset sniff: prefer the <c>Content-Type</c>'s <c>charset=</c>
    /// parameter, then a recognised BOM, else UTF-8. The full WHATWG
    /// preamble + meta sniff lands with the HTML parser's encoding
    /// integration — see 03_NETWORKING.md "Encoding sniffing".
    /// </summary>
    internal static Encoding ResolveEncoding(string? contentType, ReadOnlySpan<byte> body)
    {
        if (contentType is { Length: > 0 })
        {
            foreach (var raw in contentType.Split(';'))
            {
                var part = raw.Trim();
                const string prefix = "charset=";
                if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var name = part[prefix.Length..].Trim().Trim('"');
                    try { return Encoding.GetEncoding(name); }
                    catch (ArgumentException) { /* fall through */ }
                }
            }
        }

        if (body.Length >= 3 && body[0] == 0xEF && body[1] == 0xBB && body[2] == 0xBF)
            return Encoding.UTF8;
        if (body.Length >= 2 && body[0] == 0xFF && body[1] == 0xFE)
            return Encoding.Unicode;
        if (body.Length >= 2 && body[0] == 0xFE && body[1] == 0xFF)
            return Encoding.BigEndianUnicode;

        return Encoding.UTF8;
    }

    internal static string ExtractDisplayText(Document doc)
    {
        // Prefer the body; fall back to the whole document so single-line input
        // fragments still render.
        var source = (Node?)doc.Body ?? doc;
        var raw = source.TextContent;

        // Normalize whitespace per the spec's "white-space: normal" handling —
        // collapse runs of whitespace into single ASCII spaces, then trim. This
        // is enough for M0; M1 will own real whitespace handling in the
        // inline formatting context (see 07_LAYOUT.md).
        if (raw.Length == 0) return string.Empty;
        var buf = new System.Text.StringBuilder(raw.Length);
        var prevWs = false;
        foreach (var ch in raw)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!prevWs && buf.Length > 0) buf.Append(' ');
                prevWs = true;
            }
            else
            {
                buf.Append(ch);
                prevWs = false;
            }
        }
        return buf.ToString().TrimEnd();
    }
}

public sealed record RenderOptions(Size Viewport, float FontSize = 32f)
{
    public static RenderOptions Default { get; } = new(new Size(800, 600));
}

public sealed record RenderOutcome(string OutputPath, int Width, int Height, string DisplayText);

public sealed record RenderError(string Message);
