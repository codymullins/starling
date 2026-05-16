using System.Buffers.Binary;
using System.IO.Compression;

namespace Tessera.Paint.WebFonts;

/// <summary>
/// Decompresses a WOFF2 (Web Open Font Format, v2) container back to its
/// underlying SFNT bytes. Skia's <c>SkTypeface::makeFromData</c> accepts SFNT
/// directly, so once we unwrap WOFF2 the rest of the @font-face pipeline is
/// unchanged.
/// </summary>
/// <remarks>
/// Spec: <see href="https://www.w3.org/TR/WOFF2/"/>. WOFF2 is tighter than
/// WOFF v1: a single Brotli stream replaces per-table zlib, and the
/// <c>glyf</c>/<c>loca</c> tables are stored in a transformed form that
/// rebuilds the original TrueType bytes. We unwrap the Brotli stream and
/// stitch tables back together, but the <c>glyf</c> transform reversal
/// (WOFF2 §5.1) is intentionally not implemented — it is ~1000 lines of
/// careful TrueType bookkeeping that needs a real font corpus to validate.
/// A typed <see cref="Woff2UnsupportedTransformException"/> signals this so
/// <c>FontFaceFetcher</c> can fall back to the next <c>src</c> entry.
/// <para>
/// What works today: WOFF2 files whose tables are all stored verbatim (no
/// transform — rare in the wild but legal per spec). Variable fonts shipped
/// as CFF2 fall in this bucket too, since the glyf transform only applies to
/// TrueType outlines.
/// </para>
/// </remarks>
internal static class Woff2Decoder
{
    /// <summary>The leading four bytes of a WOFF2 container.</summary>
    public const uint Signature = 0x774F4632; // 'wOF2'

    public static bool IsWoff2(ReadOnlySpan<byte> bytes)
        => bytes.Length >= 4 &&
           BinaryPrimitives.ReadUInt32BigEndian(bytes) == Signature;

    public static byte[] Decode(ReadOnlySpan<byte> woff2)
    {
        if (woff2.Length < 48)
            throw new InvalidDataException("WOFF2: header too short.");
        if (BinaryPrimitives.ReadUInt32BigEndian(woff2) != Signature)
            throw new InvalidDataException("WOFF2: bad signature.");

        var flavor = BinaryPrimitives.ReadUInt32BigEndian(woff2[4..]);
        var numTables = BinaryPrimitives.ReadUInt16BigEndian(woff2[12..]);
        var totalCompressedSize = BinaryPrimitives.ReadUInt32BigEndian(woff2[20..]);

        if (flavor == 0x74746366) // 'ttcf'
            throw new InvalidDataException("WOFF2: font collections are not supported.");

        var cursor = 48;
        var entries = new Woff2TableEntry[numTables];
        for (var i = 0; i < numTables; i++)
            cursor = ReadDirectoryEntry(woff2, cursor, out entries[i]);

        // glyf/loca transforms are the common case in real-world WOFF2 files.
        // Surfacing this as a typed exception lets the fetcher fall through
        // to the next src entry (often a TTF/OTF fallback declared after the
        // WOFF2 url in the @font-face src list).
        foreach (var e in entries)
        {
            if ((e.Tag == TagGlyf || e.Tag == TagLoca) && e.IsTransformed)
                throw new Woff2UnsupportedTransformException(
                    "WOFF2 glyf/loca transform reversal is not implemented; " +
                    "declare a TTF/OTF fallback in the @font-face src list, " +
                    "or serve the font in an unwrapped format.");
        }

        if (cursor + totalCompressedSize > woff2.Length)
            throw new InvalidDataException("WOFF2: compressed payload overruns container.");

        var compressed = woff2.Slice(cursor, (int)totalCompressedSize).ToArray();
        var totalUncompressed = 0L;
        foreach (var e in entries) totalUncompressed += e.TransformLength ?? e.OrigLength;
        if (totalUncompressed > int.MaxValue)
            throw new InvalidDataException("WOFF2: decoded payload exceeds 2 GiB.");
        var payload = BrotliInflate(compressed, (int)totalUncompressed);

        var rawTables = new byte[numTables][];
        var payloadPos = 0;
        for (var i = 0; i < numTables; i++)
        {
            var len = (int)(entries[i].TransformLength ?? entries[i].OrigLength);
            if (payloadPos + len > payload.Length)
                throw new InvalidDataException($"WOFF2: payload truncated at table {i}.");
            rawTables[i] = new byte[len];
            payload.AsSpan(payloadPos, len).CopyTo(rawTables[i]);
            payloadPos += len;
        }

        var sfntEntries = new WoffDecoder.TableEntry[numTables];
        for (var i = 0; i < numTables; i++)
        {
            sfntEntries[i] = new WoffDecoder.TableEntry
            {
                Tag = entries[i].Tag,
                Offset = 0,
                CompLength = (uint)rawTables[i].Length,
                OrigLength = (uint)rawTables[i].Length,
                OrigChecksum = 0,
            };
        }
        return WoffDecoder.AssembleSfnt(flavor, sfntEntries, rawTables);
    }

    private const uint TagGlyf = 0x676C7966; // 'glyf'
    private const uint TagLoca = 0x6C6F6361; // 'loca'

