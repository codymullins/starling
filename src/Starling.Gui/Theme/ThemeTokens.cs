namespace Tessera.Gui.Theme;

public enum ThemeMode { Dark, Light, Contrast }
public enum DensityMode { Comfy, Compact }
public enum TypeMode { Sans, Mono }

/// <summary>
/// One theme's worth of colour tokens — the C# port of a <c>[data-theme]</c>
/// block in <c>design/theme.css</c>. Three instances (<see cref="Dark"/>,
/// <see cref="Light"/>, <see cref="Contrast"/>) are the authoritative palette;
/// every chrome and devtools widget reads its colours from here via
/// <see cref="ThemeManager"/>.
/// </summary>
public sealed record ThemeTokens
{
    // surfaces
    public required Color Bg { get; init; }
    public required Color Panel { get; init; }
    public required Color Surface { get; init; }
    public required Color Raise { get; init; }
    public required Color Hover { get; init; }
    public required Color Press { get; init; }

    // borders
    public required Color Border { get; init; }
    public required Color Hair { get; init; }
    public required Color Strong { get; init; }

    // text
    public required Color Text { get; init; }
    public required Color Text2 { get; init; }
    public required Color Muted { get; init; }
    public required Color Faint { get; init; }
    public required Color Inverse { get; init; }

    // accent
    public required Color Accent { get; init; }
    public required Color Accent2 { get; init; }
    public required Color AccentOn { get; init; }
    public required Color AccentBg { get; init; }
    public required Color AccentLine { get; init; }

    // status
    public required Color Ok { get; init; }
    public required Color Warn { get; init; }
    public required Color Err { get; init; }
    public required Color Info { get; init; }

    // category palette (flame chart, log levels, IPC channels)
    public required Color CatHtml { get; init; }
    public required Color CatCss { get; init; }
    public required Color CatJs { get; init; }
    public required Color CatLayout { get; init; }
    public required Color CatPaint { get; init; }
    public required Color CatGc { get; init; }
    public required Color CatNet { get; init; }
    public required Color CatIdle { get; init; }

    // webview body (the rendered page area — does not follow chrome theme)
    public required Color WebBg { get; init; }
    public required Color WebText { get; init; }
    public required Color WebLink { get; init; }
    public required Color WebVisited { get; init; }

    // flame-bar label ink — flips to read on the category fills
    public required Color BarInk { get; init; }

    /// <summary>Looks up a category fill by <see cref="Category"/>.</summary>
    public Color this[Category cat] => cat switch
    {
        Category.Html => CatHtml,
        Category.Css => CatCss,
        Category.Js => CatJs,
        Category.Layout => CatLayout,
        Category.Paint => CatPaint,
        Category.Gc => CatGc,
        Category.Net => CatNet,
        _ => CatIdle,
    };

    private static Color Rgba(int r, int g, int b, double a)
        => new(r / 255f, g / 255f, b / 255f, (float)a);

    private static Color Hex(string hex) => Color.FromArgb(hex);

    // ─── Dark (default) ────────────────────────────────────────────────
    public static readonly ThemeTokens Dark = new()
    {
        Bg = Hex("#0c0d10"),
        Panel = Hex("#14161b"),
        Surface = Hex("#1b1e25"),
        Raise = Hex("#232730"),
        Hover = Rgba(255, 255, 255, 0.04),
        Press = Rgba(255, 255, 255, 0.07),
        Border = Rgba(255, 255, 255, 0.06),
        Hair = Rgba(255, 255, 255, 0.10),
        Strong = Rgba(255, 255, 255, 0.18),
        Text = Hex("#e8e9ed"),
        Text2 = Hex("#b8bdc8"),
        Muted = Hex("#7f8492"),
        Faint = Hex("#525866"),
        Inverse = Hex("#0c0d10"),
        Accent = Hex("#7dd3a0"),
        Accent2 = Hex("#5fb886"),
        AccentOn = Hex("#0c0d10"),
        AccentBg = Rgba(125, 211, 160, 0.10),
        AccentLine = Rgba(125, 211, 160, 0.35),
        Ok = Hex("#7dd3a0"),
        Warn = Hex("#f5b942"),
        Err = Hex("#ef6f7a"),
        Info = Hex("#6db3ff"),
        CatHtml = Hex("#7dd3a0"),
        CatCss = Hex("#a78bfa"),
        CatJs = Hex("#f59e0b"),
        CatLayout = Hex("#60a5fa"),
        CatPaint = Hex("#f472b6"),
        CatGc = Hex("#ef6f7a"),
        CatNet = Hex("#22d3ee"),
        CatIdle = Hex("#3a3f4b"),
        WebBg = Hex("#ffffff"),
        WebText = Hex("#1a1a1a"),
        WebLink = Hex("#2a5dff"),
        WebVisited = Hex("#7c4dff"),
        BarInk = Hex("#0c0d10"),
    };

