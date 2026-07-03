using MapExplorer.Rendering;

namespace MapExplorer.Rendering.Tests;

// The dye/shape-suffix fallback logic in Colors.cs is fiddly string matching
// that had no test coverage at all in the Electron app — closing that gap
// here, not just porting.
public sealed class ColorsTests
{
    [Fact]
    public void DirectBlockLookupReturnsExactColor()
    {
        var color = Colors.GetBlockColor("minecraft:grass_block");
        Assert.Equal(new Rgb(86, 156, 55), color);
    }

    [Theory]
    [InlineData("minecraft:red_wool")]
    [InlineData("minecraft:blue_concrete")]
    [InlineData("minecraft:light_gray_stained_glass_pane")]
    [InlineData("minecraft:lime_carpet")]
    [InlineData("minecraft:orange_bed")]
    public void DyeSuffixResolvesToDyeColor(string blockName)
    {
        var color = Colors.GetBlockColor(blockName);
        Assert.NotEqual(new Rgb(128, 128, 128), color); // not the fallback
    }

    [Fact]
    public void DyePrefixWithoutKnownSuffixFallsThroughToShapeOrFallback()
    {
        // "minecraft:red_" is a real dye prefix, but "_sandstone" isn't in
        // DYE_BLOCK_SUFFIXES, so this must NOT match as a dye block.
        var color = Colors.GetBlockColor("minecraft:red_nonexistent_block_xyz");
        Assert.Equal(new Rgb(128, 128, 128), color);
    }

    [Theory]
    [InlineData("minecraft:stone_stairs", "minecraft:stone")]
    [InlineData("minecraft:oak_planks_slab", "minecraft:oak_planks")]
    [InlineData("minecraft:cobblestone_wall", "minecraft:cobblestone")]
    public void ShapeSuffixInheritsExactBaseBlockColor(string shapeName, string baseName)
    {
        Assert.Equal(Colors.GetBlockColor(baseName), Colors.GetBlockColor(shapeName));
    }

    [Fact]
    public void ShapeSuffixWithPluralizedBaseNameResolves()
    {
        // "stone_brick_wall" -> base "stone_brick" isn't a direct key, but
        // "stone_brick" + "s" = "stone_bricks" is.
        var color = Colors.GetBlockColor("minecraft:stone_brick_wall");
        Assert.Equal(Colors.GetBlockColor("minecraft:stone_bricks"), color);
    }

    [Theory]
    [InlineData("minecraft:oak_door", "minecraft:oak_planks")]
    [InlineData("minecraft:crimson_fence", "minecraft:crimson_planks")]
    [InlineData("minecraft:quartz_stairs", "minecraft:quartz_block")]
    [InlineData("minecraft:purpur_slab", "minecraft:purpur_block")]
    public void ShapeSuffixUsesOverrideMapWhenBaseNameDiffersFromBlock(string shapeName, string expectedBaseBlock)
    {
        Assert.Equal(Colors.GetBlockColor(expectedBaseBlock), Colors.GetBlockColor(shapeName));
    }

    [Fact]
    public void UnknownBlockFallsBackToGray()
    {
        Assert.Equal(new Rgb(128, 128, 128), Colors.GetBlockColor("minecraft:totally_made_up_block"));
    }

    [Fact]
    public void UnknownBiomeFallsBackToDefaultGreen()
    {
        Assert.Equal(new Rgb(100, 150, 100), Colors.GetBiomeColor("minecraft:not_a_real_biome"));
    }

    [Fact]
    public void KnownBiomeReturnsExactColor()
    {
        Assert.Equal(new Rgb(80, 165, 60), Colors.GetBiomeColor("minecraft:plains"));
    }

    [Theory]
    [InlineData(-64)] // deepest underground
    [InlineData(64)] // sea level-ish
    [InlineData(200)] // hills
    [InlineData(319)] // build limit
    public void HeightColorNeverThrowsAcrossFullYRange(int y)
    {
        // Just verifying no exceptions/out-of-range math across the whole
        // legal Y range — the gradient's exact values aren't asserted since
        // they're a deliberate visual choice, not a correctness contract.
        var color = Colors.GetHeightColor(y);
        Assert.True(color.R >= 0 && color.G >= 0 && color.B >= 0);
    }

    [Fact]
    public void AirBlocksSetContainsAllThreeAirVariants()
    {
        Assert.Contains("minecraft:air", Colors.AirBlocks);
        Assert.Contains("minecraft:cave_air", Colors.AirBlocks);
        Assert.Contains("minecraft:void_air", Colors.AirBlocks);
    }
}
