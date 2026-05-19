namespace Starling.Css.Spec.Tests.CssBackgrounds3;

/// <summary>
/// <see href="https://www.w3.org/TR/css-backgrounds-3/#background">CSS Backgrounds 3 §3.4</see>:
/// the <c>background</c> shorthand.
/// </summary>
[TestClass]
[Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/", section: "3.4")]
public sealed class BackgroundShorthandTests
{
    [PendingFact("background shorthand parser not yet implemented",
                 trackingWp: "wp:spec-css-backgrounds-3")]
    public void Shorthand_sets_color_image_position_size_repeat_origin_clip_attachment()
    {
        // background: #fff url("a.png") no-repeat fixed center/cover content-box padding-box;
        // → all eight longhands set as per §3.4 reset rules.
        throw new NotImplementedException();
    }

    [PendingFact("multi-layer background parser not implemented",
                 trackingWp: "wp:spec-css-backgrounds-3-layers")]
    public void Shorthand_supports_multiple_comma_separated_layers()
    {
        // background: url(a.png) top left, linear-gradient(red, blue) bottom / cover, #eee;
        // → three layers; only the last may carry a background-color.
        throw new NotImplementedException();
    }
}
