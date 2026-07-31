using MapExplorer.Core.Chunk;

namespace MapExplorer.Rendering;

/// <summary>What a given LayerConfig needs in order to render a chunk's base pixel color — used to
/// route MapCanvasControl to the cheapest data source that still answers the question: Summary (a
/// ChunkSummary, no I/O) for Surface/Heightmap/Biome; Slice (a single ChunkSliceData section, one
/// targeted decode) for Slice mode. Ore overlay tinting no longer needs its own Full/ChunkData case
/// here — it's sourced separately from OreSummary (see ApplyOreOverlay/FindDominantOre below), a small
/// per-chunk index built once at world-load time, regardless of which of these the base color came
/// from.</summary>
public enum ChunkDataNeed { Summary, Slice }

// Ported from the Electron app's src/renderer/src/core/renderer.ts — same
// surface-shading/ore-overlay logic (a Cave mode was ported too but later
// removed here — it required a full column scan just like the ore overlay
// and wasn't earning its keep). The TS version returns ImageData
// (16x16 or downsampled "preview" sizes for the macro-tile system); this
// version returns per-pixel Rgb and leaves writing into a bitmap to the
// caller (MapCanvasControl in the App project), since the plan drops
// macro-tiling in favor of letting Avalonia's compositor scale full-resolution tiles.
public static class ChunkRenderer
{
    public static Rgb GetChunkPixelColor(ChunkData chunk, LayerConfig config, int lx, int lz)
    {
        Rgb color = config.Mode switch
        {
            LayerMode.Surface => GetSurfaceColor(chunk, lx, lz),
            LayerMode.Slice => Colors.GetBlockColor(chunk.GetBlock(lx, config.SliceY, lz)),
            LayerMode.Heightmap => Colors.GetHeightColor(FindSurfaceY(chunk, lx, lz)),
            LayerMode.Biome => Colors.GetBiomeColor(chunk.GetBiome(lx, FindSurfaceY(chunk, lx, lz), lz)),
            _ => new Rgb(0, 0, 0)
        };

        if (config.OreOverlay && config.OreFilter.Count > 0)
        {
            int surfaceY = FindSurfaceY(chunk, lx, lz);
            for (int y = surfaceY; y >= -ChunkData.YOffset; y--)
            {
                string name = chunk.GetBlock(lx, y, lz);
                if (config.OreFilter.Contains(name) && Colors.OreBlocks.TryGetValue(name, out var ore))
                {
                    color = new Rgb(
                        Rgb.ClampByte(ore.R * 0.8 + color.R * 0.2),
                        Rgb.ClampByte(ore.G * 0.8 + color.G * 0.2),
                        Rgb.ClampByte(ore.B * 0.8 + color.B * 0.2));
                    break;
                }
            }
        }

        return color;
    }

    /// <summary>What MapCanvasControl needs to fetch/decode in order to render this config's base
    /// color. Plain Slice mode needs the one 16-tall section containing SliceY (see
    /// WorldChunkStore.GetOrDecodeSlice) — decoding (or re-decoding, while scrubbing the Y slider or
    /// panning) a full 384-tall column just to read one Y level was the main source of Slice mode's
    /// slowness. Surface/Heightmap/Biome need neither — see the ChunkSummary overload below. The ore
    /// overlay used to force a Full ChunkData decode here regardless of mode (it scanned a whole
    /// column looking for ore); it no longer needs to, since ore data now comes from the small
    /// resident OreSummary instead — see ApplyOreOverlay/FindDominantOre.</summary>
    public static ChunkDataNeed GetDataNeed(LayerConfig config) =>
        config.Mode == LayerMode.Slice ? ChunkDataNeed.Slice : ChunkDataNeed.Summary;

    /// <summary>Surface/Heightmap/Biome rendering sourced from a ChunkSummary instead of a full
    /// ChunkData — valid whenever GetDataNeed(config) is Summary. Pass the chunk's OreSummary for
    /// per-column-accurate ore tinting (the normal per-chunk-tile path); pass null to get the plain
    /// base color with no tint applied (used by macro tiles / the overview, which apply one coarse
    /// tint across a whole chunk instead — see FindDominantOre).</summary>
    public static Rgb GetChunkPixelColor(ChunkSummary summary, NamePalette blockNames, NamePalette biomeNames, LayerConfig config, int lx, int lz, OreSummary? oreSummary = null)
    {
        int idx = lx * 16 + lz;
        int surfaceY = summary.SurfaceY[idx];

        Rgb color = config.Mode switch
        {
            LayerMode.Heightmap => Colors.GetHeightColor(surfaceY),
            LayerMode.Biome => Colors.GetBiomeColor(biomeNames[summary.BiomeIndex[idx]]),
            _ => ShadeSurfaceColor(blockNames[summary.TopBlockIndex[idx]], surfaceY)
        };

        return ApplyOreOverlay(color, oreSummary, config, lx, lz);
    }

