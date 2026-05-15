using Microsoft.Win32.SafeHandles;
using Tessera.Common.Diagnostics;
using Tessera.Skia.Interop;

namespace Tessera.Skia.Handles;

/// <summary>
/// Non-owning <see cref="System.Runtime.InteropServices.SafeHandle"/> for a native <c>TsCanvas*</c> — a draw
/// target view onto a <see cref="SkSurface"/>. The shim owns the canvas (it is
/// valid until the surface is destroyed), so <see cref="ReleaseHandle"/> is a
/// no-op: this wrapper exists only to give the canvas a typed, uniform shape
/// alongside the owning handles. Construct it via <see cref="SkSurface.GetCanvas"/>.
/// </summary>
internal sealed class SkCanvas : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SkCanvas(nint borrowedHandle)
        : base(ownsHandle: false)
    {
        SetHandle(borrowedHandle);
    }

    /// <summary>The raw native pointer, for passing to other interop calls.</summary>
    public nint Handle => handle;

    /// <summary>Clears the whole canvas to <paramref name="color"/>.</summary>
    /// <exception cref="SkiaInteropException">The native call failed.</exception>
    public void Clear(TsColor color)
    {
        TsStatus status;
        lock (SkiaGate.Sync)
        {
            NativeCallTrace.Enter("ts_canvas_clear", handle);
            status = NativeMethods.ts_canvas_clear(handle, color);
            NativeCallTrace.Exit("ts_canvas_clear", handle);
        }
        SkiaInteropException.ThrowIfNotOk(status, nameof(NativeMethods.ts_canvas_clear));
    }

    /// <summary>Fills <paramref name="rect"/> with <paramref name="color"/>.</summary>
    /// <exception cref="SkiaInteropException">The native call failed.</exception>
    public void FillRect(TsRect rect, TsColor color)
    {
        TsStatus status;
        lock (SkiaGate.Sync)
        {
            NativeCallTrace.Enter("ts_canvas_fill_rect", handle);
            status = NativeMethods.ts_canvas_fill_rect(handle, rect, color);
            NativeCallTrace.Exit("ts_canvas_fill_rect", handle);
        }
        SkiaInteropException.ThrowIfNotOk(status, nameof(NativeMethods.ts_canvas_fill_rect));
    }

    /// <summary>Strokes the outline of <paramref name="rect"/>.</summary>
    /// <exception cref="SkiaInteropException">The native call failed.</exception>
    public void StrokeRect(TsRect rect, TsColor color, float strokeWidth)
    {
        TsStatus status;
        lock (SkiaGate.Sync)
        {
            NativeCallTrace.Enter("ts_canvas_stroke_rect", handle);
            status = NativeMethods.ts_canvas_stroke_rect(handle, rect, color, strokeWidth);
            NativeCallTrace.Exit("ts_canvas_stroke_rect", handle);
        }
        SkiaInteropException.ThrowIfNotOk(status, nameof(NativeMethods.ts_canvas_stroke_rect));
    }

    /// <summary>Draws a pre-shaped glyph run with the given font.</summary>
    /// <exception cref="SkiaInteropException">The native call failed.</exception>
    public unsafe void DrawText(SkFont font, ReadOnlySpan<TsGlyph> glyphs, TsColor color)
    {
        ArgumentNullException.ThrowIfNull(font);

        lock (SkiaGate.Sync)
        {
            NativeCallTrace.Enter("ts_canvas_draw_text", handle, $"font=0x{font.Handle:x} glyphs={glyphs.Length}");
            fixed (TsGlyph* glyphPtr = glyphs)
            {
                var status = NativeMethods.ts_canvas_draw_text(
                    handle, font.Handle, glyphPtr, (nuint)glyphs.Length, color);
                NativeCallTrace.Exit("ts_canvas_draw_text", handle);
                SkiaInteropException.ThrowIfNotOk(status, nameof(NativeMethods.ts_canvas_draw_text));
            }
        }
    }

    /// <summary>
    /// Draws an image from tightly-packed RGBA8888 <paramref name="pixels"/>,
    /// scaled into <paramref name="dstRect"/>.
    /// </summary>
    /// <exception cref="SkiaInteropException">The native call failed.</exception>
    public unsafe void DrawImage(ReadOnlySpan<byte> pixels, int width, int height, TsRect dstRect)
    {
        lock (SkiaGate.Sync)
        {
            NativeCallTrace.Enter("ts_canvas_draw_image", handle, $"{width}x{height} bytes={pixels.Length}");
            fixed (byte* pixelPtr = pixels)
            {
                var status = NativeMethods.ts_canvas_draw_image(handle, pixelPtr, width, height, dstRect);
                NativeCallTrace.Exit("ts_canvas_draw_image", handle);
                SkiaInteropException.ThrowIfNotOk(status, nameof(NativeMethods.ts_canvas_draw_image));
            }
        }
    }

    /// <summary>Draws <paramref name="image"/> scaled into <paramref name="dstRect"/>.</summary>
    /// <exception cref="SkiaInteropException">The native call failed.</exception>
    public void DrawImage(SkImage image, TsRect dstRect)
    {
        ArgumentNullException.ThrowIfNull(image);
        DrawImage(image.Pixels, image.Width, image.Height, dstRect);
    }

    protected override bool ReleaseHandle()
    {
        // Borrowed handle — owned by the SkSurface. Nothing to release.
        return true;
    }
}
