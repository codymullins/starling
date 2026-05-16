using System.Buffers;
using System.Collections.Concurrent;
using Tessera.Layout.Text;
using Tessera.Skia;
using Tessera.Skia.Handles;
using Tessera.Skia.Interop;

namespace Tessera.Paint;

/// <summary>
/// Resolves a CSS <c>font-family</c> list to a concrete <see cref="SkTypeface"/>
/// for the Skia paint path — the engine's sole rasterizer. Walks the family
/// list in order:
/// <list type="number">
///   <item>For each candidate family, consults the @font-face registry first
///   (web fonts loaded from the document's stylesheets), then asks Skia's
///   <c>SkFontMgr</c>/CoreText for an exact match.</item>
///   <item>Maps CSS generic keywords (<c>serif</c>, <c>sans-serif</c>,
///   <c>monospace</c>, <c>cursive</c>, <c>fantasy</c>, <c>system-ui</c>) to
///   sensible platform fallbacks.</item>
///   <item>If nothing in the list resolves, the bundled
///   <c>OpenSans-Regular.ttf</c> (or, if that fails, the system generic
///   <c>sans-serif</c>) is the final fallback.</item>
/// </list>
/// Cached by the unordered <see cref="FontSpec"/> shape so a span seeing the
/// same family list + bold/italic doesn't re-walk the chain.
/// </summary>
public sealed class FontResolver : IDisposable
{
    public static readonly FontResolver Default = new();

    private readonly ConcurrentDictionary<FontSpec, SkTypeface> _byFontSpec = new();
    private readonly List<SkTypeface> _ownedClones = [];
    private readonly object _ownedClonesLock = new();
    private readonly object _bundledLock = new();
    private SkTypeface? _bundled;
    private bool _disposed;

    /// <summary>
    /// Resolves the typeface for <paramref name="spec"/>. Threads through the
    /// per-document web-font registry when supplied — those faces take
    /// precedence over system fonts of the same family name (CSS Fonts 3 §5).
    /// </summary>
    /// <exception cref="InvalidOperationException">No typeface resolves and the bundled fallback is also unavailable.</exception>
    /// <summary>
    /// Resolves the typeface for <paramref name="spec"/>. <paramref name="probeCodepoint"/>
    /// (when supplied) is consulted against the web-font registry's
    /// <c>unicode-range</c> filters so a subsetted face only matches when it
    /// covers the codepoint being shaped — Google Fonts and Adobe Fonts both
    /// rely on this for cheap script subsetting.
    /// </summary>
    /// <exception cref="InvalidOperationException">No typeface resolves and the bundled fallback is also unavailable.</exception>
    internal SkTypeface GetTypeface(FontSpec spec, FontFaceRegistry? webFonts = null, int? probeCodepoint = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(spec);

        // Web-font registry results are not cached on the resolver: the
        // registry is per-document, so caching here would leak its faces
        // across navigations. Hot lookups still hit the registry's own cache.
        if (webFonts is not null && TryResolveFromList(spec, webFonts, probeCodepoint) is { } webMatch)
            return ApplyVariations(webMatch, spec.Variations, ownClone: true);

        return _byFontSpec.GetOrAdd(spec, s => ApplyVariations(ResolveSystem(s), s.Variations, ownClone: false));
    }

