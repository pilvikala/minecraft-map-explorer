using MapExplorer.Core.Nbt.RoundTrip;

namespace MapExplorer.Core.Chunk;

/// <summary>
/// Full-fidelity block_states encode/decode for a single 16x16x16 section, operating on
/// MapExplorer.Core.Nbt.RoundTrip's tag model (not the lossy, read-only MapExplorer.Core.Nbt.Nbt used
/// for rendering) — this is the write-side counterpart of ChunkDecoder.DecodeSectionBlocks, kept as a
/// separate small implementation rather than unifying the two NBT stacks (they're intentionally split
/// for read-performance reasons — see Nbt.cs/NbtRoundTrip.cs).
/// </summary>
public static class ChunkSectionCodec
{
    private const int BlockCount = 4096; // 16*16*16

    /// <summary>Decodes a section's current 4096 block names (index = localY*256 + localZ*16 + localX,
    /// matching Minecraft's on-disk layout and ChunkSliceData's convention). Returns all-air if the
    /// section has no block_states/palette at all.</summary>
    public static string[] DecodeBlockNames(NbtCompoundTag section)
    {
        var names = new string[BlockCount];
        Array.Fill(names, "minecraft:air");

        if (section.Get("block_states") is not NbtCompoundTag bs) return names;
        var rawPalette = bs.Get("palette") is NbtListTag pl ? pl.Items : [];
        if (rawPalette.Count == 0) return names;

        var paletteNames = new string[rawPalette.Count];
        for (int i = 0; i < rawPalette.Count; i++)
        {
            paletteNames[i] = rawPalette[i] is NbtCompoundTag entry && entry.Get("Name") is NbtStringTag n
                ? n.Value
                : "minecraft:air";
        }

        if (rawPalette.Count == 1)
        {
            Array.Fill(names, paletteNames[0]);
            return names;
        }

        if (bs.Get("data") is not NbtLongArrayTag dataArr) return names; // malformed: leave as air

        int bitsPerBlock = Math.Max(4, (int)Math.Ceiling(Math.Log2(rawPalette.Count)));
        int valuesPerLong = 64 / bitsPerBlock;
        long mask = (1L << bitsPerBlock) - 1;
        for (int i = 0; i < BlockCount; i++)
        {
            int longIdx = i / valuesPerLong;
            int bitIdx = (i - longIdx * valuesPerLong) * bitsPerBlock;
            int value = longIdx < dataArr.Value.Length ? (int)((dataArr.Value[longIdx] >> bitIdx) & mask) : 0;
            names[i] = value < paletteNames.Length ? paletteNames[value] : "minecraft:air";
        }
        return names;
    }

    /// <summary>Builds a fresh block_states compound from a 4096-entry block name array, using the
    /// smallest palette that actually appears (matching vanilla's own encoding — a uniform section
    /// gets a single-entry palette and no "data" tag at all).</summary>
    public static NbtCompoundTag EncodeBlockStates(string[] names)
    {
        if (names.Length != BlockCount)
            throw new ArgumentException($"Expected {BlockCount} block names, got {names.Length}", nameof(names));

        var palette = new List<string>();
        var paletteIndex = new Dictionary<string, int>();
        var localIndices = new int[BlockCount];
        for (int i = 0; i < BlockCount; i++)
        {
            if (!paletteIndex.TryGetValue(names[i], out int idx))
            {
                idx = palette.Count;
                palette.Add(names[i]);
                paletteIndex[names[i]] = idx;
            }
            localIndices[i] = idx;
        }

        var paletteList = new NbtListTag(NbtTagType.Compound,
            palette.Select(name => (NbtTag)new NbtCompoundTag(new Dictionary<string, NbtTag>
            {
                ["Name"] = new NbtStringTag(name)
            })).ToList());

        var fields = new Dictionary<string, NbtTag> { ["palette"] = paletteList };

        if (palette.Count > 1)
        {
            int bitsPerBlock = Math.Max(4, (int)Math.Ceiling(Math.Log2(palette.Count)));
            int valuesPerLong = 64 / bitsPerBlock;
            int longCount = (int)Math.Ceiling(BlockCount / (double)valuesPerLong);
            var longs = new long[longCount];
            for (int i = 0; i < BlockCount; i++)
            {
                int longIdx = i / valuesPerLong;
                int bitIdx = (i - longIdx * valuesPerLong) * bitsPerBlock;
                longs[longIdx] |= (long)localIndices[i] << bitIdx;
            }
            fields["data"] = new NbtLongArrayTag(longs);
        }

        return new NbtCompoundTag(fields);
    }
}
