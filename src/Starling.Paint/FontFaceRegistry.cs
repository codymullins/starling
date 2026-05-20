using SixLabors.Fonts;
using Starling.Css.FontFace;

namespace Starling.Paint;

/// <summary>
/// Per-document registry of <c>@font-face</c>-declared typefaces. The
/// <c>FontFaceFetcher</c> in <c>Starling.Engine</c> populates this once
/// stylesheets have landed and their <c>url()</c> sources are fetched and
/// loaded.
/// <para>
/// The engine paints through ImageSharp.Drawing 3, which owns its own
/// font collection. <see cref="ImageSharpFontLookup.LoadCollection(FontFaceRegistry?)"/>
/// snapshots this registry at backend construction time, so any face
/// registered before <c>Painter.RenderDocument</c> participates in
/// font-family resolution alongside the bundled and system fonts.
/// </para>
/// </summary>
/// <remarks>
/// Lookup is case-insensitive on the family name to match CSS family matching
/// (family names compare ASCII case-insensitively per CSS Fonts 3 §5.1).
/// </remarks>
public sealed class FontFaceRegistry : IDisposable
{
    private readonly Dictionary<string, List<RegisteredFace>> _byFamily =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>
    /// Loads <paramref name="fontBytes"/> as a face and registers it for
    /// <paramref name="family"/>. <paramref name="unicodeRange"/> (optional)
    /// restricts which codepoints the face applies to; when null the face
    /// covers everything. Returns <c>false</c> if the bytes are unrecognisable;
    /// fail-soft to match the stylesheet/image fetchers.
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

        var bytes = fontBytes.ToArray();
        try
        {
            // Registration is the boundary where bad src entries must fail so
            // CSS Fonts fallback can continue to the next source.
            using var stream = new MemoryStream(bytes, writable: false);
            _ = new FontCollection().Add(stream);
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
        faces.Add(new RegisteredFace(bold, italic, unicodeRange, bytes));
        return true;
    }

    /// <summary>
    /// Adds every registered face to <paramref name="collection"/>. Used by
    /// the paint backend (<see cref="ImageSharpFontLookup.LoadCollection(FontFaceRegistry?)"/>)
    /// to fold web fonts into the per-render <c>FontCollection</c> snapshot.
    /// <para>
    /// SixLabors.Fonts identifies families from each font's own
    /// <c>name</c> table on <c>FontCollection.Add</c>, not from an
    /// externally-supplied alias. In practice this works because authors keep
    /// the <c>@font-face</c> <c>font-family</c> descriptor in sync with the
    /// font's internal family name; a stylesheet that uses a renamed family
    /// will still register the font but only resolve by the font's own name.
    /// </para>
    /// </summary>
    internal void AddTo(FontCollection collection)
    {
        if (_disposed) return;

        foreach (var faces in _byFamily.Values)
        {
            foreach (var face in faces)
            {
                using var stream = new MemoryStream(face.FontBytes, writable: false);
                collection.Add(stream);
            }
        }
    }

    /// <summary>
    /// Returns the registered font bytes for the given family / weight / style,
    /// honouring <c>unicode-range</c> when <paramref name="probeCodepoint"/>
    /// is provided. Exact bold/italic match wins; otherwise the first
    /// covering face wins. Used by tests that exercise the registry directly;
    /// the paint backend consumes the registry via
    /// <see cref="AddTo"/>.
    /// </summary>
    internal bool TryGet(string family, bool bold, bool italic, int? probeCodepoint, out byte[] fontBytes)
    {
        if (_disposed || !_byFamily.TryGetValue(family, out var faces) || faces.Count == 0)
        {
            fontBytes = [];
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
                fontBytes = f.FontBytes;
                return true;
            }
        }

        if (firstCovering is { } any)
        {
            fontBytes = any.FontBytes;
            return true;
        }

        fontBytes = [];
        return false;
    }

    /// <summary>
    /// Disposes the registry; subsequent registrations throw and no registered
    /// faces are exposed to paint backends.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _byFamily.Clear();
    }

    private readonly struct RegisteredFace
    {
        public RegisteredFace(bool bold, bool italic, UnicodeRangeSet? unicodeRange, byte[] fontBytes)
        {
            Bold = bold;
            Italic = italic;
            UnicodeRange = unicodeRange;
            FontBytes = fontBytes;
        }

        public bool Bold { get; }

        public bool Italic { get; }

        public UnicodeRangeSet? UnicodeRange { get; }

        public byte[] FontBytes { get; }
    }
}
