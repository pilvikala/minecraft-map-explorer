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

        var baseName = Path.GetFileNameWithoutExtension(filename); // "r.0.0"
        var parts = baseName.Split('.');
        if (parts.Length < 3 || !int.TryParse(parts[1], out int regionX) || !int.TryParse(parts[2], out int regionZ))
        {
            return result;
        }

        if (buffer.Length < 8192) return result;

        for (int i = 0; i < 1024; i++)
        {
            int localX = i % 32;
            int localZ = i / 32;
            uint offsetEntry = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(i * 4, 4));
            uint sectorOffset = (offsetEntry >> 8) & 0xFFFFFF;
            uint sectorCount = offsetEntry & 0xFF;

            if (sectorOffset == 0 || sectorCount == 0) continue;

            long byteOffset = sectorOffset * 4096L;
            if (byteOffset + 5 > buffer.Length) continue;

            uint length = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan((int)byteOffset, 4));
            byte compression = buffer[byteOffset + 4];

            if (length < 1 || byteOffset + 5 + length > buffer.Length) continue;

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
                continue;
            }

            result.Add(new RawChunk(regionX * 32 + localX, regionZ * 32 + localZ, decompressed));
        }

        return result;
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
