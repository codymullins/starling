namespace Starling.Bindings.Jint;

// J2c — EventTarget + Event dispatch.
// Mirrors Starling.Bindings/EventTargetBinding.cs.
// Wave-2 agent J2c: install ctx.Wrappers.EventTargetPrototype +
// ctx.Wrappers.EventPrototype, addEventListener/removeEventListener/dispatchEvent
// over the real Starling.Dom.Events dispatch, and the Event constructor. Runs
// before NodeBindings in InstallAll so Node.prototype can inherit it.
internal static class EventTargetBinding
{
    public static void Install(JintBackendContext ctx)
    {
        // TODO J2c
    }
}
