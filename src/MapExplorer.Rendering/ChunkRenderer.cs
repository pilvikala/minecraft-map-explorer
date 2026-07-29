using MapExplorer.Core.Chunk;

namespace MapExplorer.Rendering;

/// <summary>What a given LayerConfig needs in order to render a chunk's pixels — used to route
/// MapCanvasControl to the cheapest data source that still answers the question: Summary (a
/// ChunkSummary, no I/O) for Surface/Heightmap/Biome; Slice (a single ChunkSliceData section, one
/// targeted decode) for Slice mode; Full (a whole decoded ChunkData) only when the ore overlay is on,
/// since it scans an entire column regardless of the active mode.</summary>
public enum ChunkDataNeed { Summary, Slice, Full }

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

    /// <summary>What MapCanvasControl needs to fetch/decode in order to render under this config.
    /// The ore overlay always wins regardless of mode (it scans a whole column looking for ore), so
    /// it's the only thing that still requires a full ChunkData decode. Plain Slice mode only needs
    /// the one 16-tall section containing SliceY (see WorldChunkStore.GetOrDecodeSlice) — decoding
    /// (or re-decoding, while scrubbing the Y slider or panning) a full 384-tall column just to read
    /// one Y level was the main source of Slice mode's slowness. Surface/Heightmap/Biome need neither
    /// — see the ChunkSummary overload below.</summary>
    public static ChunkDataNeed GetDataNeed(LayerConfig config)
    {
        if (config.OreOverlay && config.OreFilter.Count > 0) return ChunkDataNeed.Full;
        return config.Mode == LayerMode.Slice ? ChunkDataNeed.Slice : ChunkDataNeed.Summary;
    }

    /// <summary>Surface/Heightmap/Biome rendering sourced from a ChunkSummary instead of a full
    /// ChunkData — valid only when GetDataNeed(config) is Summary.</summary>
    public static Rgb GetChunkPixelColor(ChunkSummary summary, NamePalette blockNames, NamePalette biomeNames, LayerConfig config, int lx, int lz)
    {
        int idx = lx * 16 + lz;
        int surfaceY = summary.SurfaceY[idx];

        return config.Mode switch
        {
            LayerMode.Heightmap => Colors.GetHeightColor(surfaceY),
            LayerMode.Biome => Colors.GetBiomeColor(biomeNames[summary.BiomeIndex[idx]]),
            _ => ShadeSurfaceColor(blockNames[summary.TopBlockIndex[idx]], surfaceY)
        };
    }

    /// <summary>Slice-mode rendering sourced from a single decoded section instead of a full
    /// ChunkData — valid only when GetDataNeed(config) is Slice (i.e. Mode is Slice and the ore
    /// overlay is off).</summary>
    public static Rgb GetChunkPixelColor(ChunkSliceData slice, LayerConfig config, int lx, int lz)
    {
        int localY = config.SliceY - slice.SectionY * 16;
        return Colors.GetBlockColor(slice.GetBlock(lx, localY, lz));
    }

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
