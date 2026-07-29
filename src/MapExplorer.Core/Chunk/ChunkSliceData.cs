namespace MapExplorer.Core.Chunk;

/// <summary>
/// One decoded 16x16x16 chunk section's block data — the targeted decode path for LayerMode.Slice
/// (see ChunkDecoder.DecodeSection), which only needs whatever one Y level is currently displayed,
/// not the full 384-tall column a ChunkData decode produces. Index = localY*256 + lz*16 + lx (all
/// 0-15), matching Minecraft's on-disk section layout and ChunkDecoder.ReadPackedEntry's consumer
/// convention, so no reshuffling is needed between decode and storage.
/// </summary>
public sealed class ChunkSliceData
{
    public required int ChunkX { get; init; }
    public required int ChunkZ { get; init; }
    public required int SectionY { get; init; } // Minecraft section index; world Y = SectionY*16 + localY (0-15)
    public required ushort[] Blocks { get; init; } // 16*16*16 = 4096 entries
    public required List<string> Palette { get; init; } // block names, index 0 = air

    public string GetBlock(int lx, int localY, int lz)
    {
        if (lx is < 0 or > 15 || localY is < 0 or > 15 || lz is < 0 or > 15) return "minecraft:air";
        return Palette[Blocks[localY * 256 + lz * 16 + lx]];
    }
}
