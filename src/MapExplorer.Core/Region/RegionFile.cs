using System.Buffers.Binary;
using System.IO.Compression;

namespace MapExplorer.Core.Region;

public readonly struct RawChunk(int chunkX, int chunkZ, byte[] data)
{
    public int ChunkX { get; } = chunkX;
    public int ChunkZ { get; } = chunkZ;
    public byte[] Data { get; } = data;
}

// Ported from the Electron app's src/main/region.ts — same .mca sector-table
// format, same zlib (compression type 2) handling via System.IO.Compression
// instead of pako.
public static class RegionFile
{
    public static List<RawChunk> Parse(byte[] buffer, string filename)
    {
        var result = new List<RawChunk>();

        if (!TryParseRegionCoords(filename, out int regionX, out int regionZ)) return result;
        if (buffer.Length < 8192) return result;

        for (int i = 0; i < 1024; i++)
        {
            var chunk = TryReadEntry(buffer, i, regionX, regionZ);
            if (chunk is not null) result.Add(chunk.Value);
        }

        return result;
    }

    /// <summary>Decodes a single chunk's raw (decompressed) NBT bytes from a region file buffer, without
    /// parsing the other 1023 entries — used for on-demand decode of chunks outside the initial bulk load
    /// (see WorldChunkStore). Returns null if the region file doesn't cover this chunk yet (ungenerated).</summary>
    public static RawChunk? ParseChunk(byte[] buffer, string filename, int localX, int localZ)
    {
        if (!TryParseRegionCoords(filename, out int regionX, out int regionZ)) return null;
        if (buffer.Length < 8192) return null;
        if (localX is < 0 or > 31 || localZ is < 0 or > 31) return null;

        int i = localZ * 32 + localX;
        return TryReadEntry(buffer, i, regionX, regionZ);
    }

    private static bool TryParseRegionCoords(string filename, out int regionX, out int regionZ)
    {
        var baseName = Path.GetFileNameWithoutExtension(filename); // "r.0.0"
        var parts = baseName.Split('.');
        if (parts.Length < 3 || !int.TryParse(parts[1], out regionX) || !int.TryParse(parts[2], out regionZ))
        {
            regionX = 0;
            regionZ = 0;
            return false;
        }
        return true;
    }

    private static RawChunk? TryReadEntry(byte[] buffer, int i, int regionX, int regionZ)
    {
        int localX = i % 32;
        int localZ = i / 32;
        uint offsetEntry = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(i * 4, 4));
        uint sectorOffset = (offsetEntry >> 8) & 0xFFFFFF;
        uint sectorCount = offsetEntry & 0xFF;

        if (sectorOffset == 0 || sectorCount == 0) return null;

        long byteOffset = sectorOffset * 4096L;
        if (byteOffset + 5 > buffer.Length) return null;

        uint length = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan((int)byteOffset, 4));
        byte compression = buffer[byteOffset + 4];

        if (length < 1 || byteOffset + 5 + length > buffer.Length) return null;

        var compressed = buffer.AsSpan((int)(byteOffset + 5), (int)(length - 1));

        byte[] decompressed;
        try
        {
            decompressed = compression switch
            {
                1 => Inflate(compressed, gzip: true),
                2 => Inflate(compressed, gzip: false),
                3 => compressed.ToArray(),
                _ => throw new InvalidOperationException($"unknown compression {compression}")
            };
        }
        catch
        {
            return null;
        }

        return new RawChunk(regionX * 32 + localX, regionZ * 32 + localZ, decompressed);
    }

    private static byte[] Inflate(ReadOnlySpan<byte> compressed, bool gzip)
    {
        using var input = new MemoryStream(compressed.ToArray());
        using Stream decompressor = gzip ? new GZipStream(input, CompressionMode.Decompress) : new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        decompressor.CopyTo(output);
        return output.ToArray();
    }
}
