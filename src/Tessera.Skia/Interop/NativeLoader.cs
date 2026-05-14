using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Tessera.Skia.Interop;

/// <summary>
/// Installs a <see cref="NativeLibrary.SetDllImportResolver"/> fallback for the
/// <c>tessera_skia</c> shim. The native library is built out-of-band by
/// wp:M3-06g and lives under the gitignored repo-root
/// <c>runtimes/&lt;rid&gt;/native/</c> tree — it is not yet a NuGet package, so
/// the default loader cannot always find it (the Mac Catalyst <c>.app</c>
/// bundle layout and test runs with a differing working directory both miss
/// it). The csproj copies the lib into the output's <c>runtimes</c> layout for
/// the normal case; this resolver is the belt-and-braces fallback that probes
/// the repo-root tree directly.
/// </summary>
internal static class NativeLoader
{
    private const string LibraryName = "tessera_skia";

    // CA2255: the ModuleInitializer attribute is normally discouraged in
    // libraries — but installing a DllImportResolver before any P/Invoke runs
    // is exactly the "advanced" scenario the rule carves out. There is no other
    // hook that reliably runs before the first NativeMethods call.
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Initialize()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeLoader).Assembly, Resolve);
    }
#pragma warning restore CA2255

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal))
            return nint.Zero;

        foreach (string candidate in CandidatePaths())
        {
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out nint handle))
                return handle;
        }

        // Fall through to the default resolution (runtimes/<rid>/native next to
        // the assembly, OS search paths) — return zero to signal "not handled".
        return nint.Zero;
    }

    /// <summary>
    /// RID-specific candidate file paths, most-likely first: next to the
    /// assembly, then walking up from the assembly directory to a repo-root
    /// <c>runtimes/&lt;rid&gt;/native/</c> tree.
    /// </summary>
    private static IEnumerable<string> CandidatePaths()
    {
        string rid = CurrentRid();
        string fileName = NativeFileName();
        string relative = Path.Combine("runtimes", rid, "native", fileName);

        string asmDir = Path.GetDirectoryName(typeof(NativeLoader).Assembly.Location) ?? AppContext.BaseDirectory;

        // 1. Output-dir runtimes layout (csproj copy).
        yield return Path.Combine(asmDir, relative);
        // 2. Flat next to the assembly.
        yield return Path.Combine(asmDir, fileName);

        // 3. Walk up looking for a repo-root runtimes tree (covers test runs
        //    and the Mac Catalyst .app bundle layout).
        var dir = new DirectoryInfo(asmDir);
        while (dir is not null)
        {
            yield return Path.Combine(dir.FullName, relative);
            dir = dir.Parent;
        }
    }

    private static string CurrentRid()
    {
        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "unknown",
        };

        if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
            return $"osx-{arch}";
        if (OperatingSystem.IsWindows())
            return $"win-{arch}";
        return $"linux-{arch}";
    }

    private static string NativeFileName()
    {
        if (OperatingSystem.IsWindows())
            return $"{LibraryName}.dll";
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
            return $"lib{LibraryName}.dylib";
        return $"lib{LibraryName}.so";
    }
}
