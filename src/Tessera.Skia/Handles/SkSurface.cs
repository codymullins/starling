using Microsoft.Win32.SafeHandles;
using Tessera.Common.Diagnostics;
using Tessera.Skia.Interop;

namespace Tessera.Skia.Handles;

/// <summary>
/// Owning <see cref="System.Runtime.InteropServices.SafeHandle"/> for a native <c>TsSurface*</c> — an
/// offscreen Graphite render target. Disposal calls <c>ts_surface_destroy</c>.
/// </summary>
internal sealed class SkSurface : SafeHandleZeroOrMinusOneIsInvalid
{
    private SkSurface()
        : base(ownsHandle: true)
    {
    }

    /// <summary>Creates an offscreen <paramref name="width"/>×<paramref name="height"/> surface.</summary>
    /// <exception cref="SkiaInteropException">The native call failed.</exception>
    public static SkSurface Create(SkContext context, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(context);

        NativeCallTrace.Enter("ts_surface_create", context.Handle, $"{width}x{height}");
        var status = NativeMethods.ts_surface_create(context.Handle, width, height, out nint handle);
        NativeCallTrace.Exit("ts_surface_create", handle);
        SkiaInteropException.ThrowIfNotOk(status, nameof(NativeMethods.ts_surface_create));

        var surface = new SkSurface();
        surface.SetHandle(handle);
        return surface;
    }

    /// <summary>The raw native pointer, for passing to other interop calls.</summary>
    public nint Handle => handle;

    /// <summary>
    /// Returns the surface's borrowed canvas view. The canvas is owned by the
    /// surface and must not be disposed independently.
    /// </summary>
    /// <exception cref="SkiaInteropException">The native call failed.</exception>
    public SkCanvas GetCanvas()
    {
        NativeCallTrace.Enter("ts_surface_get_canvas", handle);
        var status = NativeMethods.ts_surface_get_canvas(handle, out nint canvas);
        NativeCallTrace.Exit("ts_surface_get_canvas", canvas);
        SkiaInteropException.ThrowIfNotOk(status, nameof(NativeMethods.ts_surface_get_canvas));
        return new SkCanvas(canvas);
    }

    /// <summary>
    /// Snaps the Graphite recorder, submits to the device, and waits for GPU
    /// completion. Must run before <see cref="ReadPixels"/>.
    /// </summary>
    /// <exception cref="SkiaInteropException">The native call failed.</exception>
    public void Flush(SkContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        NativeCallTrace.Enter("ts_flush_and_submit", handle, $"ctx=0x{context.Handle:x}");
        var status = NativeMethods.ts_flush_and_submit(context.Handle, handle);
        NativeCallTrace.Exit("ts_flush_and_submit", handle);
        SkiaInteropException.ThrowIfNotOk(status, nameof(NativeMethods.ts_flush_and_submit));
    }

    /// <summary>
    /// Copies the surface contents into a freshly-allocated tightly-packed
    /// RGBA8888 buffer of length <c>width * height * 4</c>.
    /// </summary>
    /// <remarks>
    /// ABI NOTE: <c>ts_read_pixels</c> takes the <see cref="SkContext"/> as well
    /// as the surface — Graphite has no synchronous <c>SkSurface::readPixels</c>,
    /// so readback goes through the Graphite <c>Context</c>.
    /// </remarks>
    /// <exception cref="SkiaInteropException">The native call failed.</exception>
    public unsafe byte[] ReadPixels(SkContext context, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(context);

        var pixels = new byte[checked(width * height * 4)];
        TsStatus status;
        NativeCallTrace.Enter("ts_read_pixels", handle, $"ctx=0x{context.Handle:x} len={pixels.Length}");
        fixed (byte* pixelPtr = pixels)
        {
            status = NativeMethods.ts_read_pixels(
                context.Handle, handle, pixelPtr, (nuint)pixels.Length);
        }
        NativeCallTrace.Exit("ts_read_pixels", handle);

        SkiaInteropException.ThrowIfNotOk(status, nameof(NativeMethods.ts_read_pixels));
        return pixels;
    }

    protected override bool ReleaseHandle()
    {
        NativeCallTrace.Enter("ts_surface_destroy", handle);
        NativeMethods.ts_surface_destroy(handle);
        NativeCallTrace.Exit("ts_surface_destroy", handle);
        return true;
    }
}
