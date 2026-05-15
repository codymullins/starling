namespace Tessera.Gui.Theme;

/// <summary>
/// Holds the active theme / density / type selection and the resolved token
/// sets, and raises <see cref="Changed"/> when any of them flips. Registered as
/// a DI singleton; chrome and devtools widgets read <see cref="Tokens"/> /
/// <see cref="Metrics"/> / <see cref="ChromeFont"/> at build time, and the
/// composition root (<c>MainPage</c>) rebuilds its tree on <see cref="Changed"/>.
///
/// This is the C# analogue of the <c>[data-theme]</c> / <c>[data-density]</c> /
/// <c>[data-type]</c> attributes on the <c>.tessera</c> root in the design
/// canvas. A full rebuild (rather than per-property <c>DynamicResource</c>
/// plumbing) is used because density and type tokens reach into layout-shaping
/// values — heights, paddings, radii, font families — that are simplest to
/// re-evaluate wholesale. The rebuild is instant at this scale.
/// </summary>
public sealed class ThemeManager
{
    public ThemeMode Theme { get; private set; } = ThemeMode.Dark;
    public DensityMode Density { get; private set; } = DensityMode.Comfy;
    public TypeMode Type { get; private set; } = TypeMode.Sans;

    public ThemeTokens Tokens { get; private set; } = ThemeTokens.Dark;
    public DensityTokens Metrics { get; private set; } = DensityTokens.Comfy;

    /// <summary>Font family the chrome renders in — swapped by <see cref="TypeMode"/>.</summary>
    public string ChromeFont => Type == TypeMode.Mono ? Fonts.Mono : Fonts.Sans;

    /// <summary>Monospace family — always Geist Mono regardless of type mode.</summary>
    public string MonoFont => Fonts.Mono;

    /// <summary>Raised after any of theme / density / type changes.</summary>
    public event EventHandler? Changed;

    public void SetTheme(ThemeMode mode)
    {
        if (Theme == mode) return;
        Theme = mode;
        Tokens = ThemeTokens.For(mode);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetDensity(DensityMode mode)
    {
        if (Density == mode) return;
        Density = mode;
        Metrics = DensityTokens.For(mode);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetType(TypeMode mode)
    {
        if (Type == mode) return;
        Type = mode;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Font-family names. The design specifies Geist / Geist Mono, but those
/// <c>.ttf</c> files aren't bundled yet — until they land in
/// <c>Resources/Fonts/</c> and <c>ConfigureFonts</c> registers them, these fall
/// back to system families so the chrome stays legible and, crucially, the
/// monospace columns stay genuinely monospace.
/// </summary>
public static class Fonts
{
    // Empty → the platform default sans (San Francisco on Mac Catalyst).
    public const string Sans = "";
    // A system monospace that resolves by name on macOS / Mac Catalyst.
    public const string Mono = "Menlo";
}
