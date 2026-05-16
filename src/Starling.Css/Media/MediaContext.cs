namespace Tessera.Css.Media;

// MQ5 §3 viewport/device characteristics. Pure data — engine fills it.
public sealed record MediaContext(
    string MediaType = "screen",
    double ViewportWidthPx = 1024,
    double ViewportHeightPx = 768,
    double Resolution = 1.0,
    ColorScheme ColorScheme = ColorScheme.Light,
    ReducedMotion ReducedMotion = ReducedMotion.NoPreference,
    Contrast Contrast = Contrast.NoPreference,
    ReducedTransparency ReducedTransparency = ReducedTransparency.NoPreference,
    ReducedData ReducedData = ReducedData.NoPreference,
    Pointer Pointer = Pointer.Fine,
    Pointer AnyPointer = Pointer.Fine,
    Hover Hover = Hover.Hover,
    Hover AnyHover = Hover.Hover,
    Scripting Scripting = Scripting.Enabled,
    UpdateFrequency Update = UpdateFrequency.Fast,
    int Color = 8,
    int Monochrome = 0,
    ColorGamut ColorGamut = ColorGamut.Srgb,
    DisplayMode DisplayMode = DisplayMode.Browser,
    ForcedColors ForcedColors = ForcedColors.None,
    InvertedColors InvertedColors = InvertedColors.None)
{
    public static MediaContext Default { get; } = new();

    public Orientation Orientation
        => ViewportHeightPx >= ViewportWidthPx ? Orientation.Portrait : Orientation.Landscape;

    public double AspectRatio
        => ViewportHeightPx == 0 ? double.PositiveInfinity : ViewportWidthPx / ViewportHeightPx;
}

public enum ColorScheme { Light, Dark }
public enum ReducedMotion { NoPreference, Reduce }
public enum Contrast { NoPreference, More, Less, Custom }
public enum ReducedTransparency { NoPreference, Reduce }
public enum ReducedData { NoPreference, Reduce }
public enum Pointer { None, Coarse, Fine }
public enum Hover { None, Hover }
public enum Scripting { None, InitialOnly, Enabled }
public enum UpdateFrequency { None, Slow, Fast }
public enum Orientation { Portrait, Landscape }
public enum ColorGamut { Srgb, P3, Rec2020 }
public enum DisplayMode { Browser, Standalone, MinimalUi, Fullscreen, PictureInPicture }
public enum ForcedColors { None, Active }
public enum InvertedColors { None, Inverted }
