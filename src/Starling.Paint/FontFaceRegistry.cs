using Tessera.Css.FontFace;
using Tessera.Paint.WebFonts;
using Tessera.Skia.Handles;

namespace Tessera.Paint;

/// <summary>
/// Per-document registry of <c>@font-face</c>-declared typefaces. The
/// <c>FontFaceFetcher</c> in <c>Tessera.Engine</c> populates this once
/// stylesheets have landed and their <c>url()</c> sources are fetched and
/// loaded. <see cref="FontResolver"/> consults it ahead of <c>SkFontMgr</c>,
/// so a site's web fonts take precedence over a system family of the same
/// name (CSS Fonts 3 §5).
/// </summary>
/// <remarks>
/// Lookup is case-insensitive on the family name to match CSS family matching
/// (family names compare ASCII case-insensitively per CSS Fonts 3 §5.1). The
/// registry owns the typefaces it holds and disposes them on
/// <see cref="Dispose"/> at end-of-document.
/// </remarks>
public sealed class FontFaceRegistry : IDisposable
{
    private readonly Dictionary<string, List<RegisteredFace>> _byFamily =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SkTypeface> _owned = [];
    private bool _disposed;

    /// <summary>
    /// Loads <paramref name="fontBytes"/> as a typeface and registers it for
    /// <paramref name="family"/>. Sniffs WOFF / WOFF2 magic bytes and
    /// unwraps them in-process before handing SFNT bytes to Skia, so a
    /// font fetched from any of the common web-font formats lands here.
    /// <paramref name="unicodeRange"/> (optional) restricts which codepoints
    /// the face applies to; when null the face covers everything.
    /// Returns <c>false</c> if the bytes are unrecognisable or the WOFF2
    /// container uses transforms we don't yet support; fail-soft to match
    /// the stylesheet/image fetchers.
    /// </summary>
    public bool TryAdd(
        string family,
        bool bold,
        bool italic,
        ReadOnlySpan<byte> fontBytes,
        UnicodeRangeSet? unicodeRange = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(family);
        if (fontBytes.Length == 0) return false;

        ReadOnlySpan<byte> sfntBytes;
        byte[]? unwrapped = null;
        try
        {
            if (Woff2Decoder.IsWoff2(fontBytes))
            {
                unwrapped = Woff2Decoder.Decode(fontBytes);
                sfntBytes = unwrapped;
            }
            else if (WoffDecoder.IsWoff(fontBytes))
            {
                unwrapped = WoffDecoder.Decode(fontBytes);
                sfntBytes = unwrapped;
            }
            else
            {
                sfntBytes = fontBytes;
            }
        }
        catch (InvalidDataException)
        {
            // Malformed WOFF/WOFF2 container.
            return false;
        }
        catch (Woff2UnsupportedTransformException)
        {
            // WOFF2 file uses the glyf/loca transform we don't unwrap. The
            // fetcher's caller falls through to the next src entry — sites
            // that want a fallback should list a TTF/OTF after the WOFF2.
            return false;
        }

        SkTypeface typeface;
        try
        {
            typeface = SkTypeface.FromData(sfntBytes);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }

        if (!_byFamily.TryGetValue(family, out var faces))
        {
            faces = [];
            _byFamily[family] = faces;
        }
        faces.Add(new RegisteredFace(bold, italic, unicodeRange, typeface));
        _owned.Add(typeface);
        _ = unwrapped; // keep the array alive past SkTypeface.FromData (it copies internally).
        return true;
    }

    /// <summary>
    /// Looks up a face for <paramref name="family"/> matching the requested
    /// bold/italic flags. When <paramref name="probeCodepoint"/> is provided,
    /// faces whose <c>unicode-range</c> does not cover it are skipped — that
    /// way a Google-Fonts-style stylesheet with separate Latin and Cyrillic
    /// faces for the same family doesn't return the Latin subset for a
    /// Cyrillic run. Exact bold/italic match wins; otherwise the first
    /// covering face wins (paint stack synthesises any missing style).
    /// </summary>
    internal bool TryGet(string family, bool bold, bool italic, int? probeCodepoint, out SkTypeface typeface)
    {
        if (_disposed || !_byFamily.TryGetValue(family, out var faces) || faces.Count == 0)
        {
            typeface = null!;
            return false;
        }

        RegisteredFace? firstCovering = null;
        foreach (var f in faces)
        {
            if (probeCodepoint is int cp && f.UnicodeRange is { } range && !range.Contains(cp))
                continue;
            firstCovering ??= f;
            if (f.Bold == bold && f.Italic == italic)
            {
                typeface = f.Typeface;
                return true;
            }
        }

        if (firstCovering is { } any)
        {
            typeface = any.Typeface;
            return true;
        }

        typeface = null!;
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var t in _owned) t.Dispose();
        _owned.Clear();
        _byFamily.Clear();
    }

    private readonly record struct RegisteredFace(
        bool Bold,
        bool Italic,
        UnicodeRangeSet? UnicodeRange,
        SkTypeface Typeface);
}
