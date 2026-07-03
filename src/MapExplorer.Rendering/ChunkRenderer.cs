using MapExplorer.Core.Chunk;

namespace MapExplorer.Rendering;

// Ported from the Electron app's src/renderer/src/core/renderer.ts — same 5
// view modes, same surface-shading/cave-floor/ore-overlay logic. The TS
// version returns ImageData (16x16 or downsampled "preview" sizes for the
// macro-tile system); this version returns per-pixel Rgb and leaves writing
// into a bitmap to the caller (MapCanvasControl in the App project), since
// the plan drops macro-tiling in favor of letting Avalonia's compositor scale
// full-resolution tiles.
public static class ChunkRenderer
{
    public static Rgb GetChunkPixelColor(ChunkData chunk, LayerConfig config, int lx, int lz)
    {
        Rgb color = config.Mode switch
        {
            LayerMode.Surface => GetSurfaceColor(chunk, lx, lz),
            LayerMode.Slice => Colors.GetBlockColor(chunk.GetBlock(lx, config.SliceY, lz)),
            LayerMode.Heightmap => Colors.GetHeightColor(FindSurfaceY(chunk, lx, lz)),
            LayerMode.Cave => GetCaveColor(chunk, lx, lz),
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
                        (byte)Math.Round(ore.R * 0.8 + color.R * 0.2),
                        (byte)Math.Round(ore.G * 0.8 + color.G * 0.2),
                        (byte)Math.Round(ore.B * 0.8 + color.B * 0.2));
                    break;
                }
            }
        }

        return color;
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

    /// <summary>Finds the Y of the cave floor below the surface, if any (used by Cave mode).</summary>
    private static int? FindCaveFloorY(ChunkData chunk, int lx, int lz)
    {
        int surfaceY = FindSurfaceY(chunk, lx, lz);
        for (int y = surfaceY - 1; y >= -ChunkData.YOffset; y--)
        {
            string name = chunk.GetBlock(lx, y, lz);
            if (Colors.AirBlocks.Contains(name) || name == "minecraft:cave_air")
            {
                return y - 1;
            }
        }
        return null;
    }

    /// <summary>Returns the world Y whose block is actually displayed at (lx, lz) under the given layer config.</summary>
    public static int FindDisplayY(ChunkData chunk, LayerConfig config, int lx, int lz)
    {
        if (config.Mode == LayerMode.Slice) return config.SliceY;
        if (config.Mode == LayerMode.Cave) return FindCaveFloorY(chunk, lx, lz) ?? FindSurfaceY(chunk, lx, lz);
        return FindSurfaceY(chunk, lx, lz);
    }

    private static Rgb GetSurfaceColor(ChunkData chunk, int lx, int lz)
    {
        int surfaceY = FindSurfaceY(chunk, lx, lz);
        string name = chunk.GetBlock(lx, surfaceY, lz);
        // simple shading: blocks slightly above average get lighter
        double shade = Math.Min(1.2, Math.Max(0.5, 0.7 + surfaceY / 200.0));
        var c = Colors.GetBlockColor(name);
        return new Rgb(
            (byte)Math.Round(c.R * shade),
            (byte)Math.Round(c.G * shade),
            (byte)Math.Round(c.B * shade));
    }

    private static Rgb GetCaveColor(ChunkData chunk, int lx, int lz)
    {
        var floorY = FindCaveFloorY(chunk, lx, lz);
        if (floorY is not null)
        {
            return Colors.GetBlockColor(chunk.GetBlock(lx, floorY.Value, lz));
        }
        // No cave found — show surface, dimmed
        int surfaceY = FindSurfaceY(chunk, lx, lz);
        var c = Colors.GetBlockColor(chunk.GetBlock(lx, surfaceY, lz));
        return new Rgb((byte)Math.Round(c.R * 0.3), (byte)Math.Round(c.G * 0.3), (byte)Math.Round(c.B * 0.3));
    }
}
