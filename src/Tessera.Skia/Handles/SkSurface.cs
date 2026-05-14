using Microsoft.Win32.SafeHandles;
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

        var status = NativeMethods.ts_surface_create(context.Handle, width, height, out nint handle);
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
        var status = NativeMethods.ts_surface_get_canvas(handle, out nint canvas);
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

        var status = NativeMethods.ts_flush_and_submit(context.Handle, handle);
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
        fixed (byte* pixelPtr = pixels)
        {
            status = NativeMethods.ts_read_pixels(
                context.Handle, handle, pixelPtr, (nuint)pixels.Length);
        }

        SkiaInteropException.ThrowIfNotOk(status, nameof(NativeMethods.ts_read_pixels));
        return pixels;
    }

    protected override bool ReleaseHandle()
    {
        NativeMethods.ts_surface_destroy(handle);
        return true;
    }
}