    /// <summary>Slice-mode rendering sourced from a single decoded section instead of a full
    /// ChunkData — valid whenever GetDataNeed(config) is Slice. See the ChunkSummary overload above
    /// for what the optional `oreSummary` parameter means.</summary>
    public static Rgb GetChunkPixelColor(ChunkSliceData slice, LayerConfig config, int lx, int lz, OreSummary? oreSummary = null)
    {
        int localY = config.SliceY - slice.SectionY * 16;
        Rgb color = Colors.GetBlockColor(slice.GetBlock(lx, localY, lz));
        return ApplyOreOverlay(color, oreSummary, config, lx, lz);
    }

    /// <summary>Tints `color` toward the topmost ore matching the current filter at column (lx, lz),
    /// if any — sourced from the chunk's OreSummary (built once at world-load time), not a fresh
    /// per-pixel column scan through a full ChunkData. This is a lookup into a handful of cached
    /// entries instead of walking up to 384 blocks per pixel.</summary>
    private static Rgb ApplyOreOverlay(Rgb color, OreSummary? oreSummary, LayerConfig config, int lx, int lz)
    {
        if (oreSummary is null || !config.OreOverlay || config.OreFilter.Count == 0) return color;
        if (!oreSummary.ByColumn.TryGetValue(lx * 16 + lz, out var hits)) return color;

        OreHit? best = null;
        foreach (var hit in hits)
        {
            if ((best is null || hit.Y > best.Value.Y) && config.OreFilter.Contains(Colors.OreBlockNames[hit.OreType]))
                best = hit;
        }

        return best is null ? color : BlendOre(color, Colors.OreBlocks[Colors.OreBlockNames[best.Value.OreType]]);
    }

    /// <summary>Finds the shallowest ore hit anywhere in the chunk that matches the current filter —
    /// used by macro tiles and the world overview to tint a whole chunk cell at once instead of
    /// per-column, since individual ore pixels are imperceptible once a 16x16 chunk is scaled down to
    /// a handful of screen pixels anyway. Returns null if ore overlay is off or the chunk has no
    /// matching ore.</summary>
    public static Rgb? FindDominantOre(OreSummary oreSummary, LayerConfig config)
    {
        if (!config.OreOverlay || config.OreFilter.Count == 0) return null;

        OreHit? best = null;
        foreach (var hits in oreSummary.ByColumn.Values)
        {
            foreach (var hit in hits)
            {
                if ((best is null || hit.Y > best.Value.Y) && config.OreFilter.Contains(Colors.OreBlockNames[hit.OreType]))
                    best = hit;
            }
        }

        return best is null ? null : Colors.OreBlocks[Colors.OreBlockNames[best.Value.OreType]];
    }

    /// <summary>Same 80/20 ore/base blend the per-pixel overlay uses, exposed for callers (macro
    /// tiles, the overview) that apply it uniformly across a whole chunk cell instead of per-column.</summary>
    public static Rgb BlendOre(Rgb color, Rgb ore) => new(
        Rgb.ClampByte(ore.R * 0.8 + color.R * 0.2),
        Rgb.ClampByte(ore.G * 0.8 + color.G * 0.2),
        Rgb.ClampByte(ore.B * 0.8 + color.B * 0.2));

    public static int FindSurfaceY(ChunkData chunk, int lx, int lz)
    {
        const int maxY = ChunkData.ChunkHeight - ChunkData.YOffset - 1;
        for (int y = maxY; y >= -ChunkData.YOffset; y--)
        {
            if (!Colors.AirBlocks.Contains(chunk.GetBlock(lx, y, lz))) return y;
        }
        return -ChunkData.YOffset;
    }

    /// <summary>Returns the world Y whose block is actually displayed at (lx, lz) under the given layer config.</summary>
    public static int FindDisplayY(ChunkData chunk, LayerConfig config, int lx, int lz) =>
        config.Mode == LayerMode.Slice ? config.SliceY : FindSurfaceY(chunk, lx, lz);

    private static Rgb GetSurfaceColor(ChunkData chunk, int lx, int lz)
    {
        int surfaceY = FindSurfaceY(chunk, lx, lz);
        return ShadeSurfaceColor(chunk.GetBlock(lx, surfaceY, lz), surfaceY);
    }

    private static Rgb ShadeSurfaceColor(string name, int surfaceY)
    {
        // simple shading: blocks slightly above average get lighter
        double shade = Math.Min(1.2, Math.Max(0.5, 0.7 + surfaceY / 200.0));
        var c = Colors.GetBlockColor(name);
        return new Rgb(
            Rgb.ClampByte(c.R * shade),
            Rgb.ClampByte(c.G * shade),
            Rgb.ClampByte(c.B * shade));
    }
}
