namespace Starling.Css.Spec.Tests.CssBackgrounds3;

/// <summary>
/// <see href="https://www.w3.org/TR/css-backgrounds-3/#border-radius">CSS Backgrounds 3 §5</see>:
/// <c>border-radius</c> and its longhands.
/// </summary>
[TestClass]
[Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/", section: "5")]
public sealed class BorderRadiusTests
{
    [PendingFact("border-radius shorthand parser not implemented",
                 trackingWp: "wp:spec-css-backgrounds-3-radius")]
    public void Single_value_applies_to_all_four_corners()
    {
        // border-radius: 8px;  → all 4 corners 8px horizontal & vertical.
        throw new NotImplementedException();
    }

    [PendingFact("border-radius elliptical corners not implemented",
                 trackingWp: "wp:spec-css-backgrounds-3-radius")]
    public void Slash_separates_horizontal_from_vertical_radii()
    {
        // border-radius: 10px 20px / 5px 15px;
        // → corners: 10/5, 20/15, 10/5, 20/15
        throw new NotImplementedException();
    }
}
