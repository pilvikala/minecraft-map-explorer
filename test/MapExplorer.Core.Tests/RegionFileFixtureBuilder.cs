using System.Buffers.Binary;
using System.IO.Compression;

namespace MapExplorer.Core.Tests;

/// <summary>Builds minimal valid .mca byte buffers (sector table + zlib-compressed chunk NBT) for
/// testing RegionFile against something closer to the real on-disk format than a bare NBT payload.</summary>
internal static class RegionFileFixtureBuilder
{
    public static byte[] BuildRegion(IReadOnlyList<(int LocalX, int LocalZ, byte[] Nbt)> chunks)
    {
        var sectors = new List<byte[]>();
        var offsetTable = new uint[1024];

        foreach (var (localX, localZ, nbt) in chunks)
        {
            var compressed = Deflate(nbt);
            var payload = new byte[5 + compressed.Length];
            BinaryPrimitives.WriteUInt32BigEndian(payload, (uint)(compressed.Length + 1));
            payload[4] = 2; // zlib
            compressed.CopyTo(payload, 5);

            int sectorCount = (int)Math.Ceiling(payload.Length / 4096.0);
            var padded = new byte[sectorCount * 4096];
            payload.CopyTo(padded, 0);

            int sectorOffset = 2 + sectors.Sum(s => s.Length / 4096); // header occupies sectors 0-1
            sectors.Add(padded);

            int i = localZ * 32 + localX;
            offsetTable[i] = ((uint)sectorOffset << 8) | (uint)sectorCount;
        }

        using var ms = new MemoryStream();
        var header = new byte[8192];
        for (int i = 0; i < 1024; i++) BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(i * 4, 4), offsetTable[i]);
        ms.Write(header);
        foreach (var sector in sectors) ms.Write(sector);
        return ms.ToArray();
    }

    private static byte[] Deflate(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionMode.Compress, leaveOpen: true))
        {
            zlib.Write(raw);
        }
        return output.ToArray();
    }
}
