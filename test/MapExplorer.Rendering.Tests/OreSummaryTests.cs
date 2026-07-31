namespace MapExplorer.Rendering.Tests;

public sealed class OreSummaryTests
{
    [Fact]
    public void BuildRecordsTopmostOccurrencePerOreTypePerColumn()
    {
        var (chunk, setBlock) = ChunkTestFixtures.NewChunk();
        setBlock(0, 70, 0, "minecraft:grass_block"); // surface
        setBlock(0, 30, 0, "minecraft:diamond_ore"); // shallower
        setBlock(0, 10, 0, "minecraft:diamond_ore"); // deeper occurrence of the same type

        var summary = OreSummaryBuilder.Build(chunk);

        var hits = Assert.Single(summary.ByColumn); // only column (0,0) has ore
        Assert.Equal(0, hits.Key);
        var hit = Assert.Single(hits.Value); // one entry per distinct ore type, not per block
        Assert.Equal(30, hit.Y);
        Assert.Equal(Colors.OreBlockIndex["minecraft:diamond_ore"], hit.OreType);
    }

    [Fact]
    public void BuildRecordsOneEntryPerDistinctOreTypeInAColumn()
    {
        var (chunk, setBlock) = ChunkTestFixtures.NewChunk();
        setBlock(0, 70, 0, "minecraft:grass_block");
        setBlock(0, 40, 0, "minecraft:iron_ore");
        setBlock(0, 20, 0, "minecraft:diamond_ore");

        var summary = OreSummaryBuilder.Build(chunk);

        var hits = summary.ByColumn[0];
        Assert.Equal(2, hits.Length);
        Assert.Contains(hits, h => h.OreType == Colors.OreBlockIndex["minecraft:iron_ore"] && h.Y == 40);
        Assert.Contains(hits, h => h.OreType == Colors.OreBlockIndex["minecraft:diamond_ore"] && h.Y == 20);
    }

    [Fact]
    public void BuildProducesNoColumnEntryWhenThereIsNoOre()
    {
        var (chunk, setBlock) = ChunkTestFixtures.NewChunk();
        setBlock(0, 70, 0, "minecraft:grass_block");
        setBlock(0, 60, 0, "minecraft:stone");

        var summary = OreSummaryBuilder.Build(chunk);

        Assert.Empty(summary.ByColumn);
    }

    [Fact]
    public void SummaryOverloadOreTintMatchesFullChunkOverlay()
    {
        var (chunk, setBlock) = ChunkTestFixtures.NewChunk();
        setBlock(0, 70, 0, "minecraft:grass_block");
        setBlock(0, 20, 0, "minecraft:diamond_ore");

        var config = new LayerConfig
        {
            Mode = LayerMode.Surface,
            OreOverlay = true,
            OreFilter = new HashSet<string> { "minecraft:diamond_ore" }
        };

        var blockNames = new NamePalette();
        var biomeNames = new NamePalette();
        var chunkSummary = ChunkSummaryBuilder.Build(chunk, blockNames, biomeNames);
        var oreSummary = OreSummaryBuilder.Build(chunk);

        var fromFullChunk = ChunkRenderer.GetChunkPixelColor(chunk, config, 0, 0);
        var fromSummary = ChunkRenderer.GetChunkPixelColor(chunkSummary, blockNames, biomeNames, config, 0, 0, oreSummary);

        Assert.Equal(fromFullChunk, fromSummary);
    }

