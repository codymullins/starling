using System.Runtime.InteropServices;

namespace Tessera.Skia.Interop;

/// <summary>
/// Status codes returned by every fallible <c>tessera_skia</c> shim call.
/// Mirrors <c>TsStatus</c> in <c>native/shim/tessera_skia.h</c>.
/// </summary>
internal enum TsStatus
{
    Ok = 0,
    NotImplemented = 1,
    InvalidArgument = 2,
    NullHandle = 3,
    DeviceLost = 4,
    BackendUnavailable = 5,
    AllocationFailed = 6,
    ReadbackFailed = 7,
    ShapingFailed = 8,
    NotFound = 9,
    UnknownError = 100,
}

/// <summary>
/// Backend selection hint. Dawn auto-selects per platform; the non-AUTO values
/// only force a specific backend for debugging. Mirrors <c>TsBackendHint</c>.
/// </summary>
internal enum TsBackendHint
{
    Auto = 0,
    Metal = 1,
    D3D12 = 2,
    Vulkan = 3,
    GlAngle = 4,
}

/// <summary>sRGBA, 8 bits per channel, components 0-255. Mirrors <c>TsColor</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TsColor
{
    public byte R;
    public byte G;
    public byte B;
    public byte A;

    public TsColor(byte r, byte g, byte b, byte a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }
}

/// <summary>Axis-aligned rectangle in surface pixels. Mirrors <c>TsRect</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TsRect
{
    public float X;
    public float Y;
    public float Width;
    public float Height;

    public TsRect(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }
}

/// <summary>
/// One shaped glyph in a run: a glyph id plus its pen position. Produced by
/// <c>ts_shape_text</c>, consumed by <c>ts_canvas_draw_text</c>. Mirrors
/// <c>TsGlyph</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TsGlyph
{
    public uint GlyphId;
    public float X;
    public float Y;
}

/// <summary>Font metrics for a sized font, in pixels. Mirrors <c>TsFontMetrics</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TsFontMetrics
{
    public float Ascent;
    public float Descent;
    public float Leading;
    public float CapHeight;
    public float XHeight;
    public float UnderlinePosition;
    public float UnderlineThickness;
}

/// <summary>
/// One OpenType variation-axis setting. The tag is the big-endian packing
/// of the four ASCII bytes of the axis identifier (e.g. 'w','g','h','t' →
/// <c>0x77676874</c>). Mirrors <c>TsFontVariation</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TsFontVariation
{
    public uint Tag;
    public float Value;

    public static uint PackTag(string fourCc)
    {
        if (fourCc.Length != 4)
            throw new ArgumentException("axis tag must be 4 ASCII characters", nameof(fourCc));
        return ((uint)(byte)fourCc[0] << 24) | ((uint)(byte)fourCc[1] << 16) |
               ((uint)(byte)fourCc[2] << 8) | (byte)fourCc[3];
    }
}

/// <summary>
/// Source-generated P/Invoke bindings to the <c>tessera_skia</c> native shim.
/// One <see cref="LibraryImportAttribute"/> partial method per <c>ts_</c>
/// function in <c>native/shim/tessera_skia.h</c> — signatures mirror the C ABI
/// exactly (opaque handles as <see cref="nint"/>, POD structs by value).
/// </summary>
/// <remarks>
/// This is one of the two engine projects allowed <c>LibraryImport</c> (see
/// <c>Tessera.Skia.csproj</c>). The library name <c>"tessera_skia"</c> resolves
/// to <c>libtessera_skia.dylib</c> / <c>tessera_skia.dll</c> /
/// <c>libtessera_skia.so</c> via the default loader, with the repo-root
/// <c>runtimes</c> fallback installed by <see cref="NativeLoader"/>.
/// </remarks>
internal static partial class NativeMethods
{
    private const string Library = "tessera_skia";

    // --- Context / device lifecycle -------------------------------------

    [LibraryImport(Library)]
    internal static partial TsStatus ts_context_create(TsBackendHint hint, out nint outContext);

    [LibraryImport(Library)]
    internal static partial void ts_context_destroy(nint context);

    [LibraryImport(Library)]
    internal static partial nuint ts_context_backend_name(nint context, nint buffer, nuint bufferLen);

    // --- Surface + canvas -----------------------------------------------

    [LibraryImport(Library)]
    internal static partial TsStatus ts_surface_create(nint context, int width, int height, out nint outSurface);

    [LibraryImport(Library)]
    internal static partial void ts_surface_destroy(nint surface);

    [LibraryImport(Library)]
    internal static partial TsStatus ts_surface_get_canvas(nint surface, out nint outCanvas);

    [LibraryImport(Library)]
    internal static partial TsStatus ts_canvas_clear(nint canvas, TsColor color);

    [LibraryImport(Library)]
    internal static partial TsStatus ts_canvas_scale(nint canvas, float sx, float sy);

    // --- The 4 DisplayItem ops ------------------------------------------

    [LibraryImport(Library)]
    internal static partial TsStatus ts_canvas_fill_rect(nint canvas, TsRect rect, TsColor color);

    [LibraryImport(Library)]
    internal static partial TsStatus ts_canvas_stroke_rect(nint canvas, TsRect rect, TsColor color, float strokeWidth);

    [LibraryImport(Library)]
    internal static unsafe partial TsStatus ts_canvas_draw_text(
        nint canvas, nint font, TsGlyph* glyphs, nuint glyphCount, TsColor color);

    [LibraryImport(Library)]
    internal static unsafe partial TsStatus ts_canvas_draw_image(
        nint canvas, byte* pixels, int width, int height, TsRect dstRect);

    // --- Fonts + text shaping -------------------------------------------

    [LibraryImport(Library)]
    internal static unsafe partial TsStatus ts_typeface_from_data(
        byte* ttfBytes, nuint ttfLen, out nint outTypeface);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial TsStatus ts_typeface_from_name(string familyName, out nint outTypeface);

    [LibraryImport(Library)]
    internal static partial void ts_typeface_destroy(nint typeface);

    [LibraryImport(Library)]
    internal static unsafe partial TsStatus ts_typeface_clone_variations(
        nint baseTypeface, TsFontVariation* variations, nuint variationCount, out nint outTypeface);

    [LibraryImport(Library)]
    internal static partial TsStatus ts_font_create(nint typeface, float sizePx, out nint outFont);

    [LibraryImport(Library)]
    internal static partial TsStatus ts_font_create_styled(
        nint typeface, float sizePx, int embolden, int oblique, out nint outFont);

    [LibraryImport(Library)]
    internal static partial void ts_font_destroy(nint font);

    [LibraryImport(Library)]
    internal static partial TsStatus ts_font_metrics(nint font, out TsFontMetrics outMetrics);

    [LibraryImport(Library)]
    internal static unsafe partial TsStatus ts_shape_text(
        nint font, byte* utf8Text, nuint utf8Len, TsGlyph* glyphs, nuint glyphCapacity, out nuint outGlyphCount);

    // --- Flush + readback -----------------------------------------------

    [LibraryImport(Library)]
    internal static partial TsStatus ts_flush_and_submit(nint context, nint surface);

    [LibraryImport(Library)]
    internal static unsafe partial TsStatus ts_read_pixels(
        nint context, nint surface, byte* outPixels, nuint outPixelsLen);
}
