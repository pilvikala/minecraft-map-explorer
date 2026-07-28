using MapExplorer.Core.Chunk;

namespace MapExplorer.Rendering.Tests;

internal static class ChunkTestFixtures
{
    // Builds a chunk with an entirely air interior, then lets the caller poke
    // specific blocks in via SetBlock — much simpler than round-tripping
    // through NBT bytes for tests that only care about the render-logic layer
    // (findSurfaceY/findCaveFloorY/getChunkPixelColor), not the decoder.
    public static (ChunkData chunk, Action<int, int, int, string> setBlock) NewChunk()
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
}
