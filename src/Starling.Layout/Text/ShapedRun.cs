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
public sealed record ShapedRun(ShapedGlyph[] Glyphs, double Advance)
{
    /// <summary>
    /// Carve a sub-run out of an already-shaped run. Used by inline layout to
    /// shape a whole text run once and then split the result per-word for the
    /// existing per-fragment data structures (hit-testing, alignment, line
    /// wrapping). Pen positions in the returned run start at 0, so the slice
    /// can drop into a <see cref="Box.TextFragment"/> exactly as if it had
    /// been shaped on its own.
    /// <para>
    /// Assumes a 1:1 glyph-to-character mapping. The caller is responsible for
    /// detecting non-1:1 cases (combining marks, surrogate pairs, ligatures,
    /// complex script reshaping) and falling back to a per-slice shape call —
    /// the simplest check is <c>Glyphs.Length == text.Length</c> on the source.
    /// </para>
    /// </summary>
    /// <param name="startGlyph">Inclusive start glyph index into <see cref="Glyphs"/>.</param>
    /// <param name="endGlyph">Exclusive end glyph index. <c>endGlyph == Glyphs.Length</c>
    /// means "to the end of the run"; the slice's advance then uses this run's
    /// <see cref="Advance"/> as the trailing pen-X.</param>
    public ShapedRun Slice(int startGlyph, int endGlyph)
    {
        if (startGlyph < 0 || endGlyph > Glyphs.Length || startGlyph > endGlyph)
            throw new ArgumentOutOfRangeException(nameof(startGlyph));

        if (startGlyph == endGlyph)
            return new ShapedRun(Array.Empty<ShapedGlyph>(), 0d);

        var startX = Glyphs[startGlyph].X;
        // The pen-X just past the slice's last glyph: either the next glyph's
        // origin (intermediate slice) or the whole run's total advance (slice
        // touches the run's tail). Both are computed without a re-shape.
        var endX = endGlyph < Glyphs.Length ? Glyphs[endGlyph].X : (float)Advance;

        var sliced = new ShapedGlyph[endGlyph - startGlyph];
        for (var i = 0; i < sliced.Length; i++)
        {
            var g = Glyphs[startGlyph + i];
            sliced[i] = new ShapedGlyph(g.GlyphId, g.X - startX, g.Y);
        }
        return new ShapedRun(sliced, endX - startX);
    }
}
