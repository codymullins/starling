namespace Starling.Bindings.Jint;

// J3a — requestAnimationFrame / cancelAnimationFrame.
// Mirrors Starling.Bindings/AnimationFrameBinding.cs.
// Wave-2 agent J3a: route rAF onto ctx.Loop, sharing the simulated clock with
// the timers so a rAF-bootstrapped page settles on the same pump.
internal static class AnimationFrameBinding
{
    public static void Install(JintBackendContext ctx)
    {
        // TODO J3a
    }
}
