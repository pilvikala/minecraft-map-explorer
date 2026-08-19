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
}
