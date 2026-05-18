using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;

namespace Starling.Gui.Avalonia.Chrome;

/// <summary>
/// The chrome icon set — Avalonia port of Starling.Gui's Chrome/Icons.cs. Each
/// entry is an SVG path string on a 16×16 grid, stroked at 1.5px;
/// <see cref="Make"/> wraps one in an Avalonia <see cref="AvaloniaPath"/>.
/// </summary>
public static class Icons
{
    public const string Back = "M9.5 3.5 5 8l4.5 4.5";
    public const string Fwd = "M6.5 3.5 11 8l-4.5 4.5";
    public const string Reload = "M13 8a5 5 0 1 1-1.5-3.5M13 3v2h-2";
    public const string Stop = "M4.5 4.5h7v7h-7z";
    public const string Go = "M3 8h10M9 4l4 4-4 4";
    public const string Find = "M7 12a5 5 0 1 0 0-10 5 5 0 0 0 0 10ZM14 14l-3.2-3.2";
    public const string Enter = "M13 4v3a2 2 0 0 1-2 2H3M6 12 3 9l3-3";
    public const string Add = "M8 3v10M3 8h10";
    public const string Close = "M3.5 3.5l9 9M12.5 3.5l-9 9";
    public const string Lock = "M4.5 7V5.5a3.5 3.5 0 0 1 7 0V7M3.5 7h9v6h-9z";
    public const string Shield = "M8 1.5 13 3v5c0 3-2.5 5.5-5 6.5C5.5 13.5 3 11 3 8V3l5-1.5Z";
    public const string Inspect = "M3 3v6l2.5-1L7 11l1.5-.5L7 7l3-1Z";
    public const string Console = "M2 4h12v8H2zM4.5 7l2 1.5-2 1.5M8 10h3.5";
    public const string Bug = "M5 5.5V4a3 3 0 0 1 6 0v1.5M5 5.5h6v4a3 3 0 0 1-6 0v-4ZM3 7h2M11 7h2M3 11h2M11 11h2M8 5.5v8";
    public const string Spark = "M2 12 5 7l2.5 3L11 4l3 8";
    public const string Cpu = "M4 4h8v8H4zM6 6h4v4H6zM6 2v2M10 2v2M6 12v2M10 12v2M2 6h2M2 10h2M12 6h2M12 10h2";
    public const string Layers = "M8 2 2 5l6 3 6-3-6-3ZM2 8l6 3 6-3M2 11l6 3 6-3";
    public const string Star = "M8 2l1.8 3.7 4 .6-2.9 2.9.7 4L8 11.3l-3.6 1.9.7-4L2.2 6.3l4-.6L8 2Z";
    public const string PanelB = "M2 3h12v10H2zM2 9h12";
    public const string PanelR = "M2 3h12v10H2zM10 3v10";
    public const string Detach = "M3 3h6v6H3zM7 7h6v6H7z";
    public const string More = "M4 8a.7.7 0 1 1-1.4 0 .7.7 0 0 1 1.4 0ZM8.7 8a.7.7 0 1 1-1.4 0 .7.7 0 0 1 1.4 0ZM13.4 8a.7.7 0 1 1-1.4 0 .7.7 0 0 1 1.4 0Z";
    public const string TriRight = "M6 4l4 4-4 4";
    public const string TriDown = "M4 6l4 4 4-4";
    public const string Rec = "M8 4a4 4 0 1 0 0 8 4 4 0 0 0 0-8Z";
    public const string Cmd = "M5 5a1.5 1.5 0 1 0 1.5 1.5V5H5ZM11 5a1.5 1.5 0 1 1-1.5 1.5V5H11ZM5 11a1.5 1.5 0 1 1 1.5-1.5V11H5ZM11 11a1.5 1.5 0 1 0-1.5-1.5V11H11ZM6.5 6.5h3v3h-3z";
    public const string Sun = "M8 5.25a2.75 2.75 0 1 0 0 5.5 2.75 2.75 0 0 0 0-5.5ZM8 1.5v1.5M8 13v1.5M14.5 8H13M3 8H1.5M12.6 3.4l-1.1 1.1M4.5 11.5l-1.1 1.1M12.6 12.6l-1.1-1.1M4.5 4.5l-1.1-1.1";
    public const string Moon = "M13.25 9.5A5 5 0 1 1 6.5 2.75a4 4 0 0 0 6.75 6.75Z";

    /// <summary>
    /// Builds a stroked <see cref="AvaloniaPath"/> from a 16×16 path string.
    /// Stroke thickness scales with <paramref name="size"/> so the visual
    /// weight matches the design at any size.
    /// </summary>
    public static AvaloniaPath Make(string data, Color stroke, double size = 16, double strokeWidth = 1.5)
    {
        return new AvaloniaPath
        {
            Data = Geometry.Parse(data),
            Stroke = new SolidColorBrush(stroke),
            Fill = null,
            StrokeThickness = strokeWidth * size / 16.0,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Stretch = Stretch.Uniform,
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }
}
