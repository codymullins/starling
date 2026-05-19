namespace Starling.Css.Spec.Tests.CssVariables1;

/// <summary>
/// <see href="https://www.w3.org/TR/css-variables-1/#defining-variables">CSS Variables L1 §2</see>:
/// custom-property declaration syntax (<c>--*</c>).
/// </summary>
[Spec("css-variables-1", "https://www.w3.org/TR/css-variables-1/", section: "2")]
public sealed class CustomPropertyParsingTests
{
    [PendingFact("custom-property parsing/registration test harness not yet wired",
                 trackingWp: "wp:spec-css-variables-1")]
    public void Declaration_with_double_dash_prefix_is_a_custom_property()
    {
        // GIVEN  --brand: #036;
        // THEN   the declaration is preserved as a custom property whose value is
        //        the token stream "#036" (whitespace-trimmed per §2).
        throw new NotImplementedException("Wire to Starling.Css.Cascade.StyleEngine once the spec harness exists.");
    }

    [PendingFact("token-stream preservation not asserted yet", trackingWp: "wp:spec-css-variables-1")]
    public void Custom_property_preserves_arbitrary_token_stream()
    {
        // --x: 1px solid red !important;  →  value is "1px solid red", important = true.
        throw new NotImplementedException();
    }
}
