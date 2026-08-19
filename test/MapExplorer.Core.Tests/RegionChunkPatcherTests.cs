using MapExplorer.Core.Chunk;
using MapExplorer.Core.Nbt.RoundTrip;
using MapExplorer.Core.Region;
using static MapExplorer.Core.Tests.NbtFixtureBuilder;

namespace MapExplorer.Core.Tests;

public sealed class RegionChunkPatcherTests
{
    [Fact]
    public void ApplyEdits_ChangesOnlyTheEditedBlocks()
    {
        var nbt = BuildChunkNbt([new SectionFixture(Y: 0, BlockPalette: ["minecraft:stone"])]);

        var patched = RegionChunkPatcher.ApplyEdits(nbt, chunkX: 0, chunkZ: 0,
            new Dictionary<(int, int, int), string> { [(1, 5, 2)] = "minecraft:dirt" });

        var full = ChunkDecoder.Decode(patched, 0, 0);
        Assert.Equal("minecraft:dirt", full.GetBlock(1, 5, 2));
        Assert.Equal("minecraft:stone", full.GetBlock(0, 5, 2));
        Assert.Equal("minecraft:stone", full.GetBlock(1, 5, 3));
    }

    // Real Minecraft chunk NBT stores a section's "Y" as TAG_Byte — NbtFixtureBuilder always emits
    // TAG_Int (see PackLongArray/BuildSectionCompound), so every other test in this file exercises
    // an encoding no real .mca file actually uses. Build this one by hand to match reality: a lookup
    // that only recognizes TAG_Int would never find the existing section, silently create a
    // duplicate near-empty one instead, and lose the rest of the section's real blocks the next time
    // something does a full chunk decode (see ChunkDecoder.Decode, which layers every section in
    // list order without deduplicating by Y).
    [Fact]
    public void ApplyEdits_FindsTheExistingSection_WhenYIsStoredAsATagByte()
    {
        var blockStates = new NbtCompoundTag(new Dictionary<string, NbtTag>
        {
            ["palette"] = new NbtListTag(NbtTagType.Compound,
            [
                new NbtCompoundTag(new Dictionary<string, NbtTag> { ["Name"] = new NbtStringTag("minecraft:stone") })
            ])
        });
        var section = new NbtCompoundTag(new Dictionary<string, NbtTag>
        {
            ["Y"] = new NbtByteTag(0),
            ["block_states"] = blockStates
        });
        var root = new NbtCompoundTag(new Dictionary<string, NbtTag>
        {
            ["sections"] = new NbtListTag(NbtTagType.Compound, [section])
        });
        var nbt = NbtRoundTrip.Write(new NbtDocument("", root));

        var patched = RegionChunkPatcher.ApplyEdits(nbt, chunkX: 0, chunkZ: 0,
            new Dictionary<(int, int, int), string> { [(1, 5, 2)] = "minecraft:dirt" });

        var full = ChunkDecoder.Decode(patched, 0, 0);
        Assert.Equal("minecraft:dirt", full.GetBlock(1, 5, 2));
        Assert.Equal("minecraft:stone", full.GetBlock(0, 5, 2)); // rest of the section must survive, not flip to air

        var patchedDoc = NbtRoundTrip.Parse(patched);
        var sections = Assert.IsType<NbtListTag>(patchedDoc.Root.Get("sections"));
        Assert.Single(sections.Items); // must still be exactly one section for Y=0, not a duplicate
    }

    [Fact]
    public void ApplyEdits_CreatesASectionThatDidNotExistBefore()
    {
        var nbt = BuildChunkNbt([new SectionFixture(Y: 0, BlockPalette: ["minecraft:stone"])]);

        // Section Y=10 (world Y 160-175) doesn't exist in the fixture yet.
        var patched = RegionChunkPatcher.ApplyEdits(nbt, chunkX: 0, chunkZ: 0,
            new Dictionary<(int, int, int), string> { [(4, 162, 4)] = "minecraft:oak_planks" });

        var slice = ChunkDecoder.DecodeSection(patched, 0, 0, sectionY: 10);
        Assert.Equal("minecraft:oak_planks", slice.GetBlock(4, 2, 4));
    }

    [Fact]
    public void ApplyEdits_RemovesStaleBlockEntityAtAnEditedPosition()
    {
        var sectionsNbt = BuildChunkNbt([new SectionFixture(Y: 0, BlockPalette: ["minecraft:chest"])]);
        var doc = NbtRoundTrip.Parse(sectionsNbt);
        doc.Root.Set("block_entities", new NbtListTag(NbtTagType.Compound,
        [
            new NbtCompoundTag(new Dictionary<string, NbtTag>
            {
                ["id"] = new NbtStringTag("minecraft:chest"),
                ["x"] = new NbtIntTag(3),
                ["y"] = new NbtIntTag(5),
                ["z"] = new NbtIntTag(7)
            })
        ]));
        var nbt = NbtRoundTrip.Write(doc);

        var patched = RegionChunkPatcher.ApplyEdits(nbt, chunkX: 0, chunkZ: 0,
            new Dictionary<(int, int, int), string> { [(3, 5, 7)] = "minecraft:stone" });

        var patchedDoc = NbtRoundTrip.Parse(patched);
        var blockEntities = Assert.IsType<NbtListTag>(patchedDoc.Root.Get("block_entities"));
        Assert.Empty(blockEntities.Items);
    }

    [Fact]
    public void ApplyEdits_ResetsIsLightOn()
    {
        var sectionsNbt = BuildChunkNbt([new SectionFixture(Y: 0, BlockPalette: ["minecraft:stone"])]);
        var doc = NbtRoundTrip.Parse(sectionsNbt);
        doc.Root.Set("isLightOn", new NbtByteTag(1));
        var nbt = NbtRoundTrip.Write(doc);

        var patched = RegionChunkPatcher.ApplyEdits(nbt, chunkX: 0, chunkZ: 0,
            new Dictionary<(int, int, int), string> { [(0, 0, 0)] = "minecraft:dirt" });

        var patchedDoc = NbtRoundTrip.Parse(patched);
        var isLightOn = Assert.IsType<NbtByteTag>(patchedDoc.Root.Get("isLightOn"));
        Assert.Equal(0, isLightOn.Value);
    }

    [Fact]
    public void ApplyEdits_PreservesUnrelatedFields()
    {
        var sectionsNbt = BuildChunkNbt([new SectionFixture(Y: 0, BlockPalette: ["minecraft:stone"])]);
        var doc = NbtRoundTrip.Parse(sectionsNbt);
        doc.Root.Set("InhabitedTime", new NbtLongTag(12345));
        var nbt = NbtRoundTrip.Write(doc);

        var patched = RegionChunkPatcher.ApplyEdits(nbt, chunkX: 0, chunkZ: 0,
            new Dictionary<(int, int, int), string> { [(0, 0, 0)] = "minecraft:dirt" });

        var patchedDoc = NbtRoundTrip.Parse(patched);
        var inhabitedTime = Assert.IsType<NbtLongTag>(patchedDoc.Root.Get("InhabitedTime"));
        Assert.Equal(12345, inhabitedTime.Value);
    }
}
