using System.Diagnostics;

namespace Tessera.Common.Diagnostics;

/// <summary>
/// TEMPORARY diagnostic instrumentation for the native-shim heap-corruption
/// investigation (GUI SIGSEGV when navigating to a page). Logs ENTER/EXIT
/// around every native interop call together with the calling thread's
/// identity, appended line-by-line to a file that is flushed on every write so
/// it survives a hard native crash.
///
/// Reading a post-crash log:
///   - the last ENTER with no matching EXIT is the call that was in flight
///     when the process died (or the crash is in managed code right after it);
///   - the <c>tid=</c> / <c>pool</c> / <c>name=</c> columns show which threads
///     touched the shim and whether two were ever inside it at once;
///   - a <c>ts_*_destroy</c> ENTER interleaved with another thread's call is a
///     destroy-during-use.
///
/// Remove this file and its call sites once the crash is root-caused.
/// </summary>
public static class NativeCallTrace
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly object Gate = new();
    private static readonly string LogPath = Init();
    private static long _seq;

    /// <summary>Absolute path of the trace log, also echoed to stderr at startup.</summary>
    public static string Path => LogPath;

    private static string Init()
    {
        string path;
        try
        {
            var dir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(dir))
                dir = System.IO.Path.GetTempPath();
            path = System.IO.Path.Combine(dir, "tessera-native-trace.log");
            File.WriteAllText(path, $"# tessera native-call trace — started {DateTime.Now:O}\n");
        }
        catch
        {
            path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tessera-native-trace.log");
        }
        try { Console.Error.WriteLine($"[NativeCallTrace] writing to {path}"); } catch { }
        return path;
    }

    /// <summary>Logged immediately before a native call.</summary>
    public static void Enter(string op, nint handle = 0, string detail = "")
        => Write("ENTER", op, handle, detail);

    /// <summary>Logged immediately after a native call returns.</summary>
    public static void Exit(string op, nint handle = 0)
        => Write("EXIT ", op, handle, "");

    /// <summary>A free-standing marker (phase boundary, etc.).</summary>
    public static void Mark(string op, string detail = "")
        => Write("MARK ", op, 0, detail);

    private static void Write(string kind, string op, nint handle, string detail)
    {
        var t = Thread.CurrentThread;
        var seq = Interlocked.Increment(ref _seq);
        var line = string.Concat(
            seq.ToString().PadLeft(6), " ",
            Clock.ElapsedMilliseconds.ToString().PadLeft(8), "ms ",
            kind,
            " tid=", t.ManagedThreadId.ToString().PadLeft(3),
            t.IsThreadPoolThread ? " POOL" : "     ",
            " name=", t.Name ?? "-",
            " op=", op,
            handle != 0 ? " h=0x" + handle.ToString("x") : "",
            detail.Length > 0 ? " " + detail : "",
            "\n");

        // The lock only orders the log lines; the native calls themselves still
        // run concurrently, so a real cross-thread overlap is still visible as
        // interleaved ENTER/EXIT and is not masked by this serialization.
        lock (Gate)
        {
            try { File.AppendAllText(LogPath, line); }
            catch { /* diagnostics must never throw into the call path */ }
        }
    }
}
