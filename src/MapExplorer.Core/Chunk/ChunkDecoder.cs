using MapExplorer.Core.Nbt;

namespace MapExplorer.Core.Chunk;

// Ported from the Electron app's src/main/chunk-decode.ts. Unlike the JS
// version this needs no BigInt-avoidance trick: `long` shifts are native,
// fast CPU instructions in .NET, so the packed-long unpack is just a plain
// shift + mask (see ReadPackedEntry below — this is the exact spot that used
// to need a hi/lo 32-bit split workaround in the TypeScript port).
public static class ChunkDecoder
{
    private static ChunkData Empty(int chunkX, int chunkZ) => new()
    {
        ChunkX = chunkX,
        ChunkZ = chunkZ,
        Blocks = new ushort[16 * ChunkData.ChunkHeight * 16],
        Palette = ["minecraft:air"],
        BiomePalette = ["minecraft:plains"],
        BiomeIndices = new byte[4 * (ChunkData.ChunkHeight / 4) * 4]
    };

    private static int ReadPackedEntry(long[] data, int longIdx, int bitIdx, long mask)
    {
        if (longIdx >= data.Length) return 0;
        return (int)((data[longIdx] >> bitIdx) & mask);
    }

    public static ChunkData Decode(byte[] raw, int chunkX, int chunkZ)
    {
        NbtCompound nbt;
        try
        {
            nbt = Nbt.Nbt.Parse(raw);
        }
        catch
        {
            return Empty(chunkX, chunkZ);
        }

        var blockPalette = new List<string> { "minecraft:air" };
        var paletteIndex = new Dictionary<string, int> { ["minecraft:air"] = 0 };
        var blocks = new ushort[16 * ChunkData.ChunkHeight * 16];

        var biomePalette = new List<string> { "minecraft:plains" };
        var biomeIndex = new Dictionary<string, int> { ["minecraft:plains"] = 0 };
        var biomeData = new byte[4 * (ChunkData.ChunkHeight / 4) * 4];

        // Handle both flat structure (1.18+) and wrapped Level structure (older versions)
        var chunkData = nbt;
        if (nbt.Get("sections") is not NbtList && nbt.Get("Level") is NbtCompound level)
        {
            chunkData = level;
        }

        if (chunkData.Get("sections") is not NbtList sections)
        {
            return Empty(chunkX, chunkZ);
        }

        foreach (var sectionVal in sections.Items)
        {
            if (sectionVal is not NbtCompound section) continue;
            int sectionY = section.Get("Y") is NbtInt y ? y.Value : 0;
            int yBase = sectionY * 16 + ChunkData.YOffset;
            if (yBase < 0 || yBase >= ChunkData.ChunkHeight) continue;

            // --- block states ---
            if (section.Get("block_states") is NbtCompound bs)
            {
                var rawPalette = bs.Get("palette") is NbtList pl ? pl.Items : [];
                if (rawPalette.Count > 0)
                {
                    var sectionPalette = new int[rawPalette.Count];
                    for (int idx = 0; idx < rawPalette.Count; idx++)
                    {
                        string name = "minecraft:air";
                        if (rawPalette[idx] is NbtCompound entry && entry.Get("Name") is NbtString n) name = n.Value;
                        if (!paletteIndex.TryGetValue(name, out int pidx))
                        {
                            pidx = blockPalette.Count;
                            blockPalette.Add(name);
                            paletteIndex[name] = pidx;
                        }
                        sectionPalette[idx] = pidx;
                    }

                    if (rawPalette.Count == 1)
                    {
                        // entire section is this block
                        int globalIdx = sectionPalette[0];
                        for (int by = 0; by < 16; by++)
                        for (int bz = 0; bz < 16; bz++)
                        for (int bx = 0; bx < 16; bx++)
                            blocks[bx * ChunkData.ChunkHeight * 16 + (yBase + by) * 16 + bz] = (ushort)globalIdx;
                    }
                    else if (bs.Get("data") is NbtLongArray dataArr)
                    {
                        int bitsPerBlock = Math.Max(4, (int)Math.Ceiling(Math.Log2(rawPalette.Count)));
                        int valuesPerLong = 64 / bitsPerBlock;
                        long mask = (1L << bitsPerBlock) - 1;
                        // Minecraft packs blocks as index = y*256 + z*16 + x
                        for (int i = 0; i < 4096; i++)
                        {
                            int longIdx = i / valuesPerLong;
                            int bitIdx = (i - longIdx * valuesPerLong) * bitsPerBlock;
                            int value = ReadPackedEntry(dataArr.Value, longIdx, bitIdx, mask);
                            int bx = i & 15;
                            int bz = (i >> 4) & 15;
                            int by = (i >> 8) & 15;
                            int paletteIdx = value < sectionPalette.Length ? sectionPalette[value] : 0;
                            blocks[bx * ChunkData.ChunkHeight * 16 + (yBase + by) * 16 + bz] = (ushort)paletteIdx;
                        }
                    }
                }
            }

            // --- biomes ---
            if (section.Get("biomes") is NbtCompound bm)
            {
                var rawBiomePalette = bm.Get("palette") is NbtList bpl ? bpl.Items : [];
                if (rawBiomePalette.Count > 0)
                {
                    var sectionBiomePalette = new int[rawBiomePalette.Count];
                    for (int idx = 0; idx < rawBiomePalette.Count; idx++)
                    {
                        string name = rawBiomePalette[idx] is NbtString n ? n.Value : "minecraft:plains";
                        if (!biomeIndex.TryGetValue(name, out int bidx))
                        {
                            bidx = biomePalette.Count;
                            biomePalette.Add(name);
                            biomeIndex[name] = bidx;
                        }
                        sectionBiomePalette[idx] = bidx;
                    }

                    // 4x4x4 biome cells per section; sectionY=-4 -> biomeBase=0
                    int biomeBase = (sectionY + 4) * 4;
                    if (rawBiomePalette.Count == 1)
                    {
                        for (int by = 0; by < 4; by++)
                        for (int bz = 0; bz < 4; bz++)
                        for (int bx = 0; bx < 4; bx++)
                        {
                            int idx = bx * (ChunkData.ChunkHeight / 4) * 4 + (biomeBase + by) * 4 + bz;
                            if (idx >= 0 && idx < biomeData.Length) biomeData[idx] = (byte)sectionBiomePalette[0];
                        }
                    }
                    else if (bm.Get("data") is NbtLongArray bdata)
                    {
                        int bitsPerBiome = Math.Max(1, (int)Math.Ceiling(Math.Log2(rawBiomePalette.Count)));
                        int valuesPerLong = 64 / bitsPerBiome;
                        long mask = (1L << bitsPerBiome) - 1;
                        // Minecraft biome index = y*16 + z*4 + x
                        for (int i = 0; i < 64; i++)
                        {
                            int longIdx = i / valuesPerLong;
                            int bitIdx = (i - longIdx * valuesPerLong) * bitsPerBiome;
                            int value = ReadPackedEntry(bdata.Value, longIdx, bitIdx, mask);
                            int bx = i & 3;
                            int bz = (i >> 2) & 3;
                            int by = (i >> 4) & 3;
                            int idx = bx * (ChunkData.ChunkHeight / 4) * 4 + (biomeBase + by) * 4 + bz;
                            if (idx >= 0 && idx < biomeData.Length)
                                biomeData[idx] = (byte)(value < sectionBiomePalette.Length ? sectionBiomePalette[value] : 0);
                        }
                    }
                }
            }
        }

        return new ChunkData
        {
            ChunkX = chunkX,
            ChunkZ = chunkZ,
            Blocks = blocks,
            Palette = blockPalette,
            BiomePalette = biomePalette,
            BiomeIndices = biomeData
        };
    }
}
