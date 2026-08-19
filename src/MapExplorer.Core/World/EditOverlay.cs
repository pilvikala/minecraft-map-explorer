using System.Collections.Concurrent;

namespace MapExplorer.Core.World;

/// <summary>
/// The live, in-memory source of truth for "what does this block look like right now" — independent
/// of whether it's been saved to disk. Rendering always consults this before falling back to the
/// decoded on-disk value (see MapCanvasControl), so Save doesn't need to invalidate any read caches:
/// it only flushes these overrides to disk. Entries are never cleared on Save; they remain correct
/// (the disk now matches them too) for the rest of the session.
/// </summary>
public sealed class EditOverlay
{
    public sealed record Edit(int X, int Y, int Z, string Before, string After);

    private readonly object _lock = new();
    private readonly ConcurrentDictionary<(int X, int Y, int Z), string> _current = new();
    private readonly HashSet<(int ChunkX, int ChunkZ)> _dirtyChunks = [];
    private readonly List<List<Edit>> _undoStack = [];
    private readonly List<List<Edit>> _redoStack = [];
    private Dictionary<(int, int, int), Edit>? _openBatch;

    private WorldChunkStore? _chunkStore;

    /// <summary>Fired after a batch commits, an undo, or a redo — with the set of chunks whose
    /// rendered pixels may have changed, so the canvas can drop just those tiles from its cache.</summary>
    public event Action<IReadOnlyCollection<(int ChunkX, int ChunkZ)>>? ChunksInvalidated;

    /// <summary>Fired when switching to a different world/dimension — all overlay state is discarded
    /// since it's keyed by absolute coordinates that mean something different there.</summary>
    public event Action? Reset;

    /// <summary>Must be called whenever the active world/dimension changes — supplies the decoder used
    /// to capture a block's original value the first time it's touched, and clears all overlay state
    /// (edits, undo/redo history) from the previous world.</summary>
    public void RebindWorld(WorldChunkStore chunkStore)
    {
        lock (_lock)
        {
            _chunkStore = chunkStore;
            _current.Clear();
            _dirtyChunks.Clear();
            _undoStack.Clear();
            _redoStack.Clear();
            _openBatch = null;
        }
        Reset?.Invoke();
    }

    public string? GetOverride(int x, int y, int z) => _current.TryGetValue((x, y, z), out var v) ? v : null;

    public bool CanUndo { get { lock (_lock) return _undoStack.Count > 0; } }
    public bool CanRedo { get { lock (_lock) return _redoStack.Count > 0; } }

    public IReadOnlySet<(int ChunkX, int ChunkZ)> DirtyChunks { get { lock (_lock) return _dirtyChunks.ToHashSet(); } }

    /// <summary>All current overlay entries within one chunk, keyed by chunk-local position — used by
    /// WorldEditWriter to build the patch set for that chunk.</summary>
    public IReadOnlyDictionary<(int LocalX, int Y, int LocalZ), string> GetEditsForChunk(int chunkX, int chunkZ)
    {
        var result = new Dictionary<(int, int, int), string>();
        foreach (var ((x, y, z), name) in _current)
        {
            if (FloorDiv(x, 16) == chunkX && FloorDiv(z, 16) == chunkZ)
            {
                result[(x - chunkX * 16, y, z - chunkZ * 16)] = name;
            }
        }
        return result;
    }

    /// <summary>Opens one undo step. Every tool action (a whole paint stroke, one fill, one paste) is
    /// exactly one Begin/EndBatch pair.</summary>
    public void BeginBatch()
    {
        lock (_lock) _openBatch = [];
    }

    public void Set(int x, int y, int z, string blockName)
    {
        lock (_lock)
        {
            if (_openBatch is null) throw new InvalidOperationException("Set called outside a batch");

            var key = (x, y, z);
            if (_openBatch.TryGetValue(key, out var existing))
            {
                _openBatch[key] = existing with { After = blockName };
            }
            else
            {
                string before = _current.TryGetValue(key, out var cur) ? cur : LookupOriginal(x, y, z);
                _openBatch[key] = new Edit(x, y, z, before, blockName);
            }

            _current[key] = blockName;
            _dirtyChunks.Add((FloorDiv(x, 16), FloorDiv(z, 16)));
        }
    }

    public void EndBatch()
    {
        List<(int, int)> touchedChunks;
        lock (_lock)
        {
            if (_openBatch is null) return;
            var changed = _openBatch.Values.Where(e => e.Before != e.After).ToList();
            _openBatch = null;
            if (changed.Count == 0) return;

            _undoStack.Add(changed);
            _redoStack.Clear();
            touchedChunks = changed.Select(e => (FloorDiv(e.X, 16), FloorDiv(e.Z, 16))).Distinct().ToList();
        }
        ChunksInvalidated?.Invoke(touchedChunks);
    }

    public void Undo()
    {
        List<(int, int)> touched;
        lock (_lock)
        {
            if (_undoStack.Count == 0) return;
            var batch = _undoStack[^1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            foreach (var e in batch) _current[(e.X, e.Y, e.Z)] = e.Before;
            _redoStack.Add(batch);
            touched = batch.Select(e => (FloorDiv(e.X, 16), FloorDiv(e.Z, 16))).Distinct().ToList();
        }
        ChunksInvalidated?.Invoke(touched);
    }

    public void Redo()
    {
        List<(int, int)> touched;
        lock (_lock)
        {
            if (_redoStack.Count == 0) return;
            var batch = _redoStack[^1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            foreach (var e in batch) _current[(e.X, e.Y, e.Z)] = e.After;
            _undoStack.Add(batch);
            touched = batch.Select(e => (FloorDiv(e.X, 16), FloorDiv(e.Z, 16))).Distinct().ToList();
        }
        ChunksInvalidated?.Invoke(touched);
    }

    private string LookupOriginal(int x, int y, int z)
    {
        if (_chunkStore is null) return "minecraft:air";
        int cx = FloorDiv(x, 16), cz = FloorDiv(z, 16), sectionY = FloorDiv(y, 16);
        var slice = _chunkStore.GetOrDecodeSlice(cx, cz, sectionY);
        if (slice is null) return "minecraft:air";
        int lx = x - cx * 16, lz = z - cz * 16, localY = y - sectionY * 16;
        return slice.GetBlock(lx, localY, lz);
    }

    private static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);
}
