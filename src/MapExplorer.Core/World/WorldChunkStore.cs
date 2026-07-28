using MapExplorer.Core.Chunk;
using MapExplorer.Core.Region;

namespace MapExplorer.Core.World;

/// <summary>
/// Decodes full ChunkData on demand for a single world/dimension's region directory, backed by a
/// bounded LRU cache. Pairs with the lightweight per-chunk summaries WorldLoader.Load produces for
/// the whole world: most rendering only needs those summaries, and this store exists for the few
/// view modes (Slice, Cave, ore overlay) that need real 3D column data — and then only for chunks
/// currently on screen, not the whole world.
/// </summary>
public sealed class WorldChunkStore(string regionDir, int capacity = 4096)
{
    private readonly LruCache<(int, int), ChunkData> _cache = new(capacity);

    /// <summary>Returns the decoded chunk, from cache or freshly decoded from disk, or null if the
    /// chunk doesn't exist (region file missing, or ungenerated within an existing region file).</summary>
    public ChunkData? GetOrDecode(int cx, int cz)
    {
        var key = (cx, cz);
        if (_cache.TryGetValue(key, out var cached)) return cached;

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
        var raw = RegionFile.ParseChunk(buffer, Path.GetFileName(path), localX, localZ);
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

        _cache.Add(key, decoded);
        return decoded;
    }

    private static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);

    /// <summary>Fixed-capacity, most-recently-used-first cache. Not disposal-aware — callers store
    /// only plain managed data (see ChunkData), so eviction is just dropping the reference.</summary>
    private sealed class LruCache<TKey, TValue> where TKey : notnull
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map = new();
        private readonly LinkedList<(TKey Key, TValue Value)> _order = new();
        private readonly object _lock = new();

        public LruCache(int capacity) => _capacity = capacity;

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
