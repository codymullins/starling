namespace Starling.Css.Spec.Tests.CssVariables1;

/// <summary>
/// <see href="https://www.w3.org/TR/css-variables-1/#using-variables">CSS Variables L1 §3</see>:
/// <c>var()</c> substitution.
/// </summary>
[TestClass]
[Spec("css-variables-1", "https://www.w3.org/TR/css-variables-1/", section: "3")]
public sealed class VarSubstitutionTests
{
    [PendingFact("var() fallback resolution end-to-end not yet covered",
                 trackingWp: "wp:spec-css-variables-1")]
    public void Var_with_fallback_uses_fallback_when_property_undefined()
    {
        // .x { color: var(--missing, red); }   →   computed color = red
        throw new NotImplementedException();
    }

    [PendingFact("nested var() in fallback not covered", trackingWp: "wp:spec-css-variables-1")]
    public void Var_fallback_may_itself_contain_var()
    {
        // .x { color: var(--missing, var(--also-missing, blue)); } → blue
        throw new NotImplementedException();
    }
}
