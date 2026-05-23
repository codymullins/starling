namespace Starling.Bindings.Jint;

// J2b — Node / Element / Document prototypes + methods.
// Mirrors Starling.Bindings/NodeBindings.cs, DomWrappers.cs, QuerySelectorEngine.cs.
// Wave-2 agent J2b: populate ctx.Wrappers.NodePrototype / ElementPrototype /
// DocumentPrototype here using JintInterop.DefineMethod / DefineAccessor, and
// expose `document` on the global. Reuse Starling.Css selector matching for
// querySelector* rather than re-porting it. Do not edit other Install files.
internal static class NodeBindings
{
    public static void Install(JintBackendContext ctx)
    {
        // TODO J2b
    }
}
