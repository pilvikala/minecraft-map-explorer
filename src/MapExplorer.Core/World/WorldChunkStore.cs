using MapExplorer.Core.Chunk;
using MapExplorer.Core.Region;

namespace MapExplorer.Core.World;

/// <summary>
/// Decodes chunk data on demand for a single world/dimension's region directory, backed by bounded
/// LRU caches. Pairs with the lightweight per-chunk summaries WorldLoader.Load produces for the
/// whole world (a ChunkSummary and an OreSummary per chunk, see MapExplorer.Rendering) — rendering,
/// including the ore overlay, only needs those. GetOrDecode (a full ChunkData) is kept as a general
/// on-demand accessor — e.g. the --check/--bench CLI tooling in Program.cs — but nothing in the
/// interactive render path calls it anymore; ore rendering used to force it for every mode (it scanned
/// an entire column looking for ore) until OreSummary made that unnecessary. GetOrDecodeSlice (one
/// 16-tall section, for Slice mode, which only ever needs one Y level) is unaffected. Both are
/// on-demand and only for chunks currently requested, not the whole world.
/// </summary>
public sealed class WorldChunkStore(string regionDir, int capacity = 4096, int sliceCapacity = 8192)
{
    private readonly LruCache<(int, int), ChunkData> _fullCache = new(capacity);
    private readonly LruCache<(int, int, int), ChunkSliceData> _sliceCache = new(sliceCapacity);

    /// <summary>Returns the fully decoded chunk, from cache or freshly decoded from disk, or null if
    /// the chunk doesn't exist (region file missing, or ungenerated within an existing region file).</summary>
    public ChunkData? GetOrDecode(int cx, int cz)
    {
        var key = (cx, cz);
        if (_fullCache.TryGetValue(key, out var cached)) return cached;

        var raw = ReadRaw(cx, cz);
        if (raw is null) return null;

        ChunkData decoded;
        try
        {
            decoded = ChunkDecoder.Decode(raw.Value.Data, cx, cz);
        }
        catch
        {
            return null;
        }

        _fullCache.Add(key, decoded);
        return decoded;
    }

    /// <summary>Returns just the one 16-tall section containing `sectionY`, from cache or freshly
    /// decoded from disk (skipping every other section's block data and all biome data — see
    /// ChunkDecoder.DecodeSection), or null if the chunk doesn't exist at all. A chunk that exists
    /// but has no data at this particular section still returns a (non-null) all-air slice.</summary>
    public ChunkSliceData? GetOrDecodeSlice(int cx, int cz, int sectionY)
    {
        var key = (cx, cz, sectionY);
        if (_sliceCache.TryGetValue(key, out var cached)) return cached;

        var raw = ReadRaw(cx, cz);
        if (raw is null) return null;

        ChunkSliceData decoded;
        try
        {
            decoded = ChunkDecoder.DecodeSection(raw.Value.Data, cx, cz, sectionY);
        }
        catch
        {
            return null;
        }

        _sliceCache.Add(key, decoded);
        return decoded;
    }

    private RawChunk? ReadRaw(int cx, int cz)
    {
        int regionX = FloorDiv(cx, 32);
        int regionZ = FloorDiv(cz, 32);
        string path = Path.Combine(regionDir, $"r.{regionX}.{regionZ}.mca");

        byte[] buffer;
        try
        {
            buffer = File.ReadAllBytes(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        int localX = cx - regionX * 32;
        int localZ = cz - regionZ * 32;
        return RegionFile.ParseChunk(buffer, Path.GetFileName(path), localX, localZ);
    }

    /// <summary>Drops any cached full decode of this chunk — called after WorldEditWriter.Save writes
    /// new bytes for it, so a subsequent GetOrDecode (used to re-summarize the edited chunk) can't
    /// return a decode from before the edit. GetOrDecodeSlice's cache is deliberately left alone: the
    /// edit-mode overlay is always consulted before any cached slice pixel is used (see
    /// MapCanvasControl), so a stale cached slice never affects what gets rendered.</summary>
    public void InvalidateChunk(int cx, int cz) => _fullCache.Remove((cx, cz));

    private static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);

    /// <summary>Fixed-capacity, most-recently-used-first cache. Not disposal-aware — callers store
    /// only plain managed data (see ChunkData/ChunkSliceData), so eviction is just dropping the reference.</summary>
    private sealed class LruCache<TKey, TValue> where TKey : notnull
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map = new();
        private readonly LinkedList<(TKey Key, TValue Value)> _order = new();
        private readonly object _lock = new();

        public LruCache(int capacity) => _capacity = capacity;

        public void Remove(TKey key)
        {
            lock (_lock)
            {
                if (_map.Remove(key, out var node)) _order.Remove(node);
            }
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            lock (_lock)
            {
                if (_map.TryGetValue(key, out var node))
                {
                    _order.Remove(node);
                    _order.AddFirst(node);
                    value = node.Value.Value;
                    return true;
                }
            }
            value = default!;
            return false;
        }

        public void Add(TKey key, TValue value)
        {
            lock (_lock)
            {
                if (_map.TryGetValue(key, out var existing))
                {
                    _order.Remove(existing);
                }

                var node = new LinkedListNode<(TKey, TValue)>((key, value));
                _order.AddFirst(node);
                _map[key] = node;

                while (_map.Count > _capacity)
                {
                    var last = _order.Last!;
                    _order.RemoveLast();
                    _map.Remove(last.Value.Key);
                }
            }
        }
    }
}