    /// <summary>
    /// Applies the variation axes to <paramref name="base"/>. Returns the
    /// supplied typeface unchanged when there are no axes. Otherwise clones
    /// the typeface via the shim — the clone is owned by the resolver so
    /// disposal cleans it up (callers should not dispose the result).
    /// </summary>
    private SkTypeface ApplyVariations(SkTypeface @base, IReadOnlyList<FontVariation> variations, bool ownClone)
    {
        if (variations.Count == 0) return @base;

        var buffer = ArrayPool<TsFontVariation>.Shared.Rent(variations.Count);
        try
        {
            for (var i = 0; i < variations.Count; i++)
            {
                buffer[i] = new TsFontVariation
                {
                    Tag = TsFontVariation.PackTag(variations[i].Tag),
                    Value = variations[i].Value,
                };
            }
            var clone = @base.CloneWithVariations(buffer.AsSpan(0, variations.Count));
            if (ownClone)
            {
                lock (_ownedClonesLock) _ownedClones.Add(clone);
            }
            return clone;
        }
        finally
        {
            ArrayPool<TsFontVariation>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Back-compat: the sans-serif typeface. Kept for callers that have not
    /// yet been threaded through the spec-aware path.
    /// </summary>
    internal SkTypeface GetSkiaSansSerifTypeface()
        => GetTypeface(FontSpec.Default);

    private SkTypeface? TryResolveFromList(FontSpec spec, FontFaceRegistry webFonts, int? probeCodepoint)
    {
        foreach (var family in spec.Families)
        {
            if (webFonts.TryGet(family, spec.Bold, spec.Italic, probeCodepoint, out var typeface))
                return typeface;
        }
        return null;
    }

    private SkTypeface ResolveSystem(FontSpec spec)
    {
        // Walk the declared family list, expanding any generic keywords. Each
        // candidate gets a single exact-match attempt — the shim's
        // ts_typeface_from_name has been tightened to return TS_NOT_FOUND
        // rather than the lenient legacyMakeTypeface fallback, so a miss here
        // really means "no such family installed".
        foreach (var family in EnumerateCandidates(spec.Families))
        {
            if (TryMatchSystem(family) is { } match)
                return match;
        }

        return GetBundledFallback();
    }

    private static SkTypeface? TryMatchSystem(string family)
    {
        try
        {
            return SkTypeface.FromName(family);
        }
        catch (SkiaInteropException ex) when (ex.Status == TsStatus.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateCandidates(IReadOnlyList<string> families)
    {
        foreach (var family in families)
        {
            yield return family;
            foreach (var sub in ExpandGeneric(family))
                yield return sub;
        }
    }

    /// <summary>
    /// CSS generic keywords map to a small ordered list of family names that
    /// are likely to be installed on each supported platform. macOS lists
    /// come first since osx-arm64 is the only RID shipped today; the Linux
    /// entries are kept for when that RID lands.
    /// </summary>
    private static IEnumerable<string> ExpandGeneric(string family) => family.ToLowerInvariant() switch
    {
        "serif" => ["Times New Roman", "Times", "Georgia", "Liberation Serif", "DejaVu Serif", "Noto Serif"],
        "sans-serif" => ["Helvetica Neue", "Helvetica", "Arial", "Inter", "Liberation Sans", "DejaVu Sans", "Segoe UI", "Noto Sans", "Verdana"],
        "monospace" => ["Menlo", "Monaco", "SF Mono", "Courier New", "Courier", "Liberation Mono", "DejaVu Sans Mono", "Consolas", "Noto Sans Mono"],
        "cursive" => ["Snell Roundhand", "Apple Chancery", "Comic Sans MS", "Brush Script MT"],
        "fantasy" => ["Papyrus", "Impact", "Herculanum"],
        "system-ui" or "ui-sans-serif" => ["-apple-system", "system-ui", "SF Pro Text", "Helvetica Neue", "Segoe UI", "Roboto"],
        "ui-serif" => ["-apple-system", "Times New Roman", "Times"],
        "ui-monospace" => ["Menlo", "SF Mono", "Consolas"],
        _ => [],
    };

    private SkTypeface GetBundledFallback()
    {
        if (_bundled is { } b) return b;

        lock (_bundledLock)
        {
            return _bundled ??= ResolveBundled();
        }
    }

    private static SkTypeface ResolveBundled()
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

        // Tier 2: ask the system font manager for its generic "sans-serif".
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
        foreach (var t in _byFontSpec.Values) t.Dispose();
        _byFontSpec.Clear();
        lock (_ownedClonesLock)
        {
            foreach (var t in _ownedClones) t.Dispose();
            _ownedClones.Clear();
        }
        _bundled?.Dispose();
        _bundled = null;
    }
}
