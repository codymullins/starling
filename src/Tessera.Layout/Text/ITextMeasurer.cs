namespace Tessera.Layout.Text;

/// <summary>
/// Layout's text-measurement seam. The paint module supplies the real
/// implementation backed by font metrics; layout keeps a coarse fallback so
/// it can produce a usable tree without a paint dependency.
/// </summary>
public interface ITextMeasurer
{
    /// <summary>Advance width in CSS px for <paramref name="text"/> at the given font size.</summary>
    double MeasureWidth(string text, double fontSize);

    /// <summary>Line-height for a font of the given size when CSS <c>line-height: normal</c> applies.</summary>
    double NormalLineHeight(double fontSize);

    /// <summary>Distance from the top of the line box to the alphabetic baseline.</summary>
    double Baseline(double fontSize);
}

/// <summary>
/// Default measurer: assumes a roughly proportional sans-serif with an average
/// glyph advance of ~0.5em. Good enough for layout to wrap text and choose
/// line counts; precise paint metrics come from the paint module.
/// </summary>
public sealed class DefaultTextMeasurer : ITextMeasurer
{
    public static readonly DefaultTextMeasurer Instance = new();

    public double MeasureWidth(string text, double fontSize)
    {
        ArgumentNullException.ThrowIfNull(text);
        double total = 0;
        foreach (var c in text)
        {
            total += AverageAdvanceFactor(c) * fontSize;
        }
        return total;
    }

    public double NormalLineHeight(double fontSize) => fontSize * 1.2;

    public double Baseline(double fontSize) => fontSize * 0.8;

    private static double AverageAdvanceFactor(char c) => c switch
    {
        ' ' or '\t' => 0.28,
        'i' or 'l' or 'I' or '|' or '!' or '.' or ',' or ';' or ':' or '\'' or '`' => 0.28,
        'm' or 'M' or 'w' or 'W' => 0.85,
        _ when c >= '0' && c <= '9' => 0.55,
        _ when char.IsUpper(c) => 0.65,
        _ when char.IsLower(c) => 0.52,
        _ => 0.5,
    };
}
