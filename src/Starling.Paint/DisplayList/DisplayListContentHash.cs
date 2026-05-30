using Starling.Css.Values;
using Starling.Layout;

namespace Starling.Paint.DisplayList;

/// <summary>
/// Computes a stable 64-bit FNV-1a hash over a display-list slice's
/// visually-meaningful content — bounds, colors, text + font identity, image
/// refs, gradients, and the transforms/clips inside the slice. This is the
/// per-layer picture-cache key (plan LTF-02): two consecutive composites whose
/// only difference is the layer's composite-time transform/opacity (suppressed
/// from the slice, applied at composite) hash identically and serve from cache,
/// while a content change (color/text/size) produces a different hash and
/// re-rasters exactly that layer.
/// </summary>
/// <remarks>
/// 64-bit width keeps a stale-pixel collision astronomically unlikely. The walk
/// folds the exact field values that drive the raster, so a false HIT would
/// require two genuinely different slices to collide — not merely two equal
/// slices to hash equal (which they always do, the property we rely on). Any
/// field omission only ever risks a false MISS (a needless re-raster, safe),
/// never a false HIT, so unknown future item kinds fold their discriminator and
/// degrade to re-rastering rather than reusing stale pixels.
/// </remarks>
internal static class DisplayListContentHash
{
    // Use the standard constants for the  64-bit FNV-1a hash.
    private const ulong FnvOffset = 14695981039346656037UL; // initial hash value / offset basis
    private const ulong FnvPrime = 1099511628211UL;

    public static long Compute(DisplayList list)
    {
        var h = FnvOffset;
        var items = list.Items;
        for (var i = 0; i < items.Count; i++)
        {
            HashItem(ref h, items[i]);
        }

        return unchecked((long)h);
    }

    private static void HashItem(ref ulong h, DisplayItem item)
    {
        switch (item)
        {
            case FillRect f:
                Tag(ref h, 1);
                HashRect(ref h, f.Bounds);
                HashColor(ref h, f.Color);
                HashInt(ref h, (int)f.PixelAlignment);
                break;
            case StrokeRect s:
                Tag(ref h, 2);
                HashRect(ref h, s.Bounds);
                HashColor(ref h, s.Color);
                HashDouble(ref h, s.Width);
                break;
            case DrawText t:
                Tag(ref h, 3);
                HashString(ref h, t.Text);
                HashDouble(ref h, t.X);
                HashDouble(ref h, t.Y);
                HashDouble(ref h, t.FontSize);
                HashColor(ref h, t.Color);
                HashFonts(ref h, t.FontFamilies);
                HashBool(ref h, t.Bold);
                HashBool(ref h, t.Italic);
                HashDouble(ref h, t.Shaped?.Advance ?? 0d);
                break;
            case DrawImage i:
                Tag(ref h, 4);
                HashRect(ref h, i.Bounds);
                // Reference identity of the decoded pixels: a re-decode yields a
                // fresh object → different hash → correct re-raster. Same decoded
                // image reused → same hash → cache hit.
                HashInt(ref h, System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(i.Source));
                if (i.SourceRect is { } sr) { HashRect(ref h, sr); }
                break;
            case PushTransform p:
                Tag(ref h, 5);
                HashMatrix(ref h, p.Matrix);
                break;
            case PopTransform:
                Tag(ref h, 6);
                break;
            case PushClip pc:
                Tag(ref h, 7);
                HashRect(ref h, pc.Bounds);
                break;
            case PopClip:
                Tag(ref h, 8);
                break;
            case FillGradient g:
                Tag(ref h, 9);
                HashRect(ref h, g.Bounds);
                // Gradient values are rebuilt fresh each frame, so a structural
                // change makes a new object that hashes differently; an unchanged
                // gradient on a rebuilt list simply re-rasters (safe).
                HashInt(ref h, g.Gradient.GetHashCode());
                break;
            case FillRoundedRect rf:
                Tag(ref h, 10);
                HashRect(ref h, rf.Bounds);
                HashRadii(ref h, rf.Radii);
                HashColor(ref h, rf.Color);
                break;
            case StrokeRoundedRect rs:
                Tag(ref h, 11);
                HashRect(ref h, rs.Bounds);
                HashRadii(ref h, rs.Radii);
                HashColor(ref h, rs.Color);
                HashDouble(ref h, rs.Width);
                break;
            case DrawBoxShadow sh:
                Tag(ref h, 12);
                HashRect(ref h, sh.Bounds);
                HashRadii(ref h, sh.Radii);
                HashDouble(ref h, sh.OffsetX);
                HashDouble(ref h, sh.OffsetY);
                HashDouble(ref h, sh.Blur);
                HashDouble(ref h, sh.Spread);
                HashColor(ref h, sh.Color);
                HashBool(ref h, sh.Inset);
                break;
            case DrawTextDecoration d:
                Tag(ref h, 13);
                HashDouble(ref h, d.X);
                HashDouble(ref h, d.Width);
                HashDouble(ref h, d.BaselineY);
                HashDouble(ref h, d.FontSize);
                HashColor(ref h, d.Color);
                HashInt(ref h, (int)d.Lines);
                HashInt(ref h, (int)d.Style);
                HashDouble(ref h, d.Thickness);
                HashDouble(ref h, d.UnderlineOffset);
                HashFonts(ref h, d.FontFamilies);
                HashBool(ref h, d.Bold);
                HashBool(ref h, d.Italic);
                break;
            case DrawTextShadow s:
                Tag(ref h, 14);
                HashString(ref h, s.Text);
                HashDouble(ref h, s.X);
                HashDouble(ref h, s.Y);
                HashDouble(ref h, s.FontSize);
                HashColor(ref h, s.Color);
                HashDouble(ref h, s.OffsetX);
                HashDouble(ref h, s.OffsetY);
                HashDouble(ref h, s.Blur);
                HashFonts(ref h, s.FontFamilies);
                HashBool(ref h, s.Bold);
                HashBool(ref h, s.Italic);
                break;
            default:
                // Unknown item kind: fold a distinct discriminator so its mere
                // presence changes the hash. Worst case is a needless re-raster.
                Tag(ref h, 255);
                break;
        }
    }

