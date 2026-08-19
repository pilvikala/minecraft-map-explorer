using MapExplorer.Core.Chunk;
using MapExplorer.Core.Nbt.RoundTrip;

namespace MapExplorer.Core.Region;

/// <summary>
/// Applies in-memory block edits to a chunk's full-fidelity NBT and re-serializes it — the write-side
/// counterpart of ChunkDecoder, used only at Save time (see WorldEditWriter). Uses NbtRoundTrip (not
/// the lossy MapExplorer.Core.Nbt.Nbt) so every field this app never looks at — entities, heightmaps,
/// structure references, etc. — round-trips byte-for-byte-equivalent except for what was actually edited.
/// </summary>
public static class RegionChunkPatcher
{
    /// <summary>Edits keyed by chunk-local position: (localX 0-15, world Y, localZ 0-15) -> new block name.</summary>
    public static byte[] ApplyEdits(byte[] originalRawChunkNbt, int chunkX, int chunkZ,
        IReadOnlyDictionary<(int LocalX, int Y, int LocalZ), string> edits)
    {
        var doc = NbtRoundTrip.Parse(originalRawChunkNbt);

        var chunkData = doc.Root;
        if (chunkData.Get("sections") is not NbtListTag && chunkData.Get("Level") is NbtCompoundTag level)
        {
            chunkData = level;
        }
        if (chunkData.Get("sections") is not NbtListTag sections)
        {
            throw new InvalidOperationException("Chunk NBT has no sections to edit");
        }

        foreach (var group in edits.GroupBy(kv => FloorDiv(kv.Key.Y, 16)))
        {
            int sectionY = group.Key;
            var sectionTag = sections.Items.OfType<NbtCompoundTag>()
                .FirstOrDefault(s => ReadSectionY(s.Get("Y")) == sectionY);

            if (sectionTag is null)
            {
                // Real chunk NBT stores this as TAG_Byte (see ReadSectionY) — match that convention
                // for a freshly-created section too, rather than introducing a TAG_Int one no other
                // section in the file would ever have.
                sectionTag = new NbtCompoundTag(new Dictionary<string, NbtTag> { ["Y"] = new NbtByteTag((sbyte)sectionY) });
                sections.Items.Add(sectionTag);
            }

            var names = ChunkSectionCodec.DecodeBlockNames(sectionTag);
            foreach (var (key, blockName) in group)
            {
                int localY = key.Y - sectionY * 16;
                int idx = localY * 256 + key.LocalZ * 16 + key.LocalX;
                names[idx] = blockName;
            }
            sectionTag.Set("block_states", ChunkSectionCodec.EncodeBlockStates(names));
        }

        RemoveStaleBlockEntities(chunkData, chunkX, chunkZ, edits.Keys);
        ResetLighting(chunkData);

        return NbtRoundTrip.Write(new NbtDocument(doc.RootName, doc.Root));
    }

    // Painting a simple material (the only kind this editor's palette offers) over a position that
    // used to hold a chest/sign/etc. must not leave that block entity's NBT behind — it would
    // reference a block that no longer exists there.
    private static void RemoveStaleBlockEntities(NbtCompoundTag chunkData, int chunkX, int chunkZ,
        IEnumerable<(int LocalX, int Y, int LocalZ)> editedPositions)
    {
        if (chunkData.Get("block_entities") is not NbtListTag beList) return;

        var absolute = editedPositions
            .Select(p => (X: chunkX * 16 + p.LocalX, Y: p.Y, Z: chunkZ * 16 + p.LocalZ))
            .ToHashSet();

        beList.Items.RemoveAll(item =>
            item is NbtCompoundTag be &&
            be.Get("x") is NbtIntTag x && be.Get("y") is NbtIntTag y && be.Get("z") is NbtIntTag z &&
            absolute.Contains((x.Value, y.Value, z.Value)));
    }

    // Forces Minecraft to recompute lighting for this chunk on next load instead of showing stale
    // light values around the edit.
    private static void ResetLighting(NbtCompoundTag chunkData)
    {
        if (chunkData.Get("isLightOn") is NbtByteTag)
        {
            chunkData.Set("isLightOn", new NbtByteTag(0));
        }
    }

    // A section's "Y" tag is TAG_Byte in real Minecraft chunk NBT (unlike block_entities' x/y/z,
    // which are genuinely TAG_Int) — accept Byte/Short/Int alike so this matches whichever one a
    // given file actually has, the same flexibility MapExplorer.Core.Nbt.Nbt's NbtInt already gives
    // the read-only decoder. Matching only NbtIntTag here previously meant the lookup below never
    // found an existing section (real files never have one), so every edit silently created a
    // duplicate, near-empty section that a later full decode would layer over — and lose — the
    // original block data for that entire Y range.
    private static int? ReadSectionY(NbtTag? tag) => tag switch
    {
        NbtByteTag b => b.Value,
        NbtShortTag s => s.Value,
        NbtIntTag i => i.Value,
        _ => null
    };

    private static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);
}
