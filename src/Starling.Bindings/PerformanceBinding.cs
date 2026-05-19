using System.Diagnostics;
using Tessera.Js.Runtime;

namespace Tessera.Bindings;

/// <summary>
/// B5-5 — High Resolution Time Level 3 (W3C). Installs <c>performance</c> on
/// the window with <c>now()</c> and <c>timeOrigin</c>.
/// </summary>
/// <remarks>
/// <para><b>Monotonicity:</b> <c>now()</c> is backed by
/// <see cref="Stopwatch.GetTimestamp"/>, which is guaranteed monotonic on
/// every supported platform (osx/linux/win). <c>timeOrigin</c> captures the
/// wall-clock time at install (UTC ms since epoch), so
/// <c>timeOrigin + now()</c> approximates wall-clock without ever going
/// backwards across an NTP adjustment.</para>
/// <para><b>Out-of-scope (v1):</b> Performance Timeline (mark/measure,
/// PerformanceEntry, PerformanceObserver), Resource Timing, Navigation
/// Timing, Paint Timing. The two implemented members cover the
/// overwhelming majority of in-the-wild <c>performance</c> usage (timing
/// shims, RAF deltas, third-party RUM probes that feature-detect the rest).</para>
/// </remarks>
public static class PerformanceBinding
{
    public static void Install(JsRuntime runtime) => Install(runtime, () => (DateTimeOffset.UtcNow - DateTimeOffset.UnixEpoch).TotalMilliseconds);

    /// <summary>Test seam: lets a suite pin <c>timeOrigin</c> deterministically.</summary>
    public static void Install(JsRuntime runtime, Func<double> wallClockMs)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(wallClockMs);

        var realm = runtime.Realm;
        if (realm.GlobalObject.HasOwn("performance")) return;

        var origin = wallClockMs();
        var stopwatch = Stopwatch.StartNew();

        var performance = new JsObject(realm.ObjectPrototype);
        EventTargetBinding.DefineAccessor(realm, performance, "timeOrigin",
            (_, _) => JsValue.Number(origin));
        EventTargetBinding.DefineMethod(realm, performance, "now",
            (_, _) => JsValue.Number(stopwatch.Elapsed.TotalMilliseconds),
            length: 0);
        EventTargetBinding.DefineMethod(realm, performance, "toJSON",
            (_, _) =>
            {
                var json = new JsObject(realm.ObjectPrototype);
                json.DefineOwnProperty("timeOrigin",
                    PropertyDescriptor.Data(JsValue.Number(origin), writable: true, enumerable: true, configurable: true));
                return JsValue.Object(json);
            }, length: 0);

        realm.GlobalObject.DefineOwnProperty("performance",
            PropertyDescriptor.Data(JsValue.Object(performance), writable: true, enumerable: true, configurable: true));
    }
}
