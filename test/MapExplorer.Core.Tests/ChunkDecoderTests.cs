using MapExplorer.Core.Chunk;
using static MapExplorer.Core.Tests.NbtFixtureBuilder;

namespace MapExplorer.Core.Tests;

// Exhaustive correctness test for the packed-long bit-unpacking in
// ChunkDecoder — the exact class of bug that bit us during the Avalonia
// prototype (a signed/unsigned NBT byte read that silently dropped every
// underground section). Mirrors the Electron app's decode-pipeline.test.ts,
// including the specific bit-widths chosen to force entries that straddle a
// 32-bit boundary within a 64-bit long.
public sealed class ChunkDecoderTests
{
    // ceil(log2(300))=9 bits — forces entries straddling the 32-bit boundary
    // within a 64-bit long (e.g. entry index 3: bitIdx=27, occupies bits 27-35).
    private static readonly List<string> BigBlockPalette =
        Enumerable.Range(0, 300).Select(i => $"minecraft:test_block_{i}").ToList();

    // ceil(log2(6))=3 bits — also produces straddling entries (e.g. index 10: bitIdx=30-32).
    private static readonly List<string> BiomePalette =
        ["minecraft:plains", "minecraft:desert", "minecraft:forest", "minecraft:swamp", "minecraft:taiga", "minecraft:jungle"];

    private static ChunkData BuildChunkA()
    {
        var bottomSection = new SectionFixture(Y: -4, BlockPalette: ["minecraft:bedrock"]);

        var packedSection = new SectionFixture(
            Y: 0,
            BlockPalette: BigBlockPalette,
            BlockIndices: Enumerable.Range(0, 4096).Select(i => i % BigBlockPalette.Count).ToArray(),
            BiomePalette: BiomePalette,
            BiomeIndices: Enumerable.Range(0, 64).Select(i => i % BiomePalette.Count).ToArray());

        var topSection = new SectionFixture(Y: 19, BlockPalette: ["minecraft:air"], BiomePalette: ["minecraft:the_end"]);

        var nbt = BuildChunkNbt([bottomSection, packedSection, topSection]);
        return ChunkDecoder.Decode(nbt, chunkX: 0, chunkZ: 0);
    }

    private static readonly List<string> SmallPalette = Enumerable.Range(0, 20).Select(i => $"minecraft:small_{i}").ToList();

    private static ChunkData BuildChunkB()
    {
        var otherSection = new SectionFixture(
            Y: 2,
            BlockPalette: SmallPalette,
            BlockIndices: Enumerable.Range(0, 4096).Select(i => i % SmallPalette.Count).ToArray());
            // no biomes tag at all — exercises the "missing biomes" fallback path

        var nbt = BuildChunkNbt([otherSection]);
        return ChunkDecoder.Decode(nbt, chunkX: 5, chunkZ: 3);
    }

    [Fact]
    public void DecodesUniformSingleBlockPaletteSection()
    {
        var chunk = BuildChunkA();
        foreach (var (x, y, z) in new[] { (0, -64, 0), (15, -55, 15), (7, -49, 3) })
        {
            Assert.Equal("minecraft:bedrock", chunk.GetBlock(x, y, z));
        }
    }

    [Fact]
    public void DecodesUniformSingleBiomePaletteSectionAtTopOfWorld()
    {
        var chunk = BuildChunkA();
        Assert.Equal("minecraft:the_end", chunk.GetBiome(0, 315, 0));
    }

    [Fact]
    public void ExhaustivelyDecodesEveryPackedBlockEntry_BitsPerBlock9_IncludingStraddlingEntries()
    {
        var chunk = BuildChunkA();
        var indices = Enumerable.Range(0, 4096).Select(i => i % BigBlockPalette.Count).ToArray();

        for (int i = 0; i < 4096; i++)
        {
            int bx = i & 15;
            int bz = (i >> 4) & 15;
            int by = (i >> 8) & 15;
            int worldY = 0 * 16 + by; // section Y=0
            string expected = BigBlockPalette[indices[i]];
            Assert.Equal(expected, chunk.GetBlock(bx, worldY, bz));
        }
    }

