namespace Starling.Bindings.Jint;

// J4 — ES module loader (static import, top-level await, dynamic import()).
// Mirrors the module path in Starling.Engine + Starling.Js/Modules.
// Wave-2 agent J4: wire ctx.Engine.Modules + a resolver/loader over ctx.Fetch so
// `<script type=module>` and dynamic import() resolve through one mechanism.
// JintScriptSession.RunModuleScriptAsync currently throws NotSupported until
// this is implemented; replace that path here.
internal static class ModuleLoader
{
    public static void Install(JintBackendContext ctx)
    {
        // TODO J4
    }
}
