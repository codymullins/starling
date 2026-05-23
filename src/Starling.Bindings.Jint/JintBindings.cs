namespace Starling.Bindings.Jint;

/// <summary>
/// Single registration point that installs every Web-API binding family onto a
/// <see cref="JintBackendContext"/>. Each family lives in its own file with a
/// no-op-until-implemented <c>Install(JintBackendContext)</c>; Wave-2 agents fill
/// in exactly one file and never touch this dispatcher.
/// </summary>
/// <remarks>
/// Order matters where one family's prototype inherits another's:
/// EventTarget (J2c) → Node/Element/Document (J2b) → Window + Storage/History/
/// Performance (J2d) → Timers/rAF (J3a) → fetch/XHR (J3b/J3c) → Observers/
/// crypto/cookies (J3d) → module loader (J4). Cookies run after NodeBindings so
/// the DocumentPrototype slot exists.
/// </remarks>
public static class JintBindings
{
    public static void InstallAll(JintBackendContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        // J2c — EventTarget + Event (Node.prototype inherits this).
        EventTargetBinding.Install(ctx);

        // J2b — Node / Element / Document.
        NodeBindings.Install(ctx);

        // J2d — Window/global + companions.
        WindowBinding.Install(ctx);
        StorageBinding.Install(ctx);
        HistoryBinding.Install(ctx);
        PerformanceBinding.Install(ctx);

        // J3a — timers + rAF on the simulated loop.
        TimersBinding.Install(ctx);
        AnimationFrameBinding.Install(ctx);

        // J3b/J3c — network.
        FetchBinding.Install(ctx);
        XhrBinding.Install(ctx);

        // J3d — observers, crypto, cookies (cookies after NodeBindings).
        ObserversBinding.Install(ctx);
        CryptoBinding.Install(ctx);
        CookieBinding.Install(ctx);

        // J4 — ES modules.
        ModuleLoader.Install(ctx);
    }
}
