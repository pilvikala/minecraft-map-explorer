using MapExplorer.Core.Chunk;
using static MapExplorer.Core.Tests.NbtFixtureBuilder;

namespace MapExplorer.Core.Tests;

// DecodeSection is the Slice-mode-only decode path (see ChunkRenderer.GetDataNeed) — it should agree
// exactly with Decode's full-chunk result for whatever one section it targets, since both go through
// the same shared bit-unpack helper (ChunkDecoder.DecodeSectionBlocks).
public sealed class ChunkDecoderSectionTests
{
    private static readonly List<string> BigBlockPalette =
        Enumerable.Range(0, 300).Select(i => $"minecraft:test_block_{i}").ToList();

    [Fact]
    public void DecodeSection_MatchesFullDecode_ForAUniformSection()
    {
        var nbt = BuildChunkNbt([new SectionFixture(Y: -4, BlockPalette: ["minecraft:bedrock"])]);

        var full = ChunkDecoder.Decode(nbt, chunkX: 0, chunkZ: 0);
        var slice = ChunkDecoder.DecodeSection(nbt, chunkX: 0, chunkZ: 0, sectionY: -4);

        for (int lx = 0; lx < 16; lx++)
        for (int lz = 0; lz < 16; lz++)
        for (int localY = 0; localY < 16; localY++)
        {
            int worldY = -4 * 16 + localY;
            Assert.Equal(full.GetBlock(lx, worldY, lz), slice.GetBlock(lx, localY, lz));
        }
    }

    [Fact]
    public void DecodeSection_MatchesFullDecode_ForAPackedSectionRequiringMultipleBits()
    {
        var indices = Enumerable.Range(0, 4096).Select(i => i % BigBlockPalette.Count).ToArray();
        var section = new SectionFixture(Y: 0, BlockPalette: BigBlockPalette, BlockIndices: indices);
        var nbt = BuildChunkNbt([section]);

        var full = ChunkDecoder.Decode(nbt, chunkX: 2, chunkZ: -3);
        var slice = ChunkDecoder.DecodeSection(nbt, chunkX: 2, chunkZ: -3, sectionY: 0);

        for (int i = 0; i < 4096; i++)
        {
            int bx = i & 15, bz = (i >> 4) & 15, by = (i >> 8) & 15;
            Assert.Equal(full.GetBlock(bx, by, bz), slice.GetBlock(bx, by, bz));
        }
    }

    [Fact]
    public void DecodeSection_IgnoresOtherSections()
    {
        var bottom = new SectionFixture(Y: -4, BlockPalette: ["minecraft:bedrock"]);
        var target = new SectionFixture(Y: 0, BlockPalette: ["minecraft:stone"]);
        var top = new SectionFixture(Y: 19, BlockPalette: ["minecraft:air"]);
        var nbt = BuildChunkNbt([bottom, target, top]);

        var slice = ChunkDecoder.DecodeSection(nbt, chunkX: 0, chunkZ: 0, sectionY: 0);

        Assert.Equal("minecraft:stone", slice.GetBlock(0, 0, 0));
        Assert.Equal("minecraft:stone", slice.GetBlock(15, 15, 15));
    }

    [Fact]
    public void DecodeSection_ReturnsAllAir_WhenTheTargetSectionIsAbsent()
    {
        var nbt = BuildChunkNbt([new SectionFixture(Y: 0, BlockPalette: ["minecraft:stone"])]);

        var slice = ChunkDecoder.DecodeSection(nbt, chunkX: 0, chunkZ: 0, sectionY: 5);

        Assert.Equal(5, slice.SectionY);
        Assert.Equal("minecraft:air", slice.GetBlock(0, 0, 0));
    }

    [Fact]
    public void DecodeSection_ReturnsAllAir_OnMalformedNbt()
    {
        var slice = ChunkDecoder.DecodeSection([1, 2, 3], chunkX: 7, chunkZ: 8, sectionY: 0);

        Assert.Equal(7, slice.ChunkX);
        Assert.Equal(8, slice.ChunkZ);
        Assert.Equal("minecraft:air", slice.GetBlock(0, 0, 0));
    }
}
