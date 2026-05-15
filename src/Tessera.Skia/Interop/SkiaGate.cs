namespace Tessera.Skia.Interop;

/// <summary>
/// Process-wide serialization gate for the native <c>tessera_skia</c> shim.
///
/// The shim wraps Skia / Graphite objects (<c>GrDirectContext</c>,
/// <c>SkSurface</c>, <c>SkFont</c>, <c>SkTypeface</c>) that are <b>not</b> safe
/// to touch from more than one thread at a time. The engine drives layout
/// (text shaping) and rendering from .NET thread-pool workers, so without this
/// gate two workers can be inside the shim at once — observed in the
/// native-call trace as 2,400+ overlapping ENTER/EXIT pairs, including
/// destroy-during-use — which corrupts the native heap and surfaces as an
/// intermittent <c>EXC_BAD_ACCESS</c>.
///
/// Every <c>Sk*</c> handle wrapper takes this lock around its
/// <c>NativeMethods.ts_*</c> call, so exactly one thread is ever inside the
/// shim. It is the C# <see cref="System.Threading.Monitor"/>, so it is
/// re-entrant on a single thread (a shim call that nests another is fine) and
/// cannot deadlock against itself.
///
/// This is a correctness-first stopgap — it serialises all GPU / shaping work.
/// The longer-term fix is to stop fanning that work across threads in the
/// first place (or give each thread its own context).
/// </summary>
internal static class SkiaGate
{
    /// <summary>The single lock object guarding all native shim calls.</summary>
    public static readonly object Sync = new();
}