    [Fact]
    public void SummaryOverloadWithoutOreSummaryLeavesColorUntinted()
    {
        var (chunk, setBlock) = ChunkTestFixtures.NewChunk();
        setBlock(0, 70, 0, "minecraft:grass_block");
        setBlock(0, 20, 0, "minecraft:diamond_ore");

        var config = new LayerConfig
        {
            Mode = LayerMode.Surface,
            OreOverlay = true,
            OreFilter = new HashSet<string> { "minecraft:diamond_ore" }
        };

        var blockNames = new NamePalette();
        var biomeNames = new NamePalette();
        var chunkSummary = ChunkSummaryBuilder.Build(chunk, blockNames, biomeNames);

        var plain = ChunkRenderer.GetChunkPixelColor(chunkSummary, blockNames, biomeNames, config with { OreOverlay = false }, 0, 0);
        var withNullOreSummary = ChunkRenderer.GetChunkPixelColor(chunkSummary, blockNames, biomeNames, config, 0, 0);

        Assert.Equal(plain, withNullOreSummary);
    }

    [Fact]
    public void SliceOverloadAppliesOreTintFromTheFullColumnEvenThoughTheSliceItselfIsOneSection()
    {
        // Ore lives at Y=20 (section 1: world Y 16-31), the slice being displayed is section 0
        // (world Y 0-15) — the tint must come from the chunk-wide OreSummary, not the slice.
        var (chunk, setChunkBlock) = ChunkTestFixtures.NewChunk();
        setChunkBlock(0, 20, 0, "minecraft:diamond_ore");
        var oreSummary = OreSummaryBuilder.Build(chunk);

        var (slice, setSliceBlock) = ChunkTestFixtures.NewSlice(sectionY: 0);
        setSliceBlock(0, 10, 0, "minecraft:stone");
        var config = new LayerConfig
        {
            Mode = LayerMode.Slice,
            SliceY = 10,
            OreOverlay = true,
            OreFilter = new HashSet<string> { "minecraft:diamond_ore" }
        };

        var tinted = ChunkRenderer.GetChunkPixelColor(slice, config, 0, 0, oreSummary);
        var untinted = ChunkRenderer.GetChunkPixelColor(slice, config, 0, 0);

        Assert.NotEqual(untinted, tinted);
    }

    [Fact]
    public void FindDominantOreReturnsNullWhenOreOverlayIsOff()
    {
        var (chunk, setBlock) = ChunkTestFixtures.NewChunk();
        setBlock(0, 20, 0, "minecraft:diamond_ore");
        var oreSummary = OreSummaryBuilder.Build(chunk);

        var config = new LayerConfig { OreOverlay = false, OreFilter = new HashSet<string> { "minecraft:diamond_ore" } };

        Assert.Null(ChunkRenderer.FindDominantOre(oreSummary, config));
    }

    [Fact]
    public void FindDominantOreIgnoresOresNotInTheFilterEvenIfShallower()
    {
        var (chunk, setBlock) = ChunkTestFixtures.NewChunk();
        setBlock(0, 60, 0, "minecraft:coal_ore"); // shallower but not selected
        setBlock(1, 20, 0, "minecraft:diamond_ore"); // deeper but selected
        var oreSummary = OreSummaryBuilder.Build(chunk);

        var config = new LayerConfig { OreOverlay = true, OreFilter = new HashSet<string> { "minecraft:diamond_ore" } };

        Assert.Equal(Colors.OreBlocks["minecraft:diamond_ore"], ChunkRenderer.FindDominantOre(oreSummary, config));
    }

    [Fact]
    public void FindDominantOrePicksTheShallowestMatchingHitAcrossTheWholeChunk()
    {
        var (chunk, setBlock) = ChunkTestFixtures.NewChunk();
        setBlock(0, 20, 0, "minecraft:diamond_ore");
        setBlock(5, 50, 5, "minecraft:iron_ore"); // shallower, also selected
        var oreSummary = OreSummaryBuilder.Build(chunk);

        var config = new LayerConfig
        {
            OreOverlay = true,
            OreFilter = new HashSet<string> { "minecraft:diamond_ore", "minecraft:iron_ore" }
        };

        Assert.Equal(Colors.OreBlocks["minecraft:iron_ore"], ChunkRenderer.FindDominantOre(oreSummary, config));
    }
}
