using MapExplorer.Core.Chunk;

namespace MapExplorer.Rendering.Tests;

public sealed class ChunkRendererTests
{
    // Builds a chunk with an entirely air interior, then lets the caller poke
    // specific blocks in via SetBlock — much simpler than round-tripping
    // through NBT bytes for tests that only care about the render-logic layer
    // (findSurfaceY/findCaveFloorY/getChunkPixelColor), not the decoder.
    private static (ChunkData chunk, Action<int, int, int, string> setBlock) NewChunk()
    {
        var palette = new List<string> { "minecraft:air" };
        var paletteIndex = new Dictionary<string, int> { ["minecraft:air"] = 0 };
        var blocks = new ushort[16 * ChunkData.ChunkHeight * 16];
        var chunk = new ChunkData
        {
            ChunkX = 0,
            ChunkZ = 0,
            Blocks = blocks,
            Palette = palette,
            BiomePalette = ["minecraft:plains"],
            BiomeIndices = new byte[4 * (ChunkData.ChunkHeight / 4) * 4]
        };

        void SetBlock(int lx, int y, int lz, string name)
        {
            if (!paletteIndex.TryGetValue(name, out int idx))
            {
                idx = palette.Count;
                palette.Add(name);
                paletteIndex[name] = idx;
            }
            int yi = y + ChunkData.YOffset;
            blocks[lx * ChunkData.ChunkHeight * 16 + yi * 16 + lz] = (ushort)idx;
        }

        return (chunk, SetBlock);
    }

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
    public void CaveModeFindsAirPocketBelowSurfaceAndShowsFloorBlock()
    {
        // The algorithm scans down from the surface and returns the position
        // just below the FIRST air block it finds — i.e. it reports whatever
        // is directly beneath the topmost gap, not the true bottom of a tall
        // cave. So a one-block air gap at y=69 with stone at y=68 reports 68.
        var (chunk, setBlock) = NewChunk();
        setBlock(0, 68, 0, "minecraft:stone"); // reported "floor" (one below the air gap)
        // y=69 stays air (the one-block gap directly under the surface)
        setBlock(0, 70, 0, "minecraft:grass_block"); // surface

        var config = new LayerConfig { Mode = LayerMode.Cave };
        int displayY = ChunkRenderer.FindDisplayY(chunk, config, 0, 0);

        Assert.Equal(68, displayY);
        var color = ChunkRenderer.GetChunkPixelColor(chunk, config, 0, 0);
        Assert.Equal(Colors.GetBlockColor("minecraft:stone"), color);
    }

    [Fact]
    public void CaveModeWithNoCaveFallsBackToDimmedSurfaceColor()
    {
        var (chunk, setBlock) = NewChunk();
        // Solid column all the way down, no air pocket below the surface.
        for (int y = -64; y <= 70; y++) setBlock(0, y, 0, "minecraft:stone");

        var config = new LayerConfig { Mode = LayerMode.Cave };
        var color = ChunkRenderer.GetChunkPixelColor(chunk, config, 0, 0);

        var stoneColor = Colors.GetBlockColor("minecraft:stone");
        var expectedDimmed = new Rgb(
            (byte)Math.Round(stoneColor.R * 0.3),
            (byte)Math.Round(stoneColor.G * 0.3),
            (byte)Math.Round(stoneColor.B * 0.3));
        Assert.Equal(expectedDimmed, color);
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
}
