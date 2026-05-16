using Tessera.Css.Cascade;
using Tessera.Dom;
using Tessera.Layout;
using Tessera.Layout.Box;

namespace Tessera.Engine;

/// <summary>
/// A fully laid-out page exposed to interactive shells. Owns the box tree
/// (positions in document-space CSS px), the source DOM, and the resolver
/// objects backing image/stylesheet lookups — disposing the page releases
/// those resources.
/// </summary>
/// <remarks>
/// Interactive callers walk <see cref="Root"/> to render native views, hit-test
/// taps, drive Cmd-F, etc. The headless render path stays on
/// <see cref="TesseraEngine.RenderAsync"/> and goes through the rasterizer.
/// </remarks>
public sealed class LaidOutPage : IDisposable
{
    private readonly IDisposable _images;
    private readonly IDisposable _stylesheets;
    private readonly IDisposable _webFonts;
    private bool _disposed;

    internal LaidOutPage(
        BlockBox root,
        Document document,
        StyleEngine style,
        Size viewport,
        string url,
        string? title,
        IDisposable images,
        IDisposable stylesheets,
        IDisposable webFonts)
    {
        Root = root;
        Document = document;
        Style = style;
        Viewport = viewport;
        Url = url;
        Title = title;
        _images = images;
        _stylesheets = stylesheets;
        _webFonts = webFonts;
    }

    public BlockBox Root { get; }
    public Document Document { get; }

    /// <summary>
    /// The style engine that produced <see cref="Root"/>. Interactive shells
    /// call <c>Style.Compute(element, context)</c> with a hover/focus context
    /// to get an updated <c>ComputedStyle</c> and push the diff onto the
    /// corresponding view — no re-layout needed for cosmetic state changes.
    /// </summary>
    public StyleEngine Style { get; }

    public Size Viewport { get; }
    public string Url { get; }
    public string? Title { get; }

    /// <summary>
    /// The full laid-out page height in CSS px — typically taller than the
    /// viewport. The shell sizes its scroll surface to this value.
    /// </summary>
    public double DocumentHeight => Root.Frame.Height;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _images.Dispose();
        _stylesheets.Dispose();
        _webFonts.Dispose();
    }
}
