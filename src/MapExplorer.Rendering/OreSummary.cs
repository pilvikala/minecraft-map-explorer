using MapExplorer.Core.Chunk;

namespace MapExplorer.Rendering;

/// <summary>One ore block found in a chunk: which ore type (index into Colors.OreBlockNames) and at
/// what world Y. See OreSummary for how these are grouped per chunk.</summary>
public readonly record struct OreHit(byte OreType, short Y);

/// <summary>
/// Per-chunk record of where ore is, built once at world-load time (see OreSummaryBuilder) instead of
/// rescanned on every render — same idea as ChunkSummary, applied to the ore overlay's "is there ore
/// under this pixel" question instead of "what's the surface block." Keyed by column (index =
/// lx*16+lz); a column only appears here if it has at least one ore block, and only ever holds the
/// topmost occurrence of each distinct ore type in that column (a deeper block of the same type could
/// never be the one shown, since the overlay always displays the shallowest match for whichever ore
/// types are selected) — so despite spanning the same 256 columns as ChunkSummary, this stays tiny:
/// most columns have no entry at all, and the ones that do rarely list more than a couple of ore types.
/// </summary>
public sealed class OreSummary
{
    public required int ChunkX { get; init; }
    public required int ChunkZ { get; init; }
    public required Dictionary<int, OreHit[]> ByColumn { get; init; }
}

public static class OreSummaryBuilder
{
    public static OreSummary Build(ChunkData chunk)
    {
        Dictionary<int, OreHit[]>? byColumn = null;

        for (int lz = 0; lz < 16; lz++)
        {
            for (int lx = 0; lx < 16; lx++)
            {
                int surfaceY = ChunkRenderer.FindSurfaceY(chunk, lx, lz);
                uint seenTypes = 0; // bitset over Colors.OreBlockNames' indices (well under 32 entries)
                List<OreHit>? hits = null;

                for (int y = surfaceY; y >= -ChunkData.YOffset; y--)
                {
                    string name = chunk.GetBlock(lx, y, lz);
                    if (!Colors.OreBlockIndex.TryGetValue(name, out byte oreType)) continue;

                    uint bit = 1u << oreType;
                    if ((seenTypes & bit) != 0) continue; // deeper occurrence of an already-recorded type

                    seenTypes |= bit;
                    (hits ??= new List<OreHit>()).Add(new OreHit(oreType, (short)y));
                }

                if (hits is not null)
                {
                    byColumn ??= new Dictionary<int, OreHit[]>();
                    byColumn[lx * 16 + lz] = hits.ToArray();
                }
            }
        }

        return new OreSummary { ChunkX = chunk.ChunkX, ChunkZ = chunk.ChunkZ, ByColumn = byColumn ?? new Dictionary<int, OreHit[]>() };
    }
}
