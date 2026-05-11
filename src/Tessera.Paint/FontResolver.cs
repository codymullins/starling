using SixLabors.Fonts;

namespace Tessera.Paint;

/// <summary>
/// M0 font resolver. Resolves a sans-serif <see cref="Font"/> via the following
/// chain:
///   1. A font file bundled at <c>Resources/Fonts/*.ttf</c> next to the assembly.
///   2. An <c>EmbeddedResource</c> with extension <c>.ttf</c> / <c>.otf</c>.
///   3. The OS's installed sans-serif family (<see cref="SystemFonts"/>).
///   4. The first installed family (last-resort).
/// If none of those resolve, a clear exception fires.
///
/// The full <c>FontResolver</c> per 08_FONTS_PAINT.md adds @font-face, Unicode
/// fallback chains, and variable-font axis selection — those are M2+ work.
/// </summary>
public sealed class FontResolver
{
    public static readonly FontResolver Default = new();

    private readonly FontCollection _bundled = new();
    private readonly FontFamily? _bundledSansFamily;

    public FontResolver()
    {
        _bundledSansFamily = TryLoadBundled();
    }

    public Font GetSansSerifFont(float size)
    {
        if (_bundledSansFamily is { } b)
            return b.CreateFont(size, FontStyle.Regular);

        if (TryGetSystemSansSerif(out var sys))
            return sys.CreateFont(size, FontStyle.Regular);

        // Last-resort: whatever the system has. SystemFonts.Families is
        // non-empty on macOS, Windows, and most Linux desktops; if it IS empty
        // (e.g. a minimal CI container), throw a clear, actionable error.
        var first = SystemFonts.Families.FirstOrDefault();
        if (first.Name is not null)
            return first.CreateFont(size, FontStyle.Regular);

        throw new InvalidOperationException(
            "No fonts available. Either bundle a TTF/OTF under " +
            "src/Tessera.Paint/Resources/Fonts/ or install system fonts. " +
            "See browser-plan/08_FONTS_PAINT.md.");
    }

    private FontFamily? TryLoadBundled()
    {
        // Filesystem bundle (preferred — works for distribution).
        var asmDir = Path.GetDirectoryName(typeof(FontResolver).Assembly.Location);
        if (!string.IsNullOrEmpty(asmDir))
        {
            var fontsDir = Path.Combine(asmDir, "Resources", "Fonts");
            if (Directory.Exists(fontsDir))
            {
                foreach (var file in Directory.EnumerateFiles(fontsDir)
                                              .Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                                                       || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        var family = _bundled.Add(file);
                        return family;
                    }
                    catch (Exception ex) when (ex is InvalidFontFileException or IOException)
                    {
                        // Try the next file rather than crashing the engine.
                    }
                }
            }
        }

        // Embedded resource bundle (also supported, so unit tests can ship a font).
        var asm = typeof(FontResolver).Assembly;
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                continue;
            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null) continue;
            try
            {
                return _bundled.Add(stream);
            }
            catch (InvalidFontFileException)
            {
                // skip
            }
        }

        return null;
    }

    private static bool TryGetSystemSansSerif(out FontFamily family)
    {
        // Preference order — try a few canonical sans-serif faces in turn.
        string[] candidates =
        [
            "Inter",
            "Helvetica Neue",
            "Helvetica",
            "Arial",
            "Liberation Sans",
            "DejaVu Sans",
            "Segoe UI",
            "Noto Sans",
            "Verdana",
        ];

        foreach (var name in candidates)
        {
            if (SystemFonts.TryGet(name, out family))
                return true;
        }
        family = default;
        return false;
    }
}