    private static void Tag(ref ulong h, byte discriminator) => HashByte(ref h, discriminator);

    private static void HashByte(ref ulong h, byte b)
    {
        h ^= b;
        h *= FnvPrime;
    }

    private static void HashInt(ref ulong h, int v)
    {
        HashByte(ref h, (byte)v);
        HashByte(ref h, (byte)(v >> 8));
        HashByte(ref h, (byte)(v >> 16));
        HashByte(ref h, (byte)(v >> 24));
    }

    private static void HashLong(ref ulong h, long v)
    {
        for (var i = 0; i < 8; i++)
            HashByte(ref h, (byte)(v >> (i * 8)));
    }

    private static void HashDouble(ref ulong h, double d)
        => HashLong(ref h, BitConverter.DoubleToInt64Bits(d));

    private static void HashBool(ref ulong h, bool b) => HashByte(ref h, b ? (byte)1 : (byte)0);

    private static void HashString(ref ulong h, string s)
    {
        HashInt(ref h, s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            HashByte(ref h, (byte)c);
            HashByte(ref h, (byte)(c >> 8));
        }
    }

    private static void HashFonts(ref ulong h, IReadOnlyList<string> families)
    {
        HashInt(ref h, families.Count);
        for (var i = 0; i < families.Count; i++)
            HashString(ref h, families[i]);
    }

    private static void HashRect(ref ulong h, Rect r)
    {
        HashDouble(ref h, r.X);
        HashDouble(ref h, r.Y);
        HashDouble(ref h, r.Width);
        HashDouble(ref h, r.Height);
    }

    private static void HashColor(ref ulong h, CssColor c)
    {
        HashByte(ref h, c.R);
        HashByte(ref h, c.G);
        HashByte(ref h, c.B);
        HashByte(ref h, c.A);
    }

    private static void HashMatrix(ref ulong h, Matrix2D m)
    {
        HashDouble(ref h, m.A);
        HashDouble(ref h, m.B);
        HashDouble(ref h, m.C);
        HashDouble(ref h, m.D);
        HashDouble(ref h, m.E);
        HashDouble(ref h, m.F);
    }

    private static void HashRadii(ref ulong h, CornerRadii radii)
    {
        HashDouble(ref h, radii.TopLeftX);
        HashDouble(ref h, radii.TopLeftY);
        HashDouble(ref h, radii.TopRightX);
        HashDouble(ref h, radii.TopRightY);
        HashDouble(ref h, radii.BottomRightX);
        HashDouble(ref h, radii.BottomRightY);
        HashDouble(ref h, radii.BottomLeftX);
        HashDouble(ref h, radii.BottomLeftY);
    }
}
