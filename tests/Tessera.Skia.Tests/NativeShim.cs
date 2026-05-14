namespace Tessera.Skia.Tests;

/// <summary>
/// Test-time gate for the native <c>tessera_skia</c> P/Invoke smoke tests. The
/// shim dylib is built out-of-band by wp:M3-06g and is gitignored — it only
/// exists on a macOS dev box that has run the native build. CI on win/linux
/// has no dylib yet, so the P/Invoke tests <c>Skip</c> rather than fail.
/// </summary>
internal static class NativeShim
{
    /// <summary>
    /// The repo-root <c>runtimes/osx-arm64/native/libtessera_skia.dylib</c> path,
    /// resolved by walking up from the test assembly directory.
    /// </summary>
    private static readonly string? DylibPath = FindDylib();

    /// <summary>
    /// True when the current OS is macOS AND the shim dylib is present — the
    /// only configuration in which the P/Invoke flow can actually run.
    /// </summary>
    public static bool IsAvailable => OperatingSystem.IsMacOS() && DylibPath is not null;

    /// <summary>The xunit <c>Skip</c> reason when <see cref="IsAvailable"/> is false.</summary>
    public static string SkipReason =>
        OperatingSystem.IsMacOS()
            ? "libtessera_skia.dylib not found under runtimes/osx-arm64/native (run the native build)."
            : "tessera_skia shim is osx-arm64-only until win/linux dylibs are built (wp:M3-06g).";

    private static string? FindDylib()
    {
        const string relative = "runtimes/osx-arm64/native/libtessera_skia.dylib";
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        return null;
    }
}
