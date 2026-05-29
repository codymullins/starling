using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Animations;

/// <summary>
/// Per-property value interpolation. CSS Transitions / Animations sample
/// the from/to values at progress <c>p ∈ [0, 1]</c> per CSS Values 4 §5.
/// The interpolator dispatches on value type because the rules differ:
/// numeric and length values lerp directly, colors lerp in sRGB component
/// space, and transform lists either lerp pairwise (matching function
/// signatures) or decompose to matrices and lerp those (mismatched lists,
/// per CSS Transforms 1 §10.2).
/// </summary>
public static class Interpolator
{
    /// <summary>
    /// Returns the interpolated value of <paramref name="from"/> /
    /// <paramref name="to"/> at progress <paramref name="progress"/>. If
    /// the pair is not interpolable per the spec (mismatched value kinds,
    /// keyword endpoints other than identical), the function falls back to
    /// the discrete rule: return <paramref name="from"/> while
    /// <c>progress &lt; 0.5</c>, otherwise <paramref name="to"/>
    /// (CSS Transitions 1 §3.1).
    /// </summary>
    public static CssValue Interpolate(PropertyId property, CssValue from, CssValue to, double progress)
    {
        if (progress <= 0) return from;
        if (progress >= 1) return to;

        if (ReferenceEquals(from, to)) return from;

        // `transform` is stored in the cascade as a function value / value list
        // (e.g. rotate(0deg)), not a CssTransform, so the type switch below would
        // miss it and the value would snap at 50%. Parse both endpoints to
        // CssTransform up front (Parse is idempotent + maps `none` to the empty
        // transform) so a rotate/translate/scale animation actually tweens.
        if (property == PropertyId.Transform)
        {
            var fromTransform = from as CssTransform ?? CssTransformParser.Parse(from);
            var toTransform = to as CssTransform ?? CssTransformParser.Parse(to);
            return InterpolateTransform(fromTransform, toTransform, progress);
        }

        if (from.GetType() != to.GetType()) return Discrete(from, to, progress);

        switch (from)
        {
            case CssNumber f when to is CssNumber t:
                return new CssNumber(Lerp(f.Value, t.Value, progress));

            case CssPercentage f when to is CssPercentage t:
                return new CssPercentage(Lerp(f.Value, t.Value, progress));

            case CssLength f when to is CssLength t:
                // Same-unit fast path keeps the result in the source unit so
                // subsequent computed-value passes don't need a second
                // conversion. Different units fall through to the px
                // common-denominator branch handled below.
                if (f.Unit == t.Unit) return new CssLength(Lerp(f.Value, t.Value, progress), f.Unit);
                if (TryToPx(f, out var fpx) && TryToPx(t, out var tpx))
                    return new CssLength(Lerp(fpx, tpx, progress), CssLengthUnit.Px);
                return Discrete(from, to, progress);

            case CssTime f when to is CssTime t:
                return new CssTime(Lerp(f.InSeconds, t.InSeconds, progress), CssTimeUnit.Seconds);

            case CssAngle f when to is CssAngle t:
                // Interpolate in degrees so different angle units (turn, rad)
                // can mix; convert the result back into degrees as a stable
                // canonical unit.
                return new CssAngle(Lerp(f.InDegrees, t.InDegrees, progress), CssAngleUnit.Degrees);

            case CssColor f when to is CssColor t:
                return InterpolateColor(f, t, progress);

            case CssTransform f when to is CssTransform t:
                return InterpolateTransform(f, t, progress);
        }

        return Discrete(from, to, progress);
    }

    /// <summary>
    /// Returns true when <paramref name="property"/> is animatable per the
    /// CSS property registry. The shortlist mirrors the properties we
    /// currently interpolate; anything else falls back to discrete switch.
    /// </summary>
    public static bool IsAnimatable(PropertyId property) => property switch
    {
        PropertyId.Opacity => true,
        PropertyId.Color => true,
        PropertyId.BackgroundColor => true,
        PropertyId.BorderTopColor or PropertyId.BorderRightColor
            or PropertyId.BorderBottomColor or PropertyId.BorderLeftColor => true,
        PropertyId.Width or PropertyId.Height => true,
        PropertyId.Top or PropertyId.Right or PropertyId.Bottom or PropertyId.Left => true,
        PropertyId.MarginTop or PropertyId.MarginRight or PropertyId.MarginBottom or PropertyId.MarginLeft => true,
        PropertyId.PaddingTop or PropertyId.PaddingRight or PropertyId.PaddingBottom or PropertyId.PaddingLeft => true,
        PropertyId.BorderTopWidth or PropertyId.BorderRightWidth
            or PropertyId.BorderBottomWidth or PropertyId.BorderLeftWidth => true,
        PropertyId.FontSize or PropertyId.LineHeight or PropertyId.LetterSpacing or PropertyId.WordSpacing => true,
        PropertyId.Transform => true,
        _ => false,
    };

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static CssValue Discrete(CssValue from, CssValue to, double progress)
        => progress < 0.5 ? from : to;

