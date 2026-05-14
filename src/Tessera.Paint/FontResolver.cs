using Tessera.Skia.Handles;

namespace Tessera.Paint;

/// <summary>
/// Resolves the sans-serif <see cref="SkTypeface"/> for the Skia paint path —
/// the engine's sole rasterizer. Walks a 3-tier sans-serif chain:
/// <list type="number">
///   <item>The bundled <c>OpenSans-Regular.ttf</c> (filesystem bundle, then the
///   embedded resource — the embedded copy always ships inside the assembly).</item>
///   <item>An installed sans-serif family via Skia's <c>SkFontMgr</c>/CoreText.</item>
///   <item>The system font manager's generic <c>sans-serif</c>.</item>
/// </list>
/// If none resolve, a clear exception fires.
///
/// The full <c>FontResolver</c> per 08_FONTS_PAINT.md adds @font-face, Unicode
/// fallback chains, and variable-font axis selection — those are later work.
/// </summary>
public sealed class FontResolver : IDisposable
{
    public static readonly FontResolver Default = new();

    private readonly object _skiaLock = new();
    private SkTypeface? _skiaSansTypeface;
    private bool _disposed;

    /// <summary>
    /// Resolves the sans-serif <see cref="SkTypeface"/> for the Skia paint path.
    /// Resolved once and cached for the resolver's lifetime — typeface creation
    /// (parsing TTF tables / hitting SkFontMgr) is the expensive step; sized
    /// fonts and shaping are built on top of it elsewhere.
    /// <para>
    /// <c>internal</c> because <see cref="SkTypeface"/> is a <c>Tessera.Skia</c>
    /// internal handle — only the Skia-facing paint code (the measurer and the
    /// Graphite backend) names it.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">No typeface could be resolved.</exception>
    internal SkTypeface GetSkiaSansSerifTypeface()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_skiaSansTypeface is { } cached)
            return cached;

        lock (_skiaLock)
        {
            return _skiaSansTypeface ??= ResolveSkiaSansSerifTypeface();
        }
    }

    private static SkTypeface ResolveSkiaSansSerifTypeface()
    {
        // Tier 1: the bundled OpenSans-Regular.ttf. The embedded resource always
        // ships inside Tessera.Paint.dll, so this is the deterministic default.
        if (TryLoadBundledTtfBytes(out var ttf))
        {
            try
            {
                return SkTypeface.FromData(ttf);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Bundled font unreadable by Skia — fall through to system.
            }
        }

        // Tier 2: an installed sans-serif family via Skia's SkFontMgr/CoreText.
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
            try
            {
                return SkTypeface.FromName(name);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Try the next family.
            }
        }

        // Tier 3: ask the system font manager for its generic "sans-serif".
        // SkFontMgr resolves this to a real face on every supported platform.
        try
        {
            return SkTypeface.FromName("sans-serif");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new InvalidOperationException(
                "No Skia typeface available. The bundled OpenSans-Regular.ttf " +
                "failed to load and no system sans-serif family resolved. " +
                "See browser-plan/08_FONTS_PAINT.md.", ex);
        }
    }

    /// <summary>Reads the bundled sans-serif TTF/OTF bytes (filesystem bundle first, then embedded).</summary>
    private static bool TryLoadBundledTtfBytes(out byte[] bytes)
    {
        // Filesystem bundle (preferred for distribution).
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
                        bytes = File.ReadAllBytes(file);
                        return true;
                    }
                    catch (IOException)
                    {
                        // Try the next file.
                    }
                }
            }
        }

        // Embedded resource bundle (always present inside the assembly).
        var asm = typeof(FontResolver).Assembly;
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                continue;
            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null) continue;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            bytes = ms.ToArray();
            return true;
        }

        bytes = [];
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _skiaSansTypeface?.Dispose();
        _skiaSansTypeface = null;
    }
}
