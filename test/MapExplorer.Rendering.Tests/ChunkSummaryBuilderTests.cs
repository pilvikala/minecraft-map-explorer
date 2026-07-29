namespace MapExplorer.Rendering.Tests;

public sealed class ChunkSummaryBuilderTests
{
    [Fact]
    public void BuildCapturesSurfaceYAndTopBlockPerColumn()
    {
        var (chunk, setBlock) = ChunkTestFixtures.NewChunk();
        setBlock(0, 60, 0, "minecraft:stone");
        setBlock(0, 70, 0, "minecraft:grass_block"); // topmost at (0,0)
        setBlock(5, 40, 5, "minecraft:deepslate"); // topmost at (5,5), everything else air

        var blockNames = new NamePalette();
        var biomeNames = new NamePalette();
        var summary = ChunkSummaryBuilder.Build(chunk, blockNames, biomeNames);

        int idx00 = 0 * 16 + 0;
        Assert.Equal(70, summary.SurfaceY[idx00]);
        Assert.Equal("minecraft:grass_block", blockNames[summary.TopBlockIndex[idx00]]);

        int idx55 = 5 * 16 + 5;
        Assert.Equal(40, summary.SurfaceY[idx55]);
        Assert.Equal("minecraft:deepslate", blockNames[summary.TopBlockIndex[idx55]]);

        int idxEmpty = 1 * 16 + 1;
        Assert.Equal(ChunkRenderer.FindSurfaceY(chunk, 1, 1), summary.SurfaceY[idxEmpty]);
    }

    [Fact]
    public void BuildRecordsBiomeAtTheSurfacePoint()
    {
        var (chunk, setBlock) = ChunkTestFixtures.NewChunk();
        setBlock(0, 70, 0, "minecraft:grass_block");
        // NewChunk's default BiomePalette is ["minecraft:plains"], all indices zero.

        var blockNames = new NamePalette();
        var biomeNames = new NamePalette();
        var summary = ChunkSummaryBuilder.Build(chunk, blockNames, biomeNames);

        Assert.Equal("minecraft:plains", biomeNames[summary.BiomeIndex[0]]);
    }

    [Fact]
    public void NamePaletteInternsTheSameNameAcrossMultipleChunks()
    {
        var (chunkA, setBlockA) = ChunkTestFixtures.NewChunk();
        setBlockA(0, 70, 0, "minecraft:grass_block");
        var (chunkB, setBlockB) = ChunkTestFixtures.NewChunk();
        setBlockB(0, 70, 0, "minecraft:grass_block");

        var blockNames = new NamePalette();
        var biomeNames = new NamePalette();
        var summaryA = ChunkSummaryBuilder.Build(chunkA, blockNames, biomeNames);
        var summaryB = ChunkSummaryBuilder.Build(chunkB, blockNames, biomeNames);

        Assert.Equal(summaryA.TopBlockIndex[0], summaryB.TopBlockIndex[0]);
    }

    [Theory]
    [InlineData(LayerMode.Surface)]
    [InlineData(LayerMode.Heightmap)]
    [InlineData(LayerMode.Biome)]
    public void SummaryColorMatchesFullChunkColor_ForModesThatDontNeedFullChunkData(LayerMode mode)
    {
        var (chunk, setBlock) = ChunkTestFixtures.NewChunk();
        setBlock(0, 90, 0, "minecraft:snow_block");

        var blockNames = new NamePalette();
        var biomeNames = new NamePalette();
        var summary = ChunkSummaryBuilder.Build(chunk, blockNames, biomeNames);
        var config = new LayerConfig { Mode = mode };

        var fromFullChunk = ChunkRenderer.GetChunkPixelColor(chunk, config, 0, 0);
        var fromSummary = ChunkRenderer.GetChunkPixelColor(summary, blockNames, biomeNames, config, 0, 0);

        Assert.Equal(fromFullChunk, fromSummary);
    }

    [Fact]
    public void GetDataNeedIsSliceForPlainSliceMode()
    {
        Assert.Equal(ChunkDataNeed.Slice, ChunkRenderer.GetDataNeed(new LayerConfig { Mode = LayerMode.Slice }));
    }

    [Fact]
    public void GetDataNeedIsFullWhenOreOverlayIsActive_RegardlessOfMode()
    {
        var config = new LayerConfig { Mode = LayerMode.Surface, OreOverlay = true, OreFilter = new HashSet<string> { "minecraft:diamond_ore" } };
        Assert.Equal(ChunkDataNeed.Full, ChunkRenderer.GetDataNeed(config));
    }

    [Fact]
    public void GetDataNeedIsFullForSliceModeWithOreOverlay()
    {
        var config = new LayerConfig { Mode = LayerMode.Slice, OreOverlay = true, OreFilter = new HashSet<string> { "minecraft:diamond_ore" } };
        Assert.Equal(ChunkDataNeed.Full, ChunkRenderer.GetDataNeed(config));
    }

    [Fact]
    public void GetDataNeedIsSummaryForLightModesWithoutOreOverlay()
    {
        foreach (var mode in new[] { LayerMode.Surface, LayerMode.Heightmap, LayerMode.Biome })
        {
            Assert.Equal(ChunkDataNeed.Summary, ChunkRenderer.GetDataNeed(new LayerConfig { Mode = mode }));
        }
    }
}
