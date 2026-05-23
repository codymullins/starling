namespace Starling.Bindings.Jint;

// J3a — setTimeout / setInterval / clearTimeout / clearInterval.
// Mirrors Starling.Bindings/TimersBinding.cs.
// Wave-2 agent J3a: route timers onto ctx.Loop (the WebEventLoop the session
// advances in PumpOnce). Callbacks must run on the Jint engine and then have
// promise jobs drained (ctx.Engine.Advanced.ProcessTasks()).
internal static class TimersBinding
{
    public static void Install(JintBackendContext ctx)
    {
        // TODO J3a
    }
}
