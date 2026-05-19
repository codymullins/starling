using System.Runtime.CompilerServices;

namespace Starling.Spec;

/// <summary>
/// A spec-conformance test that is <b>not yet expected to pass</b>.
/// <para>
/// Behaviour:
/// <list type="bullet">
///   <item>Tagged with <c>TestCategory("Spec:Pending")</c> (and the tracking wp,
///     if any) for filtering.</item>
///   <item>By default reported as <see cref="UnitTestOutcome.Inconclusive"/>
///     so the standard <c>dotnet test</c> run stays green while the test body
///     still serves as an in-repo, executable record of the spec requirement
///     that an agent must eventually satisfy.</item>
///   <item>Set environment variable <c>STARLING_RUN_PENDING=true</c> to actually
///     execute them — use this in a non-gating CI job to detect tests that have
///     started passing; promote them to <see cref="SpecFactAttribute"/> when they do.</item>
/// </list>
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class PendingFactAttribute : TestMethodAttribute
{
    public PendingFactAttribute(
        string reason,
        string? trackingWp = null,
        [CallerFilePath] string callerFilePath = "",
        [CallerLineNumber] int callerLineNumber = -1)
        : base(callerFilePath, callerLineNumber)
    {
        Reason = reason;
        TrackingWp = trackingWp;
    }

    public string Reason { get; }
    public string? TrackingWp { get; }

    public override async Task<TestResult[]> ExecuteAsync(ITestMethod testMethod)
    {
        if (PendingEnabled())
        {
            return await base.ExecuteAsync(testMethod).ConfigureAwait(false);
        }

        var label = TrackingWp is null
            ? $"pending: {Reason}"
            : $"pending ({TrackingWp}): {Reason}";

        return
        [
            new TestResult
            {
                Outcome = UnitTestOutcome.Inconclusive,
                TestFailureException = new AssertInconclusiveException(label),
            },
        ];
    }

    private static bool PendingEnabled()
    {
        var run = Environment.GetEnvironmentVariable("STARLING_RUN_PENDING");
        return !string.IsNullOrEmpty(run)
               && (string.Equals(run, "true", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(run, "1", StringComparison.Ordinal));
    }
}
