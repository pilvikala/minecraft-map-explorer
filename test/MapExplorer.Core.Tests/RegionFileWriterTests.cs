using System.Buffers.Binary;
using System.IO.Compression;
using MapExplorer.Core.Region;
using static MapExplorer.Core.Tests.NbtFixtureBuilder;

namespace MapExplorer.Core.Tests;

public sealed class RegionFileWriterTests
{
    [Fact]
    public void Rebuild_AppliesPatchedChunk_AndLeavesUntouchedChunksByteIdentical()
    {
        var untouchedNbt = BuildChunkNbt([new SectionFixture(Y: 0, BlockPalette: ["minecraft:stone"])]);
        var originalNbt = BuildChunkNbt([new SectionFixture(Y: 0, BlockPalette: ["minecraft:dirt"])]);
        var patchedNbt = BuildChunkNbt([new SectionFixture(Y: 0, BlockPalette: ["minecraft:diamond_block"])]);

        var original = RegionFileFixtureBuilder.BuildRegion(
        [
            (LocalX: 0, LocalZ: 0, Nbt: untouchedNbt),
            (LocalX: 5, LocalZ: 5, Nbt: originalNbt)
        ]);

        var rebuilt = RegionFileWriter.Rebuild(original,
            new Dictionary<(int, int), byte[]> { [(5, 5)] = patchedNbt },
            timestampSeconds: 1_700_000_000);

        var untouchedChunk = RegionFile.ParseChunk(rebuilt, "r.0.0.mca", 0, 0);
        Assert.NotNull(untouchedChunk);
        Assert.Equal(untouchedNbt, untouchedChunk.Value.Data);

        var patchedChunk = RegionFile.ParseChunk(rebuilt, "r.0.0.mca", 5, 5);
        Assert.NotNull(patchedChunk);
        Assert.Equal(patchedNbt, patchedChunk.Value.Data);
    }

    [Fact]
    public void Rebuild_PreservesEmptySlots()
    {
        var nbt = BuildChunkNbt([new SectionFixture(Y: 0, BlockPalette: ["minecraft:stone"])]);
        var original = RegionFileFixtureBuilder.BuildRegion([(LocalX: 3, LocalZ: 3, Nbt: nbt)]);

        var rebuilt = RegionFileWriter.Rebuild(original, new Dictionary<(int, int), byte[]>(), timestampSeconds: 0);

        Assert.Null(RegionFile.ParseChunk(rebuilt, "r.0.0.mca", 0, 0));
        Assert.NotNull(RegionFile.ParseChunk(rebuilt, "r.0.0.mca", 3, 3));
    }

    [Fact]
    public void Rebuild_CanAddANewChunkNotPresentInTheOriginal()
    {
        var original = RegionFileFixtureBuilder.BuildRegion([]);
        var newNbt = BuildChunkNbt([new SectionFixture(Y: 0, BlockPalette: ["minecraft:grass_block"])]);

        var rebuilt = RegionFileWriter.Rebuild(original,
            new Dictionary<(int, int), byte[]> { [(7, 9)] = newNbt },
            timestampSeconds: 123);

        var chunk = RegionFile.ParseChunk(rebuilt, "r.0.0.mca", 7, 9);
        Assert.NotNull(chunk);
        Assert.Equal(newNbt, chunk.Value.Data);
    }

    [Fact]
    public void Rebuild_ThrowsInsteadOfSilentlyCorruptingTheFile_WhenAChunkNeedsMoreThan255Sectors()
    {
        var original = RegionFileFixtureBuilder.BuildRegion([]);

        // Random (so effectively incompressible) payload comfortably past the 255-sector
        // (~1,044,480-byte) ceiling a single entry's 1-byte sectorCount field can hold — a real
        // .mca writer would spill this into a separate .mcc file; this one doesn't support that and
        // must fail loudly rather than let sectorCount overflow into the offset field and corrupt
        // both this entry and, via bit overlap, its neighbors.
        var oversized = new byte[1_100_000];
        Random.Shared.NextBytes(oversized);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            RegionFileWriter.Rebuild(original, new Dictionary<(int, int), byte[]> { [(1, 1)] = oversized }, timestampSeconds: 0));

        Assert.Contains("255", ex.Message);
    }

    [Fact]
    public void Rebuild_ReadsAnUntouchedEntry_WhoseRealBytesEndExactlyAtTheBufferBoundary()
    {
        // RegionFileFixtureBuilder always pads every entry out to a full 4096-byte sector, so it can
        // never exercise this: an entry whose actual (unpadded) byte range ends precisely at
        // buffer.Length, with zero bytes of trailing sector padding. Built by hand instead, to pin
        // down the exact off-by-one boundary condition ReadRawEntryBytes' bounds check must accept.
        var nbt = BuildChunkNbt([new SectionFixture(Y: 0, BlockPalette: ["minecraft:stone"])]);

        using var compressedStream = new MemoryStream();
        using (var zlib = new ZLibStream(compressedStream, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(nbt);
        var compressed = compressedStream.ToArray();

        var entry = new byte[5 + compressed.Length];
        BinaryPrimitives.WriteUInt32BigEndian(entry, (uint)(compressed.Length + 1));
        entry[4] = 2; // zlib
        compressed.CopyTo(entry, 5);

        var original = new byte[8192 + entry.Length]; // header (offsets + timestamps) + the entry, no padding after it
        BinaryPrimitives.WriteUInt32BigEndian(original.AsSpan(0, 4), (2u << 8) | 1u); // slot (0,0): sector 2, count 1
        entry.CopyTo(original, 8192);

        // Patch an unrelated slot so slot (0,0) goes through the verbatim-copy path being tested.
        var otherNbt = BuildChunkNbt([new SectionFixture(Y: 0, BlockPalette: ["minecraft:dirt"])]);
        var rebuilt = RegionFileWriter.Rebuild(original,
            new Dictionary<(int, int), byte[]> { [(5, 5)] = otherNbt },
            timestampSeconds: 0);

        var chunk = RegionFile.ParseChunk(rebuilt, "r.0.0.mca", 0, 0);
        Assert.NotNull(chunk);
        Assert.Equal(nbt, chunk.Value.Data);
    }
}
