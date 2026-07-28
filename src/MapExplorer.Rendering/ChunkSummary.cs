using System.Collections.Concurrent;
using MapExplorer.Core.Chunk;

namespace MapExplorer.Rendering;

/// <summary>
/// Thread-safe string-to-index interning table, shared across a whole world load so that repeated
/// block/biome names (the overwhelming majority of the ~225k-chunk case) cost one small integer per
/// occurrence instead of a fresh string reference each. Two instances are used per world: one for
/// block names, one for biome names.
/// </summary>
public sealed class NamePalette
{
    private readonly ConcurrentDictionary<string, ushort> _indices = new();
    private readonly ConcurrentDictionary<ushort, string> _names = new();
    private int _nextIndex = -1;

    public ushort GetOrAdd(string name) =>
        _indices.GetOrAdd(name, n =>
        {
            int idx = Interlocked.Increment(ref _nextIndex);
            if (idx > ushort.MaxValue) throw new InvalidOperationException($"NamePalette exceeded {ushort.MaxValue} distinct names");
            var index = (ushort)idx;
            _names[index] = n;
            return index;
        });

    public string this[int index] => _names[(ushort)index];
}

/// <summary>
/// Per-chunk, per-column (16x16, index = lx*16+lz) summary: the topmost non-air block's Y, its name
/// (via a NamePalette), and the biome at that point. Cheap enough (~1.3KB) to keep resident for every
/// chunk in a loaded world — unlike a full ChunkData (~200KB), which is only decoded on demand for
/// view modes that need real column data (see WorldChunkStore).
/// </summary>
public sealed class ChunkSummary
{
    public required int ChunkX { get; init; }
    public required int ChunkZ { get; init; }
    public required short[] SurfaceY { get; init; }
    public required ushort[] TopBlockIndex { get; init; }
    public required byte[] BiomeIndex { get; init; }
}

public static class ChunkSummaryBuilder
{
    public static ChunkSummary Build(ChunkData chunk, NamePalette blockNames, NamePalette biomeNames)
    {
        var surfaceY = new short[256];
        var topBlockIndex = new ushort[256];
        var biomeIndex = new byte[256];

        for (int lz = 0; lz < 16; lz++)
        {
            for (int lx = 0; lx < 16; lx++)
            {
                int idx = lx * 16 + lz;
                int y = ChunkRenderer.FindSurfaceY(chunk, lx, lz);
                surfaceY[idx] = (short)y;
                topBlockIndex[idx] = blockNames.GetOrAdd(chunk.GetBlock(lx, y, lz));

                int biomeIdx = biomeNames.GetOrAdd(chunk.GetBiome(lx, y, lz));
                if (biomeIdx > byte.MaxValue) throw new InvalidOperationException("Biome palette exceeded 255 distinct biomes");
                biomeIndex[idx] = (byte)biomeIdx;
            }
        }

        return new ChunkSummary
        {
            ChunkX = chunk.ChunkX,
            ChunkZ = chunk.ChunkZ,
            SurfaceY = surfaceY,
            TopBlockIndex = topBlockIndex,
            BiomeIndex = biomeIndex
        };
    }
}
