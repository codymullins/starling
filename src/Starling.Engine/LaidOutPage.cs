using Starling.Css.Cascade;
using Starling.Dom;
using Starling.Layout;
using Starling.Layout.Box;
using Starling.Layout.Tree;
using Starling.Net.Http;
using Starling.Paint;

namespace Starling.Engine;

/// <summary>
/// A fully laid-out page exposed to interactive shells. Owns the box tree
/// (positions in document-space CSS px), the source DOM, and the resolver
/// objects backing image/stylesheet lookups — disposing the page releases
/// those resources.
/// </summary>
/// <remarks>
/// Interactive callers walk <see cref="Root"/> to render native views, hit-test
/// taps, drive Cmd-F, etc. The headless render path stays on
/// <see cref="StarlingEngine.RenderAsync"/> and goes through the rasterizer.
/// </remarks>
public sealed class LaidOutPage : IDisposable
{
    // Process-wide monotonic source for DisplayListVersion. Each fresh layout
    // produces a new LaidOutPage and therefore a new version, so a re-layout /
    // re-style invalidates the picture cache wholesale (wp:M12-02). A scroll
    // reuses the same page object — same version — so its render can hit the
    // cache. Smarter partial invalidation is wp:M12-06.
    private static int _nextVersion;

    private readonly ImageFetcher _images;
    private readonly StylesheetFetcher _stylesheets;
    private readonly FontFaceRegistry _webFonts;
    private bool _disposed;
    private bool _resourcesTransferred;

    internal LaidOutPage(
        BlockBox root,
        Document document,
        StyleEngine style,
        Size viewport,
        string url,
        string? title,
        ImageFetcher images,
        StylesheetFetcher stylesheets,
        FontFaceRegistry webFonts,
        float? defaultFontSize,
        ConnectionSecurity? security = null)
    {
        Root = root;
        Document = document;
        Style = style;
        Viewport = viewport;
        Url = url;
        Title = title;
        Security = security;
        _images = images;
        _stylesheets = stylesheets;
        _webFonts = webFonts;
        DefaultFontSize = defaultFontSize;
        DisplayListVersion = System.Threading.Interlocked.Increment(ref _nextVersion);
    }

    public BlockBox Root { get; }
    public Document Document { get; }

    /// <summary>
    /// Monotonic version of this page's display list, assigned once at
    /// construction. The picture cache (wp:M12-02) keys on it: a render against a
    /// version different from the cached one is a full miss. Because every fresh
    /// layout is a new <see cref="LaidOutPage"/>, re-layout / re-style bumps the
    /// version automatically while a scroll (same page) leaves it stable.
    /// </summary>
    public int DisplayListVersion { get; }

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
    /// Security context of the main-document fetch (HTTP version, encryption,
    /// certificate) — backs the shell's lock affordance. Null for non-network
    /// documents (file://) or when the fetch didn't record it.
    /// </summary>
    public ConnectionSecurity? Security { get; }

    /// <summary>
    /// The full laid-out page height in CSS px — typically taller than the
    /// viewport. The shell sizes its scroll surface to this value.
    /// </summary>
    public double DocumentHeight => Root.Frame.Height;

    /// <summary>Default font size used when the page was first laid out.
    /// Re-paints (e.g. via <see cref="StarlingEngine.RenderFrame"/>) reuse
    /// it so frame-to-frame rasters stay consistent.</summary>
    internal float? DefaultFontSize { get; }

    internal ImageFetcher Images => _images;

    /// <summary>
    /// The image resolver behind this page, exposed for paint hosts that
    /// want to re-render at a different scale or with a different style
    /// override and still resolve <c>&lt;img&gt;</c> / CSS-<c>url()</c>
    /// references.
    /// </summary>
    public IImageResolver ImageResolver => _images;
    internal StylesheetFetcher Stylesheets => _stylesheets;
    internal FontFaceRegistry WebFonts => _webFonts;

    /// <summary>
    /// Produces a successor page from a fresh box tree / style engine laid out
    /// over the <em>same</em> <see cref="Document"/> and resource resolvers —
    /// the relayout path used when the viewport changes (window resize) without
    /// a navigation. The already-fetched images, stylesheets, and web fonts are
    /// handed to the successor, so disposing this page afterward leaves them
    /// alone (the successor owns them now). The caller must show the returned
    /// page and dispose this one.
    /// </summary>
    internal LaidOutPage Relayout(BlockBox root, StyleEngine style, Size viewport)
    {
        _resourcesTransferred = true;
        return new LaidOutPage(
            root, Document, style, viewport, Url, Title,
            _images, _stylesheets, _webFonts, DefaultFontSize, Security);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // A page whose resources were handed to a relayout successor must not
        // release them — they are still in use by that successor.
        if (_resourcesTransferred) return;
        _images.Dispose();
        _stylesheets.Dispose();
        _webFonts.Dispose();
    }
}
