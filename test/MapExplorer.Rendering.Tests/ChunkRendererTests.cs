using MapExplorer.Core.Chunk;

namespace MapExplorer.Rendering.Tests;

public sealed class ChunkRendererTests
{
    private static (ChunkData chunk, Action<int, int, int, string> setBlock) NewChunk() => ChunkTestFixtures.NewChunk();

    [Fact]
    public void FindSurfaceYReturnsTopmostNonAirBlock()
    {
        var (chunk, setBlock) = NewChunk();
        setBlock(0, 60, 0, "minecraft:stone");
        setBlock(0, 70, 0, "minecraft:dirt");
        // everything above y=70 stays air

        Assert.Equal(70, ChunkRenderer.FindSurfaceY(chunk, 0, 0));
    }

    [Fact]
    public void FindSurfaceYReturnsBottomOfWorldWhenColumnIsEntirelyAir()
    {
        var (chunk, _) = NewChunk();

        Assert.Equal(-64, ChunkRenderer.FindSurfaceY(chunk, 5, 5));
    }

    [Fact]
    public void SliceModeAlwaysDisplaysConfiguredY()
    {
        var config = new LayerConfig { Mode = LayerMode.Slice, SliceY = 42 };
        var (chunk, _) = NewChunk();

        Assert.Equal(42, ChunkRenderer.FindDisplayY(chunk, config, 0, 0));
    }

    [Fact]
    public void SliceModeShowsBlockAtThatExactY()
    {
        var (chunk, setBlock) = NewChunk();
        setBlock(3, 10, 3, "minecraft:diamond_block");
        var config = new LayerConfig { Mode = LayerMode.Slice, SliceY = 10 };

        var color = ChunkRenderer.GetChunkPixelColor(chunk, config, 3, 3);

        Assert.Equal(Colors.GetBlockColor("minecraft:diamond_block"), color);
    }

    [Fact]
    public void SliceOverloadMatchesFullChunkOverload_ForTheSameY()
    {
        var (chunk, setBlock) = NewChunk();
        setBlock(3, 10, 3, "minecraft:diamond_block");
        var config = new LayerConfig { Mode = LayerMode.Slice, SliceY = 10 };

        // SliceY=10 falls in section 0 (world Y 0-15), local Y = 10.
        var (slice, setSliceBlock) = ChunkTestFixtures.NewSlice(sectionY: 0);
        setSliceBlock(3, 10, 3, "minecraft:diamond_block");

        var fromFullChunk = ChunkRenderer.GetChunkPixelColor(chunk, config, 3, 3);
        var fromSlice = ChunkRenderer.GetChunkPixelColor(slice, config, 3, 3);

        Assert.Equal(fromFullChunk, fromSlice);
    }

    [Fact]
    public void SliceOverloadMapsWorldYIntoTheSectionsLocalRange()
    {
        // World Y=-59 is section -4 (world Y -64..-49), local Y = -59 - (-4*16) = 5.
        var (slice, setSliceBlock) = ChunkTestFixtures.NewSlice(sectionY: -4);
        setSliceBlock(0, 5, 0, "minecraft:deepslate");
        var config = new LayerConfig { Mode = LayerMode.Slice, SliceY = -59 };

        var color = ChunkRenderer.GetChunkPixelColor(slice, config, 0, 0);

        Assert.Equal(Colors.GetBlockColor("minecraft:deepslate"), color);
    }

    [Fact]
    public void OreOverlayBlendsOreColorIntoBaseColorWhenPresentInColumn()
    {
        var (chunk, setBlock) = NewChunk();
        setBlock(0, 70, 0, "minecraft:grass_block"); // surface
        setBlock(0, 20, 0, "minecraft:diamond_ore"); // buried ore

        var config = new LayerConfig
        {
            Mode = LayerMode.Surface,
            OreOverlay = true,
            OreFilter = new HashSet<string> { "minecraft:diamond_ore" }
        };

        var color = ChunkRenderer.GetChunkPixelColor(chunk, config, 0, 0);
        var plainSurfaceColor = ChunkRenderer.GetChunkPixelColor(chunk, config with { OreOverlay = false }, 0, 0);

        Assert.NotEqual(plainSurfaceColor, color);
    }

