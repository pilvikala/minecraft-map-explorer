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

    private static ChunkSliceData EmptySlice(int chunkX, int chunkZ, int sectionY) => new()
    {
        ChunkX = chunkX,
        ChunkZ = chunkZ,
        SectionY = sectionY,
        Blocks = new ushort[16 * 16 * 16],
        Palette = ["minecraft:air"]
    };

    private static int ReadPackedEntry(long[] data, int longIdx, int bitIdx, long mask)
    {
        if (longIdx >= data.Length) return 0;
        return (int)((data[longIdx] >> bitIdx) & mask);
    }

    /// <summary>Handles both the flat structure (1.18+) and the wrapped Level structure (older
    /// versions), returning the compound that directly holds "sections" — or null if this chunk's
    /// NBT doesn't have one at all (empty/corrupt chunk).</summary>
    private static NbtList? GetSections(NbtCompound nbt)
    {
        var chunkData = nbt;
        if (nbt.Get("sections") is not NbtList && nbt.Get("Level") is NbtCompound level)
        {
            chunkData = level;
        }
        return chunkData.Get("sections") as NbtList;
    }

    /// <summary>Unpacks one section's block_states into a 4096-entry array of indices into `palette`
    /// (the caller's accumulating block-name palette — chunk-global for Decode, section-scoped for
    /// DecodeSection). Index = by*256 + bz*16 + bx (all 0-15) — this is also exactly how Minecraft
    /// packs the source data, so no reshuffling is needed. Returns null if the section has no
    /// block_states/palette at all (caller should leave whatever default it already has, i.e. air).</summary>
    private static ushort[]? DecodeSectionBlocks(NbtCompound section, List<string> palette, Dictionary<string, int> paletteIndex)
    {
        if (section.Get("block_states") is not NbtCompound bs) return null;
        var rawPalette = bs.Get("palette") is NbtList pl ? pl.Items : [];
        if (rawPalette.Count == 0) return null;

        var sectionPaletteMap = new int[rawPalette.Count];
        for (int idx = 0; idx < rawPalette.Count; idx++)
        {
            string name = "minecraft:air";
            if (rawPalette[idx] is NbtCompound entry && entry.Get("Name") is NbtString n) name = n.Value;
            if (!paletteIndex.TryGetValue(name, out int pidx))
            {
                pidx = palette.Count;
                palette.Add(name);
                paletteIndex[name] = pidx;
            }
            sectionPaletteMap[idx] = pidx;
        }

        var result = new ushort[4096];
        if (rawPalette.Count == 1)
        {
            Array.Fill(result, (ushort)sectionPaletteMap[0]);
            return result;
        }

        if (bs.Get("data") is not NbtLongArray dataArr) return result; // malformed: multi-entry palette but no packed data — leave as air

        int bitsPerBlock = Math.Max(4, (int)Math.Ceiling(Math.Log2(rawPalette.Count)));
        int valuesPerLong = 64 / bitsPerBlock;
        long mask = (1L << bitsPerBlock) - 1;
        // Minecraft packs blocks as index = y*256 + z*16 + x — matches this array's own indexing.
        for (int i = 0; i < 4096; i++)
        {
            int longIdx = i / valuesPerLong;
            int bitIdx = (i - longIdx * valuesPerLong) * bitsPerBlock;
            int value = ReadPackedEntry(dataArr.Value, longIdx, bitIdx, mask);
            result[i] = (ushort)(value < sectionPaletteMap.Length ? sectionPaletteMap[value] : 0);
        }
        return result;
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

        if (GetSections(nbt) is not { } sections)
        {
            return Empty(chunkX, chunkZ);
        }

        foreach (var sectionVal in sections.Items)
        {
            if (sectionVal is not NbtCompound section) continue;
            int sectionY = section.Get("Y") is NbtInt y ? y.Value : 0;
            int yBase = sectionY * 16 + ChunkData.YOffset;
            if (yBase < 0 || yBase >= ChunkData.ChunkHeight) continue;

            var sectionBlocks = DecodeSectionBlocks(section, blockPalette, paletteIndex);
            if (sectionBlocks is not null)
            {
                for (int i = 0; i < 4096; i++)
                {
                    int bx = i & 15;
                    int bz = (i >> 4) & 15;
                    int by = (i >> 8) & 15;
                    blocks[bx * ChunkData.ChunkHeight * 16 + (yBase + by) * 16 + bz] = sectionBlocks[i];
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

    /// <summary>
    /// Decodes only the one 16-tall section at `sectionY` — used for Slice mode (see
    /// ChunkRenderer.GetDataNeed), which only ever needs whatever single Y level is currently
    /// displayed. Skips biomes entirely (Slice mode doesn't show them) and skips every other
    /// section's block_states unpack loop, which is the bulk of a full Decode's per-chunk cost.
    /// Returns an all-air slice if the section doesn't exist in this chunk's NBT (ungenerated/empty).
    /// </summary>
    public static ChunkSliceData DecodeSection(byte[] raw, int chunkX, int chunkZ, int sectionY)
    {
        NbtCompound nbt;
        try
        {
            nbt = Nbt.Nbt.Parse(raw);
        }
        catch
        {
            return EmptySlice(chunkX, chunkZ, sectionY);
        }

        if (GetSections(nbt) is not { } sections)
        {
            return EmptySlice(chunkX, chunkZ, sectionY);
        }

        foreach (var sectionVal in sections.Items)
        {
            if (sectionVal is not NbtCompound section) continue;
            int sy = section.Get("Y") is NbtInt y ? y.Value : 0;
            if (sy != sectionY) continue;

            var palette = new List<string> { "minecraft:air" };
            var paletteIndex = new Dictionary<string, int> { ["minecraft:air"] = 0 };
            var blocks = DecodeSectionBlocks(section, palette, paletteIndex) ?? new ushort[16 * 16 * 16];

            return new ChunkSliceData { ChunkX = chunkX, ChunkZ = chunkZ, SectionY = sectionY, Blocks = blocks, Palette = palette };
        }

        return EmptySlice(chunkX, chunkZ, sectionY);
    }
}
