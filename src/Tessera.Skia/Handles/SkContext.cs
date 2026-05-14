using Microsoft.Win32.SafeHandles;
using Tessera.Skia.Interop;

namespace Tessera.Skia.Handles;

/// <summary>
/// Owning <see cref="System.Runtime.InteropServices.SafeHandle"/> for a native <c>TsContext*</c> — a Dawn
/// instance/adapter/device plus the Skia Graphite <c>Context</c> + <c>Recorder</c>.
/// Disposal calls <c>ts_context_destroy</c>. WebGPU handles never escape this.
/// </summary>
internal sealed class SkContext : SafeHandleZeroOrMinusOneIsInvalid
{
    private SkContext()
        : base(ownsHandle: true)
    {
    }

    /// <summary>
    /// Creates a context, selecting a Dawn backend per <paramref name="hint"/>.
    /// </summary>
    /// <exception cref="SkiaInteropException">The native call failed.</exception>
    public static SkContext Create(TsBackendHint hint = TsBackendHint.Auto)
    {
        var status = NativeMethods.ts_context_create(hint, out nint handle);
        SkiaInteropException.ThrowIfNotOk(status, nameof(NativeMethods.ts_context_create));

        var context = new SkContext();
        context.SetHandle(handle);
        return context;
    }

    /// <summary>The raw native pointer, for passing to other interop calls.</summary>
    public nint Handle => handle;

    /// <summary>The human-readable backend actually selected, e.g. "Dawn/Metal".</summary>
    public unsafe string BackendName()
    {
        const int bufferLen = 128;
        byte* buffer = stackalloc byte[bufferLen];
        nuint written = NativeMethods.ts_context_backend_name(handle, (nint)buffer, bufferLen);
        return System.Text.Encoding.UTF8.GetString(buffer, (int)written);
    }

    protected override bool ReleaseHandle()
    {
        NativeMethods.ts_context_destroy(handle);
        return true;
    }
}
