namespace Starling.Css.Spec.Tests.CssVariables1;

/// <summary>
/// <see href="https://www.w3.org/TR/css-variables-1/#cycles">CSS Variables L1 §3.3</see>:
/// cycle detection in <c>var()</c> chains.
/// </summary>
[Spec("css-variables-1", "https://www.w3.org/TR/css-variables-1/", section: "3.3")]
public sealed class VarCycleDetectionTests
{
    [PendingFact("cycle detection in custom-property graph not implemented",
                 trackingWp: "wp:spec-css-variables-1-cycles")]
    public void Direct_self_reference_makes_property_invalid()
    {
        // --a: var(--a);   →   --a computes to the guaranteed-invalid value;
        //                       any property using var(--a) falls back / uses unset.
        throw new NotImplementedException();
    }

    [PendingFact("indirect cycle detection not implemented",
                 trackingWp: "wp:spec-css-variables-1-cycles")]
    public void Indirect_cycle_through_chain_is_detected()
    {
        // --a: var(--b);  --b: var(--c);  --c: var(--a);   →   all three invalid.
        throw new NotImplementedException();
    }
}