    [Fact]
    public void OreOverlayLeavesColorUnchangedWhenOreNotInFilter()
    {
        var (chunk, setBlock) = NewChunk();
        setBlock(0, 70, 0, "minecraft:grass_block");
        setBlock(0, 20, 0, "minecraft:diamond_ore");

        var withEmptyFilter = new LayerConfig { Mode = LayerMode.Surface, OreOverlay = true, OreFilter = new HashSet<string>() };
        var withOverlayOff = new LayerConfig { Mode = LayerMode.Surface, OreOverlay = false };

        Assert.Equal(
            ChunkRenderer.GetChunkPixelColor(chunk, withOverlayOff, 0, 0),
            ChunkRenderer.GetChunkPixelColor(chunk, withEmptyFilter, 0, 0));
    }

    [Fact]
    public void HeightmapModeUsesGradientAtSurfaceY()
    {
        var (chunk, setBlock) = NewChunk();
        setBlock(0, 100, 0, "minecraft:stone");
        var config = new LayerConfig { Mode = LayerMode.Heightmap };

        var color = ChunkRenderer.GetChunkPixelColor(chunk, config, 0, 0);

        Assert.Equal(Colors.GetHeightColor(100), color);
    }

    [Fact]
    public void BiomeModeReadsBiomeAtSurfaceY()
    {
        var (chunk, setBlock) = NewChunk();
        setBlock(0, 70, 0, "minecraft:grass_block");
        // Chunk's default BiomePalette is ["minecraft:plains"], BiomeIndices all
        // zero-initialized -> every column reads as plains.
        var config = new LayerConfig { Mode = LayerMode.Biome };

        var color = ChunkRenderer.GetChunkPixelColor(chunk, config, 0, 0);

        Assert.Equal(Colors.GetBiomeColor("minecraft:plains"), color);
    }

    [Theory]
    [InlineData("minecraft:snow", 90)]
    [InlineData("minecraft:snow_block", 90)]
    [InlineData("minecraft:ice", 64)]
    [InlineData("minecraft:packed_ice", 64)]
    [InlineData("minecraft:powder_snow", 90)]
    public void SurfaceModeRendersSnowAndIceAsLightNotDark(string blockName, int surfaceY)
    {
        var (chunk, setBlock) = NewChunk();
        setBlock(0, surfaceY, 0, blockName);
        var config = new LayerConfig { Mode = LayerMode.Surface };

        var color = ChunkRenderer.GetChunkPixelColor(chunk, config, 0, 0);

        // All snow/ice colors in the table have every channel >= 100; shading
        // at these Ys should keep them clearly light, not "black-ish".
        Assert.True(color.R > 80 && color.G > 80 && color.B > 80,
            $"{blockName} at y={surfaceY} rendered as ({color.R},{color.G},{color.B}), expected a light color");
    }

    [Fact]
    public void DimForBelowLayerDarkensButPreservesHue()
    {
        var color = new Rgb(200, 100, 50);

        var dimmed = ChunkRenderer.DimForBelowLayer(color);

        Assert.True(dimmed.R < color.R && dimmed.G < color.G && dimmed.B < color.B);
        // Relative ordering between channels should survive dimming, so distinct blocks
        // still read as distinct colors, just darker.
        Assert.True(dimmed.R > dimmed.G && dimmed.G > dimmed.B);
    }

    [Fact]
    public void DimForBelowLayerKeepsBlackAtBlack()
    {
        Assert.Equal(new Rgb(0, 0, 0), ChunkRenderer.DimForBelowLayer(new Rgb(0, 0, 0)));
    }
}
