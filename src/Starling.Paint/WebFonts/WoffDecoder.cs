using System.Buffers.Binary;
using System.IO.Compression;

namespace Tessera.Paint.WebFonts;

/// <summary>
/// Decompresses a WOFF (Web Open Font Format, v1) container back to its
/// underlying SFNT (TrueType/OpenType) bytes. Skia's <c>SkTypeface::makeFromData</c>
/// accepts SFNT directly; WOFF is just an SFNT wrapped with per-table zlib
/// compression and a small header, so the decoder is a header read and a
/// per-table inflate.
/// </summary>
/// <remarks>
/// Spec: <see href="https://www.w3.org/TR/WOFF/"/>. We honour the SFNT-layout
/// rules: a 12-byte sfnt header, 16 bytes per table directory entry in tag
/// order, table data aligned to 4 bytes. Metadata and private blocks
/// (sections after the table data) are dropped — they aren't part of the
/// font and Skia ignores them.
/// </remarks>
internal static class WoffDecoder
{
    /// <summary>The leading four bytes of a WOFF v1 container.</summary>
    public const uint Signature = 0x774F4646; // 'wOFF'

    public static bool IsWoff(ReadOnlySpan<byte> bytes)
        => bytes.Length >= 4 &&
           BinaryPrimitives.ReadUInt32BigEndian(bytes) == Signature;

    /// <summary>
    /// Decodes <paramref name="woff"/> into a fresh SFNT byte array. Throws
    /// <see cref="InvalidDataException"/> on a malformed container.
    /// </summary>
    public static byte[] Decode(ReadOnlySpan<byte> woff)
    {
        if (woff.Length < 44)
            throw new InvalidDataException("WOFF: header too short.");

        var sig = BinaryPrimitives.ReadUInt32BigEndian(woff);
        if (sig != Signature)
            throw new InvalidDataException("WOFF: bad signature.");

        var flavor = BinaryPrimitives.ReadUInt32BigEndian(woff[4..]);
        var numTables = BinaryPrimitives.ReadUInt16BigEndian(woff[12..]);

        if (numTables == 0)
            throw new InvalidDataException("WOFF: zero tables.");
        if (woff.Length < 44 + numTables * 20)
            throw new InvalidDataException("WOFF: directory truncated.");

        // Read table directory.
        var entries = new TableEntry[numTables];
        for (var i = 0; i < numTables; i++)
        {
            var entryOffset = 44 + i * 20;
            var entry = new TableEntry
            {
                Tag = BinaryPrimitives.ReadUInt32BigEndian(woff[entryOffset..]),
                Offset = BinaryPrimitives.ReadUInt32BigEndian(woff[(entryOffset + 4)..]),
                CompLength = BinaryPrimitives.ReadUInt32BigEndian(woff[(entryOffset + 8)..]),
                OrigLength = BinaryPrimitives.ReadUInt32BigEndian(woff[(entryOffset + 12)..]),
                OrigChecksum = BinaryPrimitives.ReadUInt32BigEndian(woff[(entryOffset + 16)..]),
            };
            if (entry.Offset + entry.CompLength > woff.Length)
                throw new InvalidDataException($"WOFF: table {i} data overruns container.");
            entries[i] = entry;
        }

        // Decompress each table. WOFF marks "no compression" by setting
        // compLength == origLength.
        var tables = new byte[numTables][];
        for (var i = 0; i < numTables; i++)
        {
            var e = entries[i];
            var slice = woff.Slice((int)e.Offset, (int)e.CompLength);
            if (e.CompLength == e.OrigLength)
            {
                tables[i] = slice.ToArray();
            }
            else
            {
                tables[i] = InflateZlib(slice, (int)e.OrigLength);
            }
        }

        return AssembleSfnt(flavor, entries, tables);
    }

    private static byte[] InflateZlib(ReadOnlySpan<byte> source, int expected)
    {
        var input = source.ToArray();
        using var input_ms = new MemoryStream(input);
        using var zlib = new ZLibStream(input_ms, CompressionMode.Decompress);
        var output = new byte[expected];
        var read = 0;
        while (read < expected)
        {
            var n = zlib.Read(output, read, expected - read);
            if (n == 0) break;
            read += n;
        }
        if (read != expected)
            throw new InvalidDataException($"WOFF: table inflate yielded {read} bytes, expected {expected}.");
        return output;
    }

    internal static byte[] AssembleSfnt(uint flavor, ReadOnlySpan<TableEntry> entries, byte[][] tables)
    {
        // SFNT layout: 12-byte header + 16 * numTables directory + tables
        // (each aligned to 4 bytes). Directory must be sorted by tag.
        var sorted = new (TableEntry Entry, byte[] Data)[entries.Length];
        for (var i = 0; i < entries.Length; i++) sorted[i] = (entries[i], tables[i]);
        Array.Sort(sorted, static (a, b) => a.Entry.Tag.CompareTo(b.Entry.Tag));

        var numTables = (ushort)entries.Length;
        var headerSize = 12 + 16 * numTables;

        var totalLength = headerSize;
        var offsets = new uint[numTables];
        for (var i = 0; i < numTables; i++)
        {
            offsets[i] = (uint)totalLength;
            totalLength += sorted[i].Data.Length;
            // Pad to 4-byte boundary.
            totalLength = (totalLength + 3) & ~3;
        }

        var output = new byte[totalLength];
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(0), flavor);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(4), numTables);
        var (searchRange, entrySelector, rangeShift) = SfntDirectoryBounds(numTables);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(6), searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(8), entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(10), rangeShift);

        for (var i = 0; i < numTables; i++)
        {
            var entryAt = 12 + i * 16;
            var e = sorted[i].Entry;
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(entryAt), e.Tag);
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(entryAt + 4), e.OrigChecksum);
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(entryAt + 8), offsets[i]);
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(entryAt + 12), (uint)sorted[i].Data.Length);

            sorted[i].Data.AsSpan().CopyTo(output.AsSpan((int)offsets[i]));
        }

        return output;
    }

    /// <summary>
    /// The classic OpenType "searchRange / entrySelector / rangeShift" trio:
    /// derived from numTables. Old fonts rely on these; modern parsers don't
    /// touch them, but a correct emitter still writes them.
    /// </summary>
    internal static (ushort SearchRange, ushort EntrySelector, ushort RangeShift) SfntDirectoryBounds(int numTables)
    {
        var entrySelector = 0;
        while ((1 << (entrySelector + 1)) <= numTables) entrySelector++;
        var searchRange = (1 << entrySelector) * 16;
        var rangeShift = numTables * 16 - searchRange;
        return ((ushort)searchRange, (ushort)entrySelector, (ushort)rangeShift);
    }

    internal struct TableEntry
    {
        public uint Tag;
        public uint Offset;
        public uint CompLength;
        public uint OrigLength;
        public uint OrigChecksum;
    }
}
