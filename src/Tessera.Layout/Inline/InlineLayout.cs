using Tessera.Css.Cascade;
using Tessera.Css.Properties;
using Tessera.Css.Values;
using Tessera.Layout.Box;
using Tessera.Layout.Text;

namespace Tessera.Layout.Inline;

/// <summary>
/// Lays out an anonymous block's inline children into line boxes. Produces
/// <see cref="TextFragment"/> entries on each <see cref="TextBox"/> describing
/// where each piece appears on its parent's line.
/// </summary>
internal sealed class InlineLayout
{
    private readonly ITextMeasurer _measurer;

    public InlineLayout(ITextMeasurer measurer)
    {
        _measurer = measurer;
    }

    public double Layout(Box.Box container, double availableWidth)
    {
        var fontSize = ResolveFontSize(container.Style);
        var lineHeight = ResolveLineHeight(container.Style, fontSize);
        var baseline = _measurer.Baseline(fontSize);

        // Collect a sequence of (text, style) runs by flattening inline + text children.
        var runs = new List<(string Text, ComputedStyle? Style, TextBox Owner)>();
        Flatten(container, runs);

        // No content → zero height.
        if (runs.Count == 0) return 0;

        double cursorX = 0, cursorY = 0;
        double currentLineHeight = lineHeight;

        foreach (var (text, style, owner) in runs)
        {
            // Normalize whitespace: collapse runs of whitespace to a single space.
            var normalized = NormalizeWhitespace(text);
            if (normalized.Length == 0) continue;

            var localFontSize = ResolveFontSize(style ?? container.Style);
            var localLineHeight = ResolveLineHeight(style ?? container.Style, localFontSize);
            currentLineHeight = Math.Max(currentLineHeight, localLineHeight);

            var words = SplitToWords(normalized);
            foreach (var word in words)
            {
                if (word.Length == 0) continue;
                var width = _measurer.MeasureWidth(word, localFontSize);

                // Wrap if needed (only after at least one fragment on the line).
                var leadingSpace = word.StartsWith(" ", StringComparison.Ordinal);
                if (cursorX > 0 && cursorX + width > availableWidth)
                {
                    cursorY += currentLineHeight;
                    cursorX = 0;
                    currentLineHeight = localLineHeight;
                    if (leadingSpace)
                    {
                        // Strip the leading space when wrapping; we don't carry it to a new line.
                        var trimmed = word.TrimStart(' ');
                        if (trimmed.Length == 0) continue;
                        var trimmedWidth = _measurer.MeasureWidth(trimmed, localFontSize);
                        owner.Fragments.Add(new TextFragment(trimmed, cursorX, cursorY, trimmedWidth, currentLineHeight, baseline));
                        cursorX += trimmedWidth;
                        continue;
                    }
                }

                owner.Fragments.Add(new TextFragment(word, cursorX, cursorY, width, currentLineHeight, _measurer.Baseline(localFontSize)));
                cursorX += width;
            }
        }

        return cursorY + currentLineHeight;
    }

    private static void Flatten(Box.Box box, List<(string Text, ComputedStyle? Style, TextBox Owner)> runs)
    {
        foreach (var child in box.Children)
        {
            switch (child)
            {
                case TextBox tb:
                    tb.Fragments.Clear();
                    runs.Add((tb.Text, tb.Style, tb));
                    break;
                case InlineBox ib:
                    Flatten(ib, runs);
                    break;
                default:
                    // Block-in-inline would land here; in v1 we just walk it as a sub-tree.
                    Flatten(child, runs);
                    break;
            }
        }
    }

    private static string NormalizeWhitespace(string text)
    {
        if (text.Length == 0) return text;
        var sb = new System.Text.StringBuilder(text.Length);
        var prevSpace = false;
        foreach (var c in text)
        {
            if (c is ' ' or '\t' or '\n' or '\r' or '\f')
            {
                if (!prevSpace) sb.Append(' ');
                prevSpace = true;
            }
            else
            {
                sb.Append(c);
                prevSpace = false;
            }
        }
        return sb.ToString();
    }

    private static IEnumerable<string> SplitToWords(string text)
    {
        // Each word is one or more non-space chars, possibly preceded by a space.
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ')
            {
                if (i > start) yield return text[start..i];
                yield return " ";
                start = i + 1;
            }
        }
        if (start < text.Length) yield return text[start..];
    }

    private static double ResolveFontSize(ComputedStyle? style)
    {
        if (style is null) return 16;
        return style.Get(PropertyId.FontSize) switch
        {
            CssLength len => Block.BlockLayout.ToPx(len),
            CssNumber n => n.Value,
            _ => 16,
        };
    }

    private double ResolveLineHeight(ComputedStyle? style, double fontSize)
    {
        if (style is null) return _measurer.NormalLineHeight(fontSize);
        return style.Get(PropertyId.LineHeight) switch
        {
            CssNumber n => n.Value * fontSize,
            CssLength len => Block.BlockLayout.ToPx(len),
            CssPercentage pct => fontSize * pct.Value / 100d,
            CssKeyword k when k.Name == "normal" => _measurer.NormalLineHeight(fontSize),
            _ => _measurer.NormalLineHeight(fontSize),
        };
    }

}
