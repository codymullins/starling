using Microsoft.Win32.SafeHandles;
using Tessera.Skia.Interop;

namespace Tessera.Skia.Handles;

/// <summary>
/// Owning <see cref="System.Runtime.InteropServices.SafeHandle"/> for a native <c>TsFont*</c> — a sized
/// <c>SkFont</c> built from a <see cref="SkTypeface"/>. Disposal calls
/// <c>ts_font_destroy</c>.
/// </summary>
internal sealed class SkFont : SafeHandleZeroOrMinusOneIsInvalid
{
    private SkFont()
        : base(ownsHandle: true)
    {
    }

    /// <summary>Creates a font of <paramref name="sizePx"/> from <paramref name="typeface"/>.</summary>
    /// <exception cref="SkiaInteropException">The native call failed.</exception>
    public static SkFont Create(SkTypeface typeface, float sizePx)
    {
        ArgumentNullException.ThrowIfNull(typeface);

        var status = NativeMethods.ts_font_create(typeface.Handle, sizePx, out nint handle);
        SkiaInteropException.ThrowIfNotOk(status, nameof(NativeMethods.ts_font_create));

        var font = new SkFont();
        font.SetHandle(handle);
        return font;
    }

    /// <summary>The raw native pointer, for passing to other interop calls.</summary>
    public nint Handle => handle;

    /// <summary>Returns the sized font's metrics, in pixels.</summary>
    /// <exception cref="SkiaInteropException">The native call failed.</exception>
    public TsFontMetrics Metrics()
    {
        var status = NativeMethods.ts_font_metrics(handle, out TsFontMetrics metrics);
        SkiaInteropException.ThrowIfNotOk(status, nameof(NativeMethods.ts_font_metrics));
        return metrics;
    }

    /// <summary>
    /// Shapes UTF-8 <paramref name="text"/> into a positioned glyph run. The
    /// shim re-reports the required capacity if the first buffer is too small;
    /// this re-allocates and retries once.
    /// </summary>
    /// <exception cref="SkiaInteropException">The native call failed.</exception>
    public unsafe TsGlyph[] ShapeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(text);

        // First attempt: a generous guess of one glyph per byte.
        var glyphs = new TsGlyph[Math.Max(utf8.Length, 1)];
        nuint count;
        TsStatus status;

        fixed (byte* textPtr = utf8)
        fixed (TsGlyph* glyphPtr = glyphs)
        {
            status = NativeMethods.ts_shape_text(
                handle, textPtr, (nuint)utf8.Length, glyphPtr, (nuint)glyphs.Length, out count);
        }

        if (status == TsStatus.InvalidArgument && (int)count > glyphs.Length)
        {
            // Buffer too small — `count` is the required capacity. Retry.
            glyphs = new TsGlyph[count];
            fixed (byte* textPtr = utf8)
            fixed (TsGlyph* glyphPtr = glyphs)
            {
                status = NativeMethods.ts_shape_text(
                    handle, textPtr, (nuint)utf8.Length, glyphPtr, (nuint)glyphs.Length, out count);
            }
        }

        SkiaInteropException.ThrowIfNotOk(status, nameof(NativeMethods.ts_shape_text));

        if ((int)count == glyphs.Length)
            return glyphs;

        var result = new TsGlyph[count];
        Array.Copy(glyphs, result, (int)count);
        return result;
    }

    protected override bool ReleaseHandle()
    {
        NativeMethods.ts_font_destroy(handle);
        return true;
    }
}
