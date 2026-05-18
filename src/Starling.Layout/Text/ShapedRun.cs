using System.Runtime.InteropServices;

namespace Tessera.Layout.Text;

/// <summary>
/// One shaped glyph in a run: a glyph id and its pen position relative to the
/// run origin (0,0). The memory layout matches the native shim's TsGlyph
/// (sequential uint+float+float) so a <see cref="ShapedRun"/>'s buffer can be
/// reinterpreted as TsGlyph[] at the paint/interop boundary without copying.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct ShapedGlyph(uint GlyphId, float X, float Y);

/// <summary>
/// A pre-shaped text run. Produced by <see cref="ITextMeasurer.Shape"/> at
/// layout time and carried unchanged through the display list to the paint
/// backend, which draws the glyphs directly without re-shaping. This is the
/// Blink/WebRender pattern — shape once, store the result on the layout tree,
/// reuse at paint.
/// </summary>
/// <param name="Glyphs">
/// The shaped glyphs, with pen positions relative to (0, 0) along the run's
/// baseline. The paint backend translates them by the fragment's (X, Y).
/// </param>
/// <param name="Advance">
/// The post-run pen advance — the total width of the shaped text. Replaces
/// the sentinel-reshape trick that <see cref="ITextMeasurer.MeasureWidth"/>
/// used to recover.
/// </param>
public sealed record ShapedRun(ShapedGlyph[] Glyphs, double Advance);
