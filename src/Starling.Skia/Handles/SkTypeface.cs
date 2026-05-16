using Microsoft.Win32.SafeHandles;
using Tessera.Common.Diagnostics;
using Tessera.Skia.Interop;

namespace Tessera.Skia.Handles;

/// <summary>
/// Owning <see cref="System.Runtime.InteropServices.SafeHandle"/> for a native <c>TsTypeface*</c> — an
/// <c>SkTypeface</c>. Disposal calls <c>ts_typeface_destroy</c>.
/// </summary>
internal sealed class SkTypeface : SafeHandleZeroOrMinusOneIsInvalid
{
    private SkTypeface()
        : base(ownsHandle: true)
    {
    }

    /// <summary>Loads a typeface from embedded TTF/OTF bytes.</summary>
    /// <exception cref="SkiaInteropException">The native call failed.</exception>
    public static unsafe SkTypeface FromData(ReadOnlySpan<byte> ttfBytes)
    {
        nint handle;
        TsStatus status;
        lock (SkiaGate.Sync)
        {
            NativeCallTrace.Enter("ts_typeface_from_data", 0, $"ttf={ttfBytes.Length}");
            fixed (byte* ttfPtr = ttfBytes)
            {
                status = NativeMethods.ts_typeface_from_data(ttfPtr, (nuint)ttfBytes.Length, out handle);
            }
            NativeCallTrace.Exit("ts_typeface_from_data", handle);
        }

        SkiaInteropException.ThrowIfNotOk(status, nameof(NativeMethods.ts_typeface_from_data));

        var typeface = new SkTypeface();
        typeface.SetHandle(handle);
        return typeface;
    }

    /// <summary>Resolves a typeface by family name from the system font manager.</summary>
    /// <exception cref="SkiaInteropException">The native call failed.</exception>
    public static SkTypeface FromName(string familyName)
    {
        ArgumentNullException.ThrowIfNull(familyName);

        nint handle;
        TsStatus status;
        lock (SkiaGate.Sync)
        {
            NativeCallTrace.Enter("ts_typeface_from_name", 0, familyName);
            status = NativeMethods.ts_typeface_from_name(familyName, out handle);
            NativeCallTrace.Exit("ts_typeface_from_name", handle);
        }
        SkiaInteropException.ThrowIfNotOk(status, nameof(NativeMethods.ts_typeface_from_name));

        var typeface = new SkTypeface();
        typeface.SetHandle(handle);
        return typeface;
    }

    /// <summary>The raw native pointer, for passing to other interop calls.</summary>
    public nint Handle => handle;

    /// <summary>
    /// Returns a new typeface that is <c>this</c> with the supplied OpenType
    /// variation-axis settings applied. For a non-variable face the axes are
    /// silently ignored. The returned typeface is independent — disposing one
    /// does not affect the other.
    /// </summary>
    /// <exception cref="SkiaInteropException">The native call failed.</exception>
    public unsafe SkTypeface CloneWithVariations(ReadOnlySpan<TsFontVariation> variations)
    {
        nint outHandle;
        TsStatus status;
        lock (SkiaGate.Sync)
        {
            NativeCallTrace.Enter("ts_typeface_clone_variations", handle, $"axes={variations.Length}");
            fixed (TsFontVariation* varsPtr = variations)
            {
                status = NativeMethods.ts_typeface_clone_variations(
                    handle, varsPtr, (nuint)variations.Length, out outHandle);
            }
            NativeCallTrace.Exit("ts_typeface_clone_variations", outHandle);
        }
        SkiaInteropException.ThrowIfNotOk(status, nameof(NativeMethods.ts_typeface_clone_variations));

        var clone = new SkTypeface();
        clone.SetHandle(outHandle);
        return clone;
    }

    protected override bool ReleaseHandle()
    {
        lock (SkiaGate.Sync)
        {
            NativeCallTrace.Enter("ts_typeface_destroy", handle);
            NativeMethods.ts_typeface_destroy(handle);
            NativeCallTrace.Exit("ts_typeface_destroy", handle);
        }
        return true;
    }
}
