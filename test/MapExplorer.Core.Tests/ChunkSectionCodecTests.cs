using MapExplorer.Core.Chunk;
using MapExplorer.Core.Nbt.RoundTrip;

namespace MapExplorer.Core.Tests;

// EncodeBlockStates/DecodeBlockNames are the write-side counterpart of ChunkDecoder.DecodeSectionBlocks
// (see ChunkDecoderSectionTests for the read-side equivalent) — round-tripping through them must be
// lossless for the palette sizes that cross a bits-per-block threshold (4/5/6/7/8 bits at 16/32/64/128
// distinct values, floored at 4 bits per vanilla's own minimum).
public sealed class ChunkSectionCodecTests
{
    // DecodeBlockNames reads block_states off a wrapping section compound (that's what
    // RegionChunkPatcher hands it — a real section, with Y/block_states/etc. siblings), while
    // EncodeBlockStates returns just the block_states compound (what gets assigned back onto that
    // section) — wrap it the same way here.
    private static NbtCompoundTag AsSection(NbtCompoundTag blockStates) =>
        new(new Dictionary<string, NbtTag> { ["block_states"] = blockStates });

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(300)]
    public void EncodeThenDecode_RoundTripsExactly_ForVariousPaletteSizes(int distinctBlocks)
    {
        var names = new string[4096];
        for (int i = 0; i < names.Length; i++) names[i] = $"minecraft:test_block_{i % distinctBlocks}";

        var encoded = ChunkSectionCodec.EncodeBlockStates(names);
        var decoded = ChunkSectionCodec.DecodeBlockNames(AsSection(encoded));

        Assert.Equal(names, decoded);
    }

    [Fact]
    public void Encode_OmitsDataTag_WhenUniform()
    {
        var names = new string[4096];
        Array.Fill(names, "minecraft:stone");

        var encoded = ChunkSectionCodec.EncodeBlockStates(names);

        Assert.Null(encoded.Get("data"));
        var palette = Assert.IsType<NbtListTag>(encoded.Get("palette"));
        Assert.Single(palette.Items);
    }

    [Fact]
    public void Decode_ReturnsAllAir_WhenSectionHasNoBlockStates()
    {
        var section = new NbtCompoundTag(new Dictionary<string, NbtTag> { ["Y"] = new NbtIntTag(0) });

        var names = ChunkSectionCodec.DecodeBlockNames(section);

        Assert.All(names, n => Assert.Equal("minecraft:air", n));
    }

    [Fact]
    public void Encode_PreservesDistinctBlockAtEachPosition()
    {
        var names = new string[4096];
        Array.Fill(names, "minecraft:air");
        names[0] = "minecraft:dirt";
        names[4095] = "minecraft:oak_planks";
        names[2048] = "minecraft:glass";

        var decoded = ChunkSectionCodec.DecodeBlockNames(AsSection(ChunkSectionCodec.EncodeBlockStates(names)));

        Assert.Equal("minecraft:dirt", decoded[0]);
        Assert.Equal("minecraft:oak_planks", decoded[4095]);
        Assert.Equal("minecraft:glass", decoded[2048]);
        Assert.Equal("minecraft:air", decoded[1]);
    }
}
