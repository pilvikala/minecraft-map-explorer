using System.Collections.Concurrent;
using MapExplorer.Core.World;
using MapExplorer.Rendering;

namespace MapExplorer.App.Views;

/// <summary>
/// Everything MapCanvasControl needs for a loaded world/dimension, bound as one unit so a world
/// switch (or dimension switch) replaces all four pieces atomically — Summaries alone wouldn't be
/// enough to interpret a stale ChunkStore/palette pair from a previous load. Summaries covers every
/// chunk in the world for the cost of a small always-resident struct-of-arrays per chunk; ChunkStore
/// decodes full ChunkData on demand (LRU-bounded) for the view modes that need real column data.
/// </summary>
public sealed class LoadedChunkData
{
    public required ConcurrentDictionary<(int, int), ChunkSummary> Summaries { get; init; }
    public required WorldChunkStore ChunkStore { get; init; }
    public required NamePalette BlockNames { get; init; }
    public required NamePalette BiomeNames { get; init; }
}
