namespace Starling.Bindings.Jint;

// J3b — fetch() + Request/Response/Headers.
// Mirrors Starling.Bindings/FetchBinding.cs.
// Wave-2 agent J3b: implement fetch over ctx.Http (Starling.Net), returning a
// Jint Promise. Resolve completions onto ctx.Loop/microtask queue so the
// session's pump observes them.
internal static class FetchBinding
{
    public static void Install(JintBackendContext ctx)
    {
        // TODO J3b
    }
}
