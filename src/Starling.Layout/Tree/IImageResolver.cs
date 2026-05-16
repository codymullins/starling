using Tessera.Common.Image;
using Tessera.Dom;

namespace Tessera.Layout.Tree;

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
