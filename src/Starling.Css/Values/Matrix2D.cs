using System.Globalization;

namespace Tessera.Css.Values;

/// <summary>
/// 2D affine matrix in the CSS Transforms 1 §6.1 form
/// <c>[a c e ; b d f ; 0 0 1]</c>, where the column vectors
/// <c>(a,b)</c> and <c>(c,d)</c> are the x/y basis and <c>(e,f)</c> the
/// translation. Composition is post-multiply (a <c>×</c> b applies b first,
/// then a — matching CSS where <c>transform: A B</c> applies B first per
/// §6.1).
/// </summary>
public readonly record struct Matrix2D(double A, double B, double C, double D, double E, double F)
{
    public static Matrix2D Identity { get; } = new(1, 0, 0, 1, 0, 0);

    public bool IsIdentity => A == 1 && B == 0 && C == 0 && D == 1 && E == 0 && F == 0;

    /// <summary>Returns <c>this × other</c> — i.e., <paramref name="other"/> applies first.</summary>
    public Matrix2D Multiply(Matrix2D other) => new(
        A: A * other.A + C * other.B,
        B: B * other.A + D * other.B,
        C: A * other.C + C * other.D,
        D: B * other.C + D * other.D,
        E: A * other.E + C * other.F + E,
        F: B * other.E + D * other.F + F);

    public static Matrix2D Translate(double tx, double ty) => new(1, 0, 0, 1, tx, ty);

    public static Matrix2D Scale(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);

    public static Matrix2D Rotate(double angleRadians)
    {
        var cos = Math.Cos(angleRadians);
        var sin = Math.Sin(angleRadians);
        return new(cos, sin, -sin, cos, 0, 0);
    }

    /// <summary>Skew by independent x/y angles (radians). <c>skewX(a)</c> alone uses ySkew=0; <c>skewY(b)</c> alone uses xSkew=0.</summary>
    public static Matrix2D Skew(double xRadians, double yRadians)
        => new(1, Math.Tan(yRadians), Math.Tan(xRadians), 1, 0, 0);

    /// <summary>Apply the matrix to point <paramref name="x"/>,<paramref name="y"/>.</summary>
    public (double X, double Y) Transform(double x, double y)
        => (A * x + C * y + E, B * x + D * y + F);

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture,
            $"matrix({A}, {B}, {C}, {D}, {E}, {F})");
}