    private static CssColor InterpolateColor(CssColor a, CssColor b, double p)
    {
        // Spec (CSS Color 4 §12): interpolation happens in the colour space
        // specified by `color-interpolation-method`, defaulting to sRGB.
        // Component lerp on premultiplied alpha avoids the bleed-through
        // artefact when fading to transparent.
        var aa = a.A / 255.0;
        var ba = b.A / 255.0;
        var ar = a.R * aa;
        var ag = a.G * aa;
        var ab = a.B * aa;
        var br = b.R * ba;
        var bg = b.G * ba;
        var bb = b.B * ba;
        var oa = Lerp(aa, ba, p);
        var or = Lerp(ar, br, p);
        var og = Lerp(ag, bg, p);
        var ob = Lerp(ab, bb, p);
        var outA = (byte)Math.Clamp(Math.Round(oa * 255), 0, 255);
        var outR = oa < 1e-6 ? (byte)0 : (byte)Math.Clamp(Math.Round(or / oa), 0, 255);
        var outG = oa < 1e-6 ? (byte)0 : (byte)Math.Clamp(Math.Round(og / oa), 0, 255);
        var outB = oa < 1e-6 ? (byte)0 : (byte)Math.Clamp(Math.Round(ob / oa), 0, 255);
        return new CssColor(outR, outG, outB, outA);
    }

    private static CssTransform InterpolateTransform(CssTransform a, CssTransform b, double p)
    {
        // Fast path — matching function signatures lerp pairwise per
        // CSS Transforms 1 §10.1. Bail to the matrix-decompose slow path
        // when counts or function types differ.
        var fa = a.Functions;
        var fb = b.Functions;
        if (fa.Count == fb.Count && CanLerpPairwise(fa, fb))
        {
            var result = new List<CssTransformFunction>(fa.Count);
            for (var i = 0; i < fa.Count; i++)
                result.Add(LerpFunction(fa[i], fb[i], p));
            return new CssTransform(result);
        }

        // Matrix decompose-and-lerp: compose the function lists into
        // Matrix2D and lerp each component independently. This is the
        // simplified form of CSS Transforms 1 §10.2 — full decompose into
        // translate/scale/rotate/skew components is a richer follow-up
        // (it produces nicer mid-frames for combined rotate+scale), but
        // matrix-component lerp matches Firefox's pre-2015 behaviour and is
        // visually acceptable for cross-function transitions.
        var ma = a.ToMatrix(0, 0);
        var mb = b.ToMatrix(0, 0);
        var lerped = new Matrix2D(
            Lerp(ma.A, mb.A, p),
            Lerp(ma.B, mb.B, p),
            Lerp(ma.C, mb.C, p),
            Lerp(ma.D, mb.D, p),
            Lerp(ma.E, mb.E, p),
            Lerp(ma.F, mb.F, p));
        return new CssTransform(new[] { (CssTransformFunction)new CssMatrix(lerped) });
    }

    private static bool CanLerpPairwise(IReadOnlyList<CssTransformFunction> a, IReadOnlyList<CssTransformFunction> b)
    {
        for (var i = 0; i < a.Count; i++)
            if (a[i].GetType() != b[i].GetType()) return false;
        return true;
    }

    private static CssTransformFunction LerpFunction(CssTransformFunction a, CssTransformFunction b, double p)
    {
        switch (a)
        {
            case CssTranslate ta when b is CssTranslate tb:
                return new CssTranslate(
                    LerpLengthOrPct(ta.X, tb.X, p),
                    LerpLengthOrPct(ta.Y, tb.Y, p));
            case CssScale sa when b is CssScale sb:
                return new CssScale(Lerp(sa.X, sb.X, p), Lerp(sa.Y, sb.Y, p));
            case CssRotate ra when b is CssRotate rb:
                return new CssRotate(Lerp(ra.AngleRadians, rb.AngleRadians, p));
            case CssSkew ka when b is CssSkew kb:
                return new CssSkew(Lerp(ka.XRadians, kb.XRadians, p), Lerp(ka.YRadians, kb.YRadians, p));
            case CssMatrix ma when b is CssMatrix mb:
                return new CssMatrix(new Matrix2D(
                    Lerp(ma.Value.A, mb.Value.A, p),
                    Lerp(ma.Value.B, mb.Value.B, p),
                    Lerp(ma.Value.C, mb.Value.C, p),
                    Lerp(ma.Value.D, mb.Value.D, p),
                    Lerp(ma.Value.E, mb.Value.E, p),
                    Lerp(ma.Value.F, mb.Value.F, p)));
            default:
                // CanLerpPairwise should have ruled this out, but stay safe.
                return p < 0.5 ? a : b;
        }
    }

    private static CssLengthOrPercent LerpLengthOrPct(CssLengthOrPercent a, CssLengthOrPercent b, double p)
    {
        // Mixed length-vs-percent translates can't be lerped at compute time
        // without the resolution context (we'd need the reference size to
        // resolve the percent to px). Fall back to discrete switch — matches
        // the behaviour you'd get from a calc() that the engine can't yet
        // evaluate at sample time.
        if (a.IsPercent != b.IsPercent) return p < 0.5 ? a : b;
        return new CssLengthOrPercent(Lerp(a.Value, b.Value, p), a.IsPercent);
    }

    private static bool TryToPx(CssLength l, out double px)
    {
        // Absolute units only. Anything font-relative or viewport-relative
        // needs the resolution context to land in px; without it the
        // interpolator surrenders and the caller switches to discrete.
        switch (l.Unit)
        {
            case CssLengthUnit.Px: px = l.Value; return true;
            case CssLengthUnit.Pt: px = l.Value * 4.0 / 3.0; return true;
            case CssLengthUnit.Pc: px = l.Value * 16.0; return true;
            case CssLengthUnit.In: px = l.Value * 96.0; return true;
            case CssLengthUnit.Cm: px = l.Value * 96.0 / 2.54; return true;
            case CssLengthUnit.Mm: px = l.Value * 96.0 / 25.4; return true;
            case CssLengthUnit.Q: px = l.Value * 96.0 / 25.4 / 4.0; return true;
            default: px = 0; return false;
        }
    }
}
