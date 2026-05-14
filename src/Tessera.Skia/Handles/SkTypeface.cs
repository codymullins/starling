using Microsoft.Win32.SafeHandles;
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
        fixed (byte* ttfPtr = ttfBytes)
        {
            status = NativeMethods.ts_typeface_from_data(ttfPtr, (nuint)ttfBytes.Length, out handle);
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

        var status = NativeMethods.ts_typeface_from_name(familyName, out nint handle);
        SkiaInteropException.ThrowIfNotOk(status, nameof(NativeMethods.ts_typeface_from_name));

        var typeface = new SkTypeface();
        typeface.SetHandle(handle);
        return typeface;
    }

    /// <summary>The raw native pointer, for passing to other interop calls.</summary>
    public nint Handle => handle;

    protected override bool ReleaseHandle()
    {
        NativeMethods.ts_typeface_destroy(handle);
        return true;
    }
}
