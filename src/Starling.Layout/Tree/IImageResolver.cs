using Starling.Common.Image;
using Starling.Css.Values;
using Starling.Dom;

namespace Starling.Layout.Tree;

/// <summary>
/// Looks up the decoded image (if any) for an <c>&lt;img&gt;</c> element.
/// Populated upstream of layout — typically by the engine fetching and
/// decoding each <c>&lt;img src&gt;</c> before kicking off render.
/// </summary>
public interface IImageResolver
{
    /// <summary>
    /// Resolve <paramref name="element"/> to a decoded image. Returns
    /// <c>false</c> when the source could not be fetched / decoded; layout
    /// will fall back to the element's <c>alt</c> text in that case.
    /// </summary>
    bool TryResolve(Element element, out ResolvedImage image);

    /// <summary>
    /// Resolve a CSS <c>url(...)</c> reference (e.g. <c>background-image</c>)
    /// to a decoded image. Defaults to no-op; implementations that prefetch
    /// CSS-referenced images override it.
    /// </summary>
    bool TryResolveUrl(string url, out DecodedImage image)
    {
        image = null!;
        return false;
    }

    /// <summary>
    /// Rasterize an inline <c>&lt;svg&gt;</c> element to a decoded image.
    /// <paramref name="currentColor"/> is the element's computed <c>color</c>,
    /// used to resolve <c>currentColor</c> (and the default fill/stroke that
    /// inherit it) inside the SVG. Returns <c>false</c> when SVG rendering is
    /// unavailable or the element yields no drawable geometry, so layout falls
    /// back to the element's accessible name. Defaults to no-op.
    /// </summary>
    bool TryResolveInlineSvg(Element svg, CssColor currentColor, out ResolvedImage image)
    {
        image = default;
        return false;
    }
}

/// <summary>
/// A resolved image: intrinsic pixel dimensions plus the backend-neutral
/// decoded pixel buffer (<see cref="DecodedImage"/>).
/// </summary>
public readonly record struct ResolvedImage(double Width, double Height, DecodedImage Source);

/// <summary>Resolver that never finds an image — every &lt;img&gt; falls back to alt text.</summary>
public sealed class NullImageResolver : IImageResolver
{
    public static NullImageResolver Instance { get; } = new();
    public bool TryResolve(Element element, out ResolvedImage image)
    {
        image = default;
        return false;
    }
}
