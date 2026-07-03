namespace MapExplorer.Core.Chunk;

// Decoded chunk data + read-only accessors. Ported from the Electron app's
// src/shared/chunk-types.ts, which was deliberately split out from the decode
// logic so both the main process (decode) and renderer (render) could share
// just the data shape — same reasoning applies here for Core vs Rendering.
public sealed class ChunkData
{
    public const int ChunkHeight = 384; // -64 to +319
    public const int YOffset = 64;

    public required int ChunkX { get; init; }
    public required int ChunkZ { get; init; }

    // 16*384*16 entries, index = x*ChunkHeight*16 + yi*16 + z (yi = y + YOffset)
    public required ushort[] Blocks { get; init; }
    public required List<string> Palette { get; init; } // block names, index 0 = air

    public required List<string> BiomePalette { get; init; }
    public required byte[] BiomeIndices { get; init; } // 4*96*4 entries (4 biome cells per 16 blocks * 384 height / 4)

    public string GetBlock(int lx, int y, int lz)
    {
        int yi = y + YOffset;
        if (lx is < 0 or > 15 || yi < 0 || yi >= ChunkHeight || lz is < 0 or > 15) return "minecraft:air";
        return Palette[Blocks[lx * ChunkHeight * 16 + yi * 16 + lz]];
    }

    public string GetBiome(int lx, int y, int lz)
    {
        int biomeY = (y + YOffset) / 4;
        int bx = lx / 4;
        int bz = lz / 4;
        const int totalBiomeY = ChunkHeight / 4;
        int idx = bx * totalBiomeY * 4 + biomeY * 4 + bz;
        if (idx < 0 || idx >= BiomeIndices.Length) return "minecraft:plains";
        return BiomePalette[BiomeIndices[idx]];
    }
}
