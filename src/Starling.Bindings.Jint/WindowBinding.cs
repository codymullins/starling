namespace Starling.Bindings.Jint;

// J2d — Window / global surface.
// Mirrors Starling.Bindings/WindowBinding.cs.
// Wave-2 agent J2d: set the global's prototype to ctx.Wrappers.WindowPrototype
// (inheriting EventTarget.prototype), expose window/self/globalThis/location/
// document, console, navigator, and the FireDomContentLoaded/FireLoad triggers
// the session calls. StorageBinding/HistoryBinding/PerformanceBinding are the
// sibling J2d files.
internal static class WindowBinding
{
    public static void Install(JintBackendContext ctx)
    {
        // TODO J2d
    }
}
