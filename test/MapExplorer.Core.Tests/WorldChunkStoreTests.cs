using MapExplorer.Core.World;
using static MapExplorer.Core.Tests.NbtFixtureBuilder;

namespace MapExplorer.Core.Tests;

public sealed class WorldChunkStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("wcs-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static byte[] StoneChunkNbt() =>
        BuildChunkNbt([new SectionFixture(Y: 0, BlockPalette: ["minecraft:stone"])]);

    [Fact]
    public void GetOrDecode_DecodesAChunkThatExistsOnDisk()
    {
        var buffer = RegionFileFixtureBuilder.BuildRegion([(LocalX: 1, LocalZ: 1, Nbt: StoneChunkNbt())]);
        File.WriteAllBytes(Path.Combine(_dir, "r.0.0.mca"), buffer);

        var store = new WorldChunkStore(_dir);
        var chunk = store.GetOrDecode(1, 1);

        Assert.NotNull(chunk);
        Assert.Equal("minecraft:stone", chunk!.GetBlock(0, 0, 0));
    }

    [Fact]
    public void GetOrDecode_ReturnsNull_WhenRegionFileDoesNotExist()
    {
        var store = new WorldChunkStore(_dir);
        Assert.Null(store.GetOrDecode(100, 100));
    }

    [Fact]
    public void GetOrDecode_ReturnsNull_WhenTheSpecificChunkIsUngenerated()
    {
        var buffer = RegionFileFixtureBuilder.BuildRegion([(LocalX: 1, LocalZ: 1, Nbt: StoneChunkNbt())]);
        File.WriteAllBytes(Path.Combine(_dir, "r.0.0.mca"), buffer);

        var store = new WorldChunkStore(_dir);
        Assert.Null(store.GetOrDecode(5, 5));
    }

    [Fact]
    public void GetOrDecode_CachesResult_AndDoesNotReReadAChangedFileOnASubsequentHit()
    {
        var path = Path.Combine(_dir, "r.0.0.mca");
        File.WriteAllBytes(path, RegionFileFixtureBuilder.BuildRegion([(LocalX: 1, LocalZ: 1, Nbt: StoneChunkNbt())]));

        var store = new WorldChunkStore(_dir);
        var first = store.GetOrDecode(1, 1);
        Assert.Equal("minecraft:stone", first!.GetBlock(0, 0, 0));

        // Replace the file with one that has no data at that coordinate at all — if GetOrDecode
        // re-read from disk, this second call would return null instead of the cached chunk.
        File.WriteAllBytes(path, RegionFileFixtureBuilder.BuildRegion([]));

        var second = store.GetOrDecode(1, 1);
        Assert.NotNull(second);
        Assert.Equal("minecraft:stone", second!.GetBlock(0, 0, 0));
    }

    [Fact]
    public void GetOrDecode_ComputesTheRegionFileFromChunkCoordinates()
    {
        // Chunk (33, 1) lives in region (1, 0), local (1, 1).
        File.WriteAllBytes(Path.Combine(_dir, "r.1.0.mca"),
            RegionFileFixtureBuilder.BuildRegion([(LocalX: 1, LocalZ: 1, Nbt: StoneChunkNbt())]));

        var store = new WorldChunkStore(_dir);
        var chunk = store.GetOrDecode(33, 1);

        Assert.NotNull(chunk);
        Assert.Equal(33, chunk!.ChunkX);
        Assert.Equal(1, chunk.ChunkZ);
    }

    [Fact]
    public void GetOrDecodeSlice_DecodesJustTheRequestedSection()
    {
        var nbt = BuildChunkNbt(
        [
            new SectionFixture(Y: -4, BlockPalette: ["minecraft:bedrock"]),
            new SectionFixture(Y: 0, BlockPalette: ["minecraft:stone"])
        ]);
        File.WriteAllBytes(Path.Combine(_dir, "r.0.0.mca"), RegionFileFixtureBuilder.BuildRegion([(LocalX: 1, LocalZ: 1, Nbt: nbt)]));

        var store = new WorldChunkStore(_dir);
        var slice = store.GetOrDecodeSlice(1, 1, sectionY: 0);

        Assert.NotNull(slice);
        Assert.Equal(0, slice!.SectionY);
        Assert.Equal("minecraft:stone", slice.GetBlock(0, 0, 0));
    }

    [Fact]
    public void GetOrDecodeSlice_ReturnsNull_WhenRegionFileDoesNotExist()
    {
        var store = new WorldChunkStore(_dir);
        Assert.Null(store.GetOrDecodeSlice(100, 100, sectionY: 0));
    }

    [Fact]
    public void GetOrDecodeSlice_ReturnsAllAirSlice_WhenChunkExistsButSectionDoesNot()
    {
        File.WriteAllBytes(Path.Combine(_dir, "r.0.0.mca"), RegionFileFixtureBuilder.BuildRegion([(LocalX: 1, LocalZ: 1, Nbt: StoneChunkNbt())]));

        var store = new WorldChunkStore(_dir);
        var slice = store.GetOrDecodeSlice(1, 1, sectionY: 10);

        Assert.NotNull(slice);
        Assert.Equal("minecraft:air", slice!.GetBlock(0, 0, 0));
    }

    [Fact]
    public void GetOrDecodeSlice_CachesPerSection_AndDoesNotReReadAChangedFileOnASubsequentHit()
    {
        var path = Path.Combine(_dir, "r.0.0.mca");
        File.WriteAllBytes(path, RegionFileFixtureBuilder.BuildRegion([(LocalX: 1, LocalZ: 1, Nbt: StoneChunkNbt())]));

        var store = new WorldChunkStore(_dir);
        var first = store.GetOrDecodeSlice(1, 1, sectionY: 0);
        Assert.Equal("minecraft:stone", first!.GetBlock(0, 0, 0));

        File.WriteAllBytes(path, RegionFileFixtureBuilder.BuildRegion([]));

        var second = store.GetOrDecodeSlice(1, 1, sectionY: 0);
        Assert.NotNull(second);
        Assert.Equal("minecraft:stone", second!.GetBlock(0, 0, 0));
    }
}