    [Fact]
    public void ExhaustivelyDecodesEveryPackedBiomeEntry_BitsPerBiome3_IncludingStraddlingEntries()
    {
        var chunk = BuildChunkA();
        var indices = Enumerable.Range(0, 64).Select(i => i % BiomePalette.Count).ToArray();

        for (int i = 0; i < 64; i++)
        {
            int bx = i & 3;
            int bz = (i >> 2) & 3;
            int by = (i >> 4) & 3;
            int biomeY = (0 + 4) * 4 + by; // section Y=0
            int worldY = biomeY * 4 - 64;
            string expected = BiomePalette[indices[i]];
            Assert.Equal(expected, chunk.GetBiome(bx * 4, worldY, bz * 4));
        }
    }

    [Fact]
    public void DecodesSecondChunkWithDifferentPaletteSize_BitsPerBlock5_AtItsOwnCoordinates()
    {
        var chunk = BuildChunkB();
        var indices = Enumerable.Range(0, 4096).Select(i => i % SmallPalette.Count).ToArray();

        for (int i = 0; i < 4096; i++)
        {
            int bx = i & 15;
            int bz = (i >> 4) & 15;
            int by = (i >> 8) & 15;
            int worldY = 2 * 16 + by;
            Assert.Equal(SmallPalette[indices[i]], chunk.GetBlock(bx, worldY, bz));
        }
    }

    [Fact]
    public void FallsBackToDefaultBiomeWhenSectionHasNoBiomesTag()
    {
        var chunk = BuildChunkB();
        Assert.Equal("minecraft:plains", chunk.GetBiome(3, 40, 9));
    }

    [Fact]
    public void CollectsDistinctPaletteEntriesOnTheDecodedChunk()
    {
        var chunk = BuildChunkA();
        Assert.Contains("minecraft:bedrock", chunk.Palette);
        Assert.True(chunk.Palette.Count >= BigBlockPalette.Count);
        Assert.All(BiomePalette, b => Assert.Contains(b, chunk.BiomePalette));
    }

    [Theory]
    [InlineData(1)]  // 1 bit — minimum biome width
    [InlineData(4)]  // minimum block width, no packing math needed
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(9)]  // straddles 32-bit boundary at several indices
    [InlineData(12)]
    [InlineData(15)] // close to max realistic width, valuesPerLong=4
    public void PackedEntrySweepAcrossBitWidthsRoundTripsExactly(int bitsPerEntry)
    {
        // Directly exercises PackLongArray + ChunkDecoder's unpack path across a
        // sweep of bit widths without needing a huge palette for each — build a
        // palette exactly large enough to require this many bits, then verify
        // every one of 4096 packed entries decodes back to what was packed.
        int paletteSize = 1 << bitsPerEntry; // guarantees ceil(log2(paletteSize)) == bitsPerEntry (or the block minimum of 4)
        var palette = Enumerable.Range(0, paletteSize).Select(i => $"minecraft:sweep_{bitsPerEntry}_{i}").ToList();
        var indices = Enumerable.Range(0, 4096).Select(i => i % paletteSize).ToArray();
        var section = new SectionFixture(Y: 0, BlockPalette: palette, BlockIndices: indices);

        var nbt = BuildChunkNbt([section]);
        var chunk = ChunkDecoder.Decode(nbt, chunkX: 0, chunkZ: 0);

        for (int i = 0; i < 4096; i++)
        {
            int bx = i & 15;
            int bz = (i >> 4) & 15;
            int by = (i >> 8) & 15;
            Assert.Equal(palette[indices[i]], chunk.GetBlock(bx, by, bz));
        }
    }
}