    private static int ReadDirectoryEntry(ReadOnlySpan<byte> woff2, int cursor, out Woff2TableEntry entry)
    {
        if (cursor >= woff2.Length)
            throw new InvalidDataException("WOFF2: directory truncated.");
        var flags = woff2[cursor++];
        var tagIndex = flags & 0x3F;
        var transformVersion = (flags >> 6) & 0x03;

        uint tag;
        if (tagIndex == 0x3F)
        {
            if (cursor + 4 > woff2.Length)
                throw new InvalidDataException("WOFF2: custom tag truncated.");
            tag = BinaryPrimitives.ReadUInt32BigEndian(woff2[cursor..]);
            cursor += 4;
        }
        else
        {
            tag = KnownTags[tagIndex];
        }

        cursor = ReadUIntBase128(woff2, cursor, out var origLength);
        uint? transformLength = null;
        var hasTransform = (tag == TagGlyf || tag == TagLoca)
            ? transformVersion != 3
            : transformVersion != 0;
        if (hasTransform)
        {
            cursor = ReadUIntBase128(woff2, cursor, out var tlen);
            transformLength = tlen;
        }

        entry = new Woff2TableEntry
        {
            Tag = tag,
            OrigLength = origLength,
            TransformLength = transformLength,
            IsTransformed = hasTransform,
        };
        return cursor;
    }

    private static int ReadUIntBase128(ReadOnlySpan<byte> data, int cursor, out uint value)
    {
        value = 0;
        for (var i = 0; i < 5; i++)
        {
            if (cursor >= data.Length)
                throw new InvalidDataException("WOFF2: UIntBase128 truncated.");
            var b = data[cursor++];
            if (i == 0 && b == 0x80)
                throw new InvalidDataException("WOFF2: UIntBase128 leading zero.");
            if ((value & 0xFE000000) != 0)
                throw new InvalidDataException("WOFF2: UIntBase128 overflow.");
            value = (value << 7) | (uint)(b & 0x7F);
            if ((b & 0x80) == 0)
                return cursor;
        }
        throw new InvalidDataException("WOFF2: UIntBase128 too long.");
    }

    private static byte[] BrotliInflate(byte[] compressed, int expected)
    {
        using var input = new MemoryStream(compressed);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        var output = new byte[expected];
        var read = 0;
        while (read < expected)
        {
            var n = brotli.Read(output, read, expected - read);
            if (n == 0) break;
            read += n;
        }
        if (read != expected)
            throw new InvalidDataException($"WOFF2: brotli yielded {read} bytes, expected {expected}.");
        return output;
    }

    private static readonly uint[] KnownTags =
    {
        TagFromAscii("cmap"), TagFromAscii("head"), TagFromAscii("hhea"), TagFromAscii("hmtx"),
        TagFromAscii("maxp"), TagFromAscii("name"), TagFromAscii("OS/2"), TagFromAscii("post"),
        TagFromAscii("cvt "), TagFromAscii("fpgm"), TagFromAscii("glyf"), TagFromAscii("loca"),
        TagFromAscii("prep"), TagFromAscii("CFF "), TagFromAscii("VORG"), TagFromAscii("EBDT"),
        TagFromAscii("EBLC"), TagFromAscii("gasp"), TagFromAscii("hdmx"), TagFromAscii("kern"),
        TagFromAscii("LTSH"), TagFromAscii("PCLT"), TagFromAscii("VDMX"), TagFromAscii("vhea"),
        TagFromAscii("vmtx"), TagFromAscii("BASE"), TagFromAscii("GDEF"), TagFromAscii("GPOS"),
        TagFromAscii("GSUB"), TagFromAscii("EBSC"), TagFromAscii("JSTF"), TagFromAscii("MATH"),
        TagFromAscii("CBDT"), TagFromAscii("CBLC"), TagFromAscii("COLR"), TagFromAscii("CPAL"),
        TagFromAscii("SVG "), TagFromAscii("sbix"), TagFromAscii("acnt"), TagFromAscii("avar"),
        TagFromAscii("bdat"), TagFromAscii("bloc"), TagFromAscii("bsln"), TagFromAscii("cvar"),
        TagFromAscii("fdsc"), TagFromAscii("feat"), TagFromAscii("fmtx"), TagFromAscii("fvar"),
        TagFromAscii("gvar"), TagFromAscii("hsty"), TagFromAscii("just"), TagFromAscii("lcar"),
        TagFromAscii("ltag"), TagFromAscii("mort"), TagFromAscii("morx"), TagFromAscii("opbd"),
        TagFromAscii("prop"), TagFromAscii("trak"), TagFromAscii("Zapf"), TagFromAscii("Silf"),
        TagFromAscii("Glat"), TagFromAscii("Gloc"), TagFromAscii("Feat"), TagFromAscii("Sill"),
    };

    private static uint TagFromAscii(string s) =>
        ((uint)(byte)s[0] << 24) | ((uint)(byte)s[1] << 16) |
        ((uint)(byte)s[2] << 8) | (byte)s[3];

    internal struct Woff2TableEntry
    {
        public uint Tag;
        public uint OrigLength;
        public uint? TransformLength;
        public bool IsTransformed;
    }
}

/// <summary>
/// Raised by <see cref="Woff2Decoder"/> when the input WOFF2 file uses the
/// glyf/loca transform that we have not implemented. The fetcher catches it
/// and skips the source so the next <c>@font-face src</c> entry — typically
/// a TTF/OTF fallback — gets a chance.
/// </summary>
#pragma warning disable RCS1194 // implement exception constructors - this is an
// internal sentinel; the single ctor is the only way it is ever raised.
internal sealed class Woff2UnsupportedTransformException : Exception
{
    public Woff2UnsupportedTransformException(string message) : base(message) { }
}
#pragma warning restore RCS1194
