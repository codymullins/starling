using System.Runtime.CompilerServices;

namespace Starling.Spec;

/// <summary>
/// A spec-conformance test that is expected to <b>pass today</b>.
/// Acts as <see cref="TestMethodAttribute"/> plus a <c>TestCategory("Spec:Implemented")</c>
/// so the reporter can categorise it.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SpecFactAttribute : TestMethodAttribute
{
    public SpecFactAttribute(
        [CallerFilePath] string callerFilePath = "",
        [CallerLineNumber] int callerLineNumber = -1)
        : base(callerFilePath, callerLineNumber)
    {
    }

    public SpecFactAttribute(
        string displayName,
        [CallerFilePath] string callerFilePath = "",
        [CallerLineNumber] int callerLineNumber = -1)
        : base(callerFilePath, callerLineNumber)
    {
        DisplayName = displayName;
    }
}

/// <summary>
/// Categorises every <see cref="SpecFactAttribute"/>-decorated test under
/// <c>Spec:Implemented</c>. Apply at the class level to take effect for
/// every test in the class.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class SpecImplementedCategoryAttribute : TestCategoryBaseAttribute
{
    public override IList<string> TestCategories { get; } = ["Spec:Implemented"];
}
