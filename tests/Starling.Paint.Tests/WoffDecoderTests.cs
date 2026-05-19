using System.Buffers.Binary;
using System.IO.Compression;
using FluentAssertions;
using Starling.Paint.WebFonts;
namespace Starling.Paint.Tests;

[TestClass]
public sealed class WoffDecoderTests
{
    [TestMethod]
    public void Detects_woff_magic_bytes()
    {
        WoffDecoder.IsWoff(new byte[] { 0x77, 0x4F, 0x46, 0x46 }).Should().BeTrue();
        WoffDecoder.IsWoff(new byte[] { 0x77, 0x4F, 0x46, 0x32 }).Should().BeFalse();
        WoffDecoder.IsWoff(new byte[] { 0x00, 0x01 }).Should().BeFalse();
    }

    [TestMethod]
    public void Detects_woff2_magic_bytes()
    {
        Woff2Decoder.IsWoff2(new byte[] { 0x77, 0x4F, 0x46, 0x32 }).Should().BeTrue();
        Woff2Decoder.IsWoff2(new byte[] { 0x77, 0x4F, 0x46, 0x46 }).Should().BeFalse();
    }

    [TestMethod]
    public void Round_trips_a_synthetic_woff_to_sfnt()
    {
        // Build a tiny "SFNT" with two named tables (just placeholder bytes —
        // we're testing the wrapping/unwrapping, not whether a rasterizer
        // accepts it).
        var tables = new (string Tag, byte[] Data)[]
        {
            ("head", Pad(Enumerable.Range(0, 54).Select(i => (byte)i).ToArray(), 4)),
            ("foo ", Pad(Enumerable.Range(100, 32).Select(i => (byte)i).ToArray(), 4)),
        };
        var sfnt = BuildSfnt(tables);
        var woff = WrapAsWoff(sfnt, tables);

        var roundTripped = WoffDecoder.Decode(woff);

        // Header signature stays the same; payload sizes match; table data
        // matches byte-for-byte after re-emission.
        BinaryPrimitives.ReadUInt32BigEndian(roundTripped).Should().Be(
            BinaryPrimitives.ReadUInt32BigEndian(sfnt),
            "the SFNT flavor must round-trip");
        // The re-emitted SFNT length is allowed to differ by trailing zero
        // padding; the per-table bytes should still be intact.
        foreach (var (tag, data) in tables)
        {
            var found = ExtractTableData(roundTripped, tag);
            found.Should().Equal(data);
        }
    }

    private static byte[] Pad(byte[] data, int boundary)
    {
        var rem = data.Length % boundary;
        if (rem == 0) return data;
        var padded = new byte[data.Length + (boundary - rem)];
        data.CopyTo(padded, 0);
        return padded;
    }

    private static byte[] BuildSfnt((string Tag, byte[] Data)[] tables)
    {
        var numTables = tables.Length;
        var headerSize = 12 + numTables * 16;
        var totalLength = headerSize;
        var offsets = new int[numTables];
        for (var i = 0; i < numTables; i++)
        {
            offsets[i] = totalLength;
            totalLength += tables[i].Data.Length;
            totalLength = (totalLength + 3) & ~3;
        }

        var output = new byte[totalLength];
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(0), 0x00010000); // TrueType flavor.
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(4), (ushort)numTables);
        // searchRange/entrySelector/rangeShift — we leave as zero; the decoder
        // recomputes them on emission.
        for (var i = 0; i < numTables; i++)
        {
            var entryOffset = 12 + i * 16;
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(entryOffset), TagBytes(tables[i].Tag));
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(entryOffset + 4), 0xCAFEBABE); // checksum placeholder
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(entryOffset + 8), (uint)offsets[i]);
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(entryOffset + 12), (uint)tables[i].Data.Length);
            tables[i].Data.CopyTo(output.AsSpan(offsets[i]));
        }
        return output;
    }

    private static byte[] WrapAsWoff(byte[] sfnt, (string Tag, byte[] Data)[] tables)
    {
        var numTables = tables.Length;
        // Compress each table with zlib.
        var compressed = new byte[numTables][];
        for (var i = 0; i < numTables; i++)
        {
            using var ms = new MemoryStream();
            using (var zlib = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(tables[i].Data, 0, tables[i].Data.Length);
            compressed[i] = ms.ToArray();
        }

        var headerSize = 44 + numTables * 20;
        var totalLength = headerSize;
        var offsets = new int[numTables];
        for (var i = 0; i < numTables; i++)
        {
            offsets[i] = totalLength;
            totalLength += compressed[i].Length;
            totalLength = (totalLength + 3) & ~3;
        }

        var output = new byte[totalLength];
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(0), 0x774F4646); // 'wOFF'
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(4), 0x00010000); // flavor
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(8), (uint)totalLength);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(12), (ushort)numTables);

        for (var i = 0; i < numTables; i++)
        {
            var entryOffset = 44 + i * 20;
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(entryOffset), TagBytes(tables[i].Tag));
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(entryOffset + 4), (uint)offsets[i]);
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(entryOffset + 8), (uint)compressed[i].Length);
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(entryOffset + 12), (uint)tables[i].Data.Length);
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(entryOffset + 16), 0xCAFEBABE);
            compressed[i].CopyTo(output.AsSpan(offsets[i]));
        }
        return output;
    }

    private static byte[] ExtractTableData(byte[] sfnt, string tag)
    {
        var numTables = BinaryPrimitives.ReadUInt16BigEndian(sfnt.AsSpan(4));
        var want = TagBytes(tag);
        for (var i = 0; i < numTables; i++)
        {
            var entryOffset = 12 + i * 16;
            var entryTag = BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(entryOffset));
            if (entryTag != want) continue;
            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(entryOffset + 8));
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(entryOffset + 12));
            return sfnt.AsSpan(offset, length).ToArray();
        }
        throw new InvalidOperationException($"table {tag} missing from output");
    }

    private static uint TagBytes(string s) =>
        ((uint)(byte)s[0] << 24) | ((uint)(byte)s[1] << 16) |
        ((uint)(byte)s[2] << 8) | (byte)s[3];
}
