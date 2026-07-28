using MapExplorer.Core.Region;
using static MapExplorer.Core.Tests.NbtFixtureBuilder;

namespace MapExplorer.Core.Tests;

public sealed class RegionFileTests
{
    private static byte[] SimpleChunkNbt() =>
        BuildChunkNbt([new SectionFixture(Y: 0, BlockPalette: ["minecraft:stone"])]);

    [Fact]
    public void ParseChunk_MatchesParse_ForTheSameCoordinate()
    {
        var nbtA = SimpleChunkNbt();
        var nbtB = BuildChunkNbt([new SectionFixture(Y: 0, BlockPalette: ["minecraft:dirt"])]);
        var buffer = RegionFileFixtureBuilder.BuildRegion([(LocalX: 3, LocalZ: 5, Nbt: nbtA), (LocalX: 10, LocalZ: 20, Nbt: nbtB)]);

        var all = RegionFile.Parse(buffer, "r.0.0.mca");
        var expected = all.Single(c => c.ChunkX == 3 && c.ChunkZ == 5);

        var single = RegionFile.ParseChunk(buffer, "r.0.0.mca", localX: 3, localZ: 5);

        Assert.NotNull(single);
        Assert.Equal(expected.ChunkX, single.Value.ChunkX);
        Assert.Equal(expected.ChunkZ, single.Value.ChunkZ);
        Assert.Equal(expected.Data, single.Value.Data);
    }

    [Fact]
    public void ParseChunk_OffsetsCoordinatesByRegionPosition()
    {
        var buffer = RegionFileFixtureBuilder.BuildRegion([(LocalX: 1, LocalZ: 2, Nbt: SimpleChunkNbt())]);

        var chunk = RegionFile.ParseChunk(buffer, "r.2.-1.mca", localX: 1, localZ: 2);

        Assert.NotNull(chunk);
        Assert.Equal(2 * 32 + 1, chunk.Value.ChunkX);
        Assert.Equal(-1 * 32 + 2, chunk.Value.ChunkZ);
    }

    [Fact]
    public void ParseChunk_ReturnsNull_ForAnUngeneratedChunk()
    {
        var buffer = RegionFileFixtureBuilder.BuildRegion([(LocalX: 3, LocalZ: 5, Nbt: SimpleChunkNbt())]);

        Assert.Null(RegionFile.ParseChunk(buffer, "r.0.0.mca", localX: 0, localZ: 0));
    }

    [Fact]
    public void ParseChunk_ReturnsNull_ForOutOfRangeLocalCoordinates()
    {
        var buffer = RegionFileFixtureBuilder.BuildRegion([(LocalX: 3, LocalZ: 5, Nbt: SimpleChunkNbt())]);

        Assert.Null(RegionFile.ParseChunk(buffer, "r.0.0.mca", localX: 32, localZ: 0));
        Assert.Null(RegionFile.ParseChunk(buffer, "r.0.0.mca", localX: -1, localZ: 0));
    }
}
