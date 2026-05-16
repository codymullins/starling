using Microsoft.Win32.SafeHandles;
using Tessera.Common.Diagnostics;
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
        => Create(typeface, sizePx, bold: false, italic: false);

    /// <summary>
    /// Creates a font of <paramref name="sizePx"/> from <paramref name="typeface"/>,
    /// optionally applying synthetic bold (emboldened outlines) and/or italic (a
    /// forward skew). Used when the cascade resolves <c>font-weight: bold</c> or
    /// <c>font-style: italic</c> but no separate styled face is loaded.
    /// </summary>
    /// <exception cref="SkiaInteropException">The native call failed.</exception>
    public static SkFont Create(SkTypeface typeface, float sizePx, bool bold, bool italic)
    {
        ArgumentNullException.ThrowIfNull(typeface);

        nint handle;
        TsStatus status;
        lock (SkiaGate.Sync)
        {
            NativeCallTrace.Enter("ts_font_create_styled", typeface.Handle, $"size={sizePx} bold={bold} italic={italic}");
            status = NativeMethods.ts_font_create_styled(
                typeface.Handle, sizePx, bold ? 1 : 0, italic ? 1 : 0, out handle);
            NativeCallTrace.Exit("ts_font_create_styled", handle);
        }
        SkiaInteropException.ThrowIfNotOk(status, nameof(NativeMethods.ts_font_create_styled));

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
        TsFontMetrics metrics;
        TsStatus status;
        lock (SkiaGate.Sync)
        {
            NativeCallTrace.Enter("ts_font_metrics", handle);
            status = NativeMethods.ts_font_metrics(handle, out metrics);
            NativeCallTrace.Exit("ts_font_metrics", handle);
        }
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

        lock (SkiaGate.Sync)
        {
            NativeCallTrace.Enter("ts_shape_text", handle, $"utf8={utf8.Length} cap={glyphs.Length}");
            fixed (byte* textPtr = utf8)
            fixed (TsGlyph* glyphPtr = glyphs)
            {
                status = NativeMethods.ts_shape_text(
                    handle, textPtr, (nuint)utf8.Length, glyphPtr, (nuint)glyphs.Length, out count);
            }
            NativeCallTrace.Exit("ts_shape_text", handle);
        }

        if (status == TsStatus.InvalidArgument && (int)count > glyphs.Length)
        {
            // Buffer too small — `count` is the required capacity. Retry.
            glyphs = new TsGlyph[count];
            lock (SkiaGate.Sync)
            {
                NativeCallTrace.Enter("ts_shape_text", handle, $"utf8={utf8.Length} cap={glyphs.Length} retry");
                fixed (byte* textPtr = utf8)
                fixed (TsGlyph* glyphPtr = glyphs)
                {
                    status = NativeMethods.ts_shape_text(
                        handle, textPtr, (nuint)utf8.Length, glyphPtr, (nuint)glyphs.Length, out count);
                }
                NativeCallTrace.Exit("ts_shape_text", handle);
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
        lock (SkiaGate.Sync)
        {
            NativeCallTrace.Enter("ts_font_destroy", handle);
            NativeMethods.ts_font_destroy(handle);
            NativeCallTrace.Exit("ts_font_destroy", handle);
        }
        return true;
    }
}