    // ─── Light ─────────────────────────────────────────────────────────
    public static readonly ThemeTokens Light = new()
    {
        Bg = Hex("#f4f3ee"),
        Panel = Hex("#fbfaf6"),
        Surface = Hex("#ffffff"),
        Raise = Hex("#ebe9e1"),
        Hover = Rgba(0, 0, 0, 0.04),
        Press = Rgba(0, 0, 0, 0.08),
        Border = Rgba(40, 32, 20, 0.10),
        Hair = Rgba(40, 32, 20, 0.14),
        Strong = Rgba(40, 32, 20, 0.30),
        Text = Hex("#1a1a17"),
        Text2 = Hex("#44423c"),
        Muted = Hex("#6e6a5e"),
        Faint = Hex("#9a9587"),
        Inverse = Hex("#ffffff"),
        Accent = Hex("#2f8a5e"),
        Accent2 = Hex("#246b48"),
        AccentOn = Hex("#ffffff"),
        AccentBg = Rgba(47, 138, 94, 0.10),
        AccentLine = Rgba(47, 138, 94, 0.35),
        Ok = Hex("#2f8a5e"),
        Warn = Hex("#b97309"),
        Err = Hex("#c54250"),
        Info = Hex("#2a5dcf"),
        CatHtml = Hex("#2f8a5e"),
        CatCss = Hex("#6a4cd0"),
        CatJs = Hex("#c47a0a"),
        CatLayout = Hex("#2a5dcf"),
        CatPaint = Hex("#c93f7c"),
        CatGc = Hex("#c54250"),
        CatNet = Hex("#0c8398"),
        CatIdle = Hex("#d4d1c5"),
        WebBg = Hex("#ffffff"),
        WebText = Hex("#1a1a1a"),
        WebLink = Hex("#2a5dff"),
        WebVisited = Hex("#7c4dff"),
        BarInk = Hex("#1a1a17"),
    };

    // ─── High contrast ─────────────────────────────────────────────────
    // theme.css only overrides a subset here; the category / status / web
    // tokens below are filled in so the contrast theme is self-complete and
    // clears the §7 contrast audit instead of inheriting dark's values.
    public static readonly ThemeTokens Contrast = new()
    {
        Bg = Hex("#000000"),
        Panel = Hex("#0a0a0a"),
        Surface = Hex("#121214"),
        Raise = Hex("#1a1a1c"),
        Hover = Rgba(255, 255, 255, 0.08),
        Press = Rgba(255, 255, 255, 0.12),
        Border = Rgba(255, 255, 255, 0.20),
        Hair = Rgba(255, 255, 255, 0.30),
        Strong = Rgba(255, 255, 255, 0.55),
        Text = Hex("#ffffff"),
        Text2 = Hex("#e8e8e8"),
        Muted = Hex("#b0b0b0"),
        Faint = Hex("#808080"),
        Inverse = Hex("#000000"),
        Accent = Hex("#a4f5c2"),
        Accent2 = Hex("#7fe0a5"),
        AccentOn = Hex("#000000"),
        AccentBg = Rgba(164, 245, 194, 0.15),
        AccentLine = Rgba(164, 245, 194, 0.55),
        Ok = Hex("#a4f5c2"),
        Warn = Hex("#ffd166"),
        Err = Hex("#ff8a94"),
        Info = Hex("#8fc7ff"),
        CatHtml = Hex("#a4f5c2"),
        CatCss = Hex("#c4b5ff"),
        CatJs = Hex("#ffc24d"),
        CatLayout = Hex("#93c5ff"),
        CatPaint = Hex("#ff9ec9"),
        CatGc = Hex("#ff8a94"),
        CatNet = Hex("#67e8f9"),
        CatIdle = Hex("#4a4f5b"),
        WebBg = Hex("#ffffff"),
        WebText = Hex("#000000"),
        WebLink = Hex("#0000ee"),
        WebVisited = Hex("#551a8b"),
        BarInk = Hex("#000000"),
    };

    public static ThemeTokens For(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => Light,
        ThemeMode.Contrast => Contrast,
        _ => Dark,
    };
}

/// <summary>
/// Spacing, radius and font-size tokens — the C# port of the density blocks in
/// <c>design/theme.css</c>. Sizes are in device-independent units.
/// </summary>
public sealed record DensityTokens
{
    public required double Row { get; init; }
    public required double RowSm { get; init; }
    public required double RowXs { get; init; }
    public required double Pad { get; init; }
    public required double PadSm { get; init; }
    public required double Gap { get; init; }
    public required double GapSm { get; init; }
    public required double R { get; init; }
    public required double RMd { get; init; }
    public required double RSm { get; init; }
    public required double RPill { get; init; }
    public required double FsXs { get; init; }
    public required double FsSm { get; init; }
    public required double FsMd { get; init; }
    public required double FsLg { get; init; }
    public required double FsXl { get; init; }

    public static readonly DensityTokens Comfy = new()
    {
        Row = 36, RowSm = 30, RowXs = 24,
        Pad = 14, PadSm = 10, Gap = 10, GapSm = 6,
        R = 12, RMd = 10, RSm = 7, RPill = 999,
        FsXs = 11, FsSm = 12, FsMd = 13, FsLg = 14, FsXl = 16,
    };

    public static readonly DensityTokens Compact = new()
    {
        Row = 28, RowSm = 24, RowXs = 20,
        Pad = 10, PadSm = 7, Gap = 7, GapSm = 4,
        R = 9, RMd = 7, RSm = 5, RPill = 999,
        FsXs = 10, FsSm = 11, FsMd = 12, FsLg = 13, FsXl = 14,
    };

    public static DensityTokens For(DensityMode mode)
        => mode == DensityMode.Compact ? Compact : Comfy;

    /// <summary>
    /// Returns a copy with every spacing / radius / font-size token multiplied
    /// by <paramref name="scale"/>. The radii's <c>RPill</c> sentinel (a "very
    /// large" 999) is preserved verbatim — scaling it would only increase
    /// rounding error against its corner-clipping semantics.
    /// </summary>
    public DensityTokens Scaled(double scale) => new()
    {
        Row = Row * scale, RowSm = RowSm * scale, RowXs = RowXs * scale,
        Pad = Pad * scale, PadSm = PadSm * scale,
        Gap = Gap * scale, GapSm = GapSm * scale,
        R = R * scale, RMd = RMd * scale, RSm = RSm * scale, RPill = RPill,
        FsXs = FsXs * scale, FsSm = FsSm * scale, FsMd = FsMd * scale,
        FsLg = FsLg * scale, FsXl = FsXl * scale,
    };
}
