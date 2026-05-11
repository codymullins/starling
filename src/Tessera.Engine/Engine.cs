using SixLabors.ImageSharp;
using Tessera.Common;
using Tessera.Common.Diagnostics;
using Tessera.Dom;
using Tessera.Paint;
using Tessera.Url;

namespace Tessera.Engine;

/// <summary>
/// M0 engine façade. One call: load a URL, parse the body's text, paint it onto
/// a bitmap. The full Browser / Page / Frame composition per
/// 01_ARCHITECTURE.md §E lands in M1+ once we have a layout pipeline to drive
/// (those types don't exist yet, hence plain text rather than cref).
/// </summary>
public sealed class TesseraEngine
{
    private readonly IDiagnostics _diag;
    private readonly Painter _painter;

    public TesseraEngine(IDiagnostics? diagnostics = null, Painter? painter = null)
    {
        _diag = diagnostics ?? NoopDiagnostics.Instance;
        _painter = painter ?? new Painter();
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
                return Result<RenderOutcome, RenderError>.Err(new RenderError(
                    $"HTTP(S) loading is M2 work — see browser-plan/03_NETWORKING.md. Cannot fetch {u}."));
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
