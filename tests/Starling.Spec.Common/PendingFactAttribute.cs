using Xunit;
using Xunit.v3;

namespace Starling.Spec;

/// <summary>
/// A spec-conformance test that is <b>not yet expected to pass</b>.
/// <para>
/// Behaviour:
/// <list type="bullet">
///   <item>Adds <c>Trait("Status", "Pending")</c> for filtering.</item>
///   <item>By default skipped, so the standard <c>dotnet test</c> run stays green
///     while the test body still serves as an in-repo, executable record of the
///     spec requirement that an agent must eventually satisfy.</item>
///   <item>Set environment variable <c>STARLING_RUN_PENDING=true</c> to actually
///     execute them — use this in a non-gating CI job to detect tests that have
///     started passing; promote them to <see cref="SpecFactAttribute"/> when they do.</item>
/// </list>
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class PendingFactAttribute : FactAttribute, ITraitAttribute
{
    public PendingFactAttribute(string reason, string? trackingWp = null)
    {
        Reason = reason;
        TrackingWp = trackingWp;

        var run = Environment.GetEnvironmentVariable("STARLING_RUN_PENDING");
        var enabled = !string.IsNullOrEmpty(run) &&
                      (string.Equals(run, "true", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(run, "1", StringComparison.Ordinal));
        if (!enabled)
        {
            Skip = trackingWp is null
                ? $"pending: {reason}"
                : $"pending ({trackingWp}): {reason}";
        }
    }

    public string Reason { get; }
    public string? TrackingWp { get; }

    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits()
    {
        var traits = new List<KeyValuePair<string, string>>(2)
        {
            new("Status", "Pending"),
        };
        if (!string.IsNullOrEmpty(TrackingWp))
        {
            traits.Add(new("Wp", TrackingWp));
        }
        return traits;
    }
}

/// <summary>
/// A spec-conformance test that is expected to <b>pass today</b>.
/// Adds <c>Trait("Status", "Implemented")</c>. Use this in lieu of
/// <see cref="FactAttribute"/> on spec tests so the reporter can categorise them.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SpecFactAttribute : FactAttribute, ITraitAttribute
{
    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits()
        => new[] { new KeyValuePair<string, string>("Status", "Implemented") };
}
