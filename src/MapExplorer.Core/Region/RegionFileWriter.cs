using System.Buffers.Binary;
using System.IO.Compression;

namespace MapExplorer.Core.Region;

// Rebuilds a region file's bytes given a set of already-patched chunks' raw NBT. Rather than manage
// an in-place free-list over the existing sector layout (what real Minecraft does), this always
// rebuilds the sector area from scratch: untouched chunks' compressed bytes are copied verbatim
// (no recompression), patched chunks are freshly zlib-compressed, and everything is laid out
// back-to-back starting at sector 2. Simpler than in-place patching and always produces a valid,
// defragmented file. Pure byte transform — no disk I/O — so it's easy to unit test.
public static class RegionFileWriter
{
    // The sector-table entry format packs sectorCount into a single byte (see ReadRawEntryBytes'
    // "& 0xFF" and the offset-word layout below) — a chunk needing more than this many 4KiB sectors
    // (~1 MiB) can't be represented at all. Real Minecraft handles that rare case by spilling the
    // chunk into a separate "c.x.z.mcc" file; this writer doesn't implement that, so it fails loudly
    // instead, since silently OR-ing an overflowed sectorCount into the offset word would corrupt
    // both this entry's byte range and, via bit overlap, its sectorOffset too.
    private const int MaxSectorsPerChunk = 255;

    public static byte[] Rebuild(byte[] originalRegionBytes,
        IReadOnlyDictionary<(int LocalX, int LocalZ), byte[]> patchedChunkNbt,
        long timestampSeconds)
    {
        bool hasHeader = originalRegionBytes.Length >= 8192;
        var newOffsets = new uint[1024];
        var newTimestamps = new uint[1024];
        var sectors = new List<byte[]>();
        int nextSector = 2; // sectors 0-1 are the offset + timestamp tables

        for (int i = 0; i < 1024; i++)
        {
            int localX = i % 32;
            int localZ = i / 32;

            byte[]? entry;
            uint timestamp;

            if (patchedChunkNbt.TryGetValue((localX, localZ), out var newNbt))
            {
                entry = BuildEntry(newNbt);
                timestamp = (uint)timestampSeconds;
            }
            else if (hasHeader)
            {
                entry = ReadRawEntryBytes(originalRegionBytes, i);
                timestamp = BinaryPrimitives.ReadUInt32BigEndian(originalRegionBytes.AsSpan(4096 + i * 4, 4));
            }
            else
            {
                entry = null;
                timestamp = 0;
            }

            if (entry is null) continue; // empty slot — offset/timestamp stay 0

            int sectorCount = (int)Math.Ceiling(entry.Length / 4096.0);
            if (sectorCount > MaxSectorsPerChunk)
            {
                throw new InvalidOperationException(
                    $"Chunk at region slot ({localX}, {localZ}) needs {sectorCount} sectors, " +
                    $"exceeding the {MaxSectorsPerChunk}-sector-per-chunk limit of the region file format " +
                    "(oversized/.mcc chunk storage is not supported).");
            }
            var padded = new byte[sectorCount * 4096];
            entry.CopyTo(padded, 0);
            sectors.Add(padded);

            newOffsets[i] = ((uint)nextSector << 8) | (uint)sectorCount;
            newTimestamps[i] = timestamp;
            nextSector += sectorCount;
        }

        using var ms = new MemoryStream();
        var offsetHeader = new byte[4096];
        for (int i = 0; i < 1024; i++) BinaryPrimitives.WriteUInt32BigEndian(offsetHeader.AsSpan(i * 4, 4), newOffsets[i]);
        ms.Write(offsetHeader);

        var timestampHeader = new byte[4096];
        for (int i = 0; i < 1024; i++) BinaryPrimitives.WriteUInt32BigEndian(timestampHeader.AsSpan(i * 4, 4), newTimestamps[i]);
        ms.Write(timestampHeader);

        foreach (var sector in sectors) ms.Write(sector);
        return ms.ToArray();
    }

    /// <summary>Builds one sector-table entry's payload (4-byte length + 1-byte compression type +
    /// zlib-compressed NBT), unpadded.</summary>
    private static byte[] BuildEntry(byte[] rawNbt)
    {
        var compressed = ZlibCompress(rawNbt);
        var entry = new byte[5 + compressed.Length];
        BinaryPrimitives.WriteUInt32BigEndian(entry, (uint)(compressed.Length + 1));
        entry[4] = 2; // zlib
        compressed.CopyTo(entry, 5);
        return entry;
    }

    /// <summary>Slices an existing entry's raw bytes (length field + compression byte + compressed
    /// payload) straight out of the original buffer, for verbatim reuse. Mirrors RegionFile's own
    /// sector-table parsing so both stay consistent about the on-disk layout.</summary>
    private static byte[]? ReadRawEntryBytes(byte[] buffer, int i)
    {
        uint offsetEntry = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(i * 4, 4));
        uint sectorOffset = (offsetEntry >> 8) & 0xFFFFFF;
        uint sectorCount = offsetEntry & 0xFF;
        if (sectorOffset == 0 || sectorCount == 0) return null;

        long byteOffset = sectorOffset * 4096L;
        if (byteOffset + 5 > buffer.Length) return null;

        uint length = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan((int)byteOffset, 4));
        if (length < 1 || byteOffset + 5 + length > buffer.Length) return null;

        return buffer.AsSpan((int)byteOffset, (int)(4 + length)).ToArray();
    }

    private static byte[] ZlibCompress(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw);
        }
        return output.ToArray();
    }
}
