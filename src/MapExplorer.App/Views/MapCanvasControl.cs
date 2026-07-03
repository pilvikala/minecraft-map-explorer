using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using MapExplorer.Core.Chunk;
using MapExplorer.Rendering;

namespace MapExplorer.App.Views;

public sealed class HoveredBlock
{
    public required int X { get; init; }
    public required int Y { get; init; }
    public required int Z { get; init; }
    public required string Name { get; init; }
}

// Ported from the Electron app's src/renderer/src/components/MapCanvas.tsx —
// same viewport model (offsetX/offsetZ/zoom), same pan/zoom/hover math, same
// per-chunk tile cache keyed by layer config. Adds mitigations the TS version
// needed and this port initially skipped: a macro-tile path that bounds
// DrawImage call count when zoomed out, a snapshot-and-blit path so in-drag
// frames don't re-walk the tile grid, a whole-world low-res "overview" bitmap
// (1px/chunk, always instantly available) drawn as a backdrop, and
// asynchronous tile/macro-tile generation so Render() never blocks the UI
// thread on per-pixel work — newly visible areas show the overview and sharpen
// in as background builds land, instead of freezing or flashing black.
public sealed class MapCanvasControl : Control
{
    private const int ChunkSize = 16;

    // Chunks per side of a macro tile. Macro tiles are pixel-identical to the
    // per-chunk tiles they replace (1 px/block either way) — this only
    // changes how many DrawImage calls it takes to cover the viewport, not
    // what gets drawn. Threshold is exactly one macro tile's worth of chunks:
    // below it, per-chunk drawing already keeps call counts low; above it,
    // batching into macro tiles can only reduce the count further.
    private const int MacroTileChunks = 16;
    private const int MacroTileThreshold = MacroTileChunks * MacroTileChunks;

    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)));
    private static readonly Pen CrosshairPen = new(new SolidColorBrush(Color.FromArgb(153, 255, 60, 60)));

    // Viewport lives as plain mutable control-local state, not an observable
    // property — it changes on every pan/zoom frame, and routing that through
    // INotifyPropertyChanged would be pure overhead on the hottest path in the
    // app (see rewrite plan §4).
    private double _offsetX;
    private double _offsetZ;
    private double _zoom = 2.0;

    private ConcurrentDictionary<(int, int), ChunkData>? _chunks;
    private LayerConfig _config = new();
    private readonly LruBitmapCache _tileCache = new(capacity: 8192);
    private readonly LruBitmapCache _macroTileCache = new(capacity: 384);

    // Builds/config-changes in flight, keyed so a slow background build
    // doesn't get requested twice while it's still running.
    private readonly HashSet<(int, int)> _pendingChunkTiles = new();
    private readonly HashSet<(int, int)> _pendingMacroTiles = new();

    // Guards the four fields above. Render() (and hence the GetOrRequest* calls
    // it makes) is not guaranteed to run on Dispatcher.UIThread — Avalonia may
    // record it on a separate render thread — while background build
    // completions always marshal back via Dispatcher.UIThread.Post. Without
    // this lock those two threads can race on the same HashSet/Dictionary,
    // which can leave a tile's "pending" flag stuck forever: it never gets
    // rebuilt and that one spot stays stuck showing the blurry overview.
    private readonly object _cacheLock = new();

    // Whole-world backdrop: one pixel per chunk (its color at a representative
    // point), covering every loaded chunk. Cheap enough to rebuild in the
    // background on every chunk/config change (one sample per chunk instead
    // of 256), and always drawn first so there's never a blank/black frame
    // while detail tiles are still being built.
    private WriteableBitmap? _overview;
    private int _overviewOriginX;
    private int _overviewOriginZ;
    private int _overviewGeneration;

    private bool _dragging;
    private Point _lastPointerPos;
    private Point _dragStartScreenPos;
    // Full-quality render captured once at drag start; every subsequent
    // pointer-move during the drag just blits this translated by the
    // screen-space delta instead of re-walking the chunk/macro tile grid.
    // The precise grid render only happens again once, when the drag ends.
    private RenderTargetBitmap? _dragSnapshot;

    // True while the bound Chunks dictionary may still gain entries (a world load in
    // progress). Chunks arrive one whole region file at a time, and a macro tile can straddle
    // two region files that finish at different moments — building and caching that macro
    // tile right after the first region lands but before the second would permanently freeze
    // it with a hole, since nothing ever invalidates an already-cached tile when more chunks
    // quietly arrive for its area. So while this is true, Render() shows only the overview
    // (which is cheap enough to fully rebuild from scratch on every progress tick, so it's
    // always correct for whatever fraction of the world is known so far, never stale) and
    // skips building/caching full-detail tiles altogether.
    private bool _isLoading;

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (_isLoading == value) return;
            _isLoading = value;
            if (!_isLoading)
            {
                // Loading just finished. Discard anything a straddling-region race might have
                // cached with incomplete data — the next paint rebuilds from-scratch against
                // the now-final chunk set.
                lock (_cacheLock)
                {
                    _tileCache.Clear();
                    _macroTileCache.Clear();
                    _pendingChunkTiles.Clear();
                    _pendingMacroTiles.Clear();
                }
            }
            InvalidateVisual();
        }
    }

    public event Action<HoveredBlock?>? HoveredBlockChanged;

    public ConcurrentDictionary<(int, int), ChunkData>? Chunks
    {
        get => _chunks;
        set
        {
            _chunks = value;
            // A new dictionary can reuse chunk-coordinate keys from a
            // previously loaded world with different block data — stale
            // cached tiles would otherwise render the old world's pixels.
            lock (_cacheLock)
            {
                _tileCache.Clear();
                _macroTileCache.Clear();
                _pendingChunkTiles.Clear();
                _pendingMacroTiles.Clear();
            }
            RequestOverviewRebuild();
            InvalidateVisual();
        }
    }

    public LayerConfig Config
    {
        get => _config;
        set
        {
            if (_config == value) return;
            _config = value;
            lock (_cacheLock)
            {
                _tileCache.Clear(); // config affects every tile's pixels — cheap to just rebuild lazily on next paint
                _macroTileCache.Clear();
                _pendingChunkTiles.Clear();
                _pendingMacroTiles.Clear();
            }
            RequestOverviewRebuild();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Call when the same Chunks dictionary just received more entries — e.g. on each
    /// progress tick during a world load, so the map progressively paints in instead of
    /// staying black until the whole load finishes. Refreshes the overview backdrop (so it
    /// reflects the newly-arrived chunks) but doesn't touch the detail-tile caches: already
    /// decoded chunks never change, so their cached tiles stay valid.
    /// </summary>
    public void NotifyChunksGrew()
    {
        RequestOverviewRebuild();
        InvalidateVisual();
    }

    public MapCanvasControl()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragging = true;
            _lastPointerPos = e.GetPosition(this);
            _dragStartScreenPos = _lastPointerPos;
            CaptureDragSnapshot();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);

        if (_dragging)
        {
            var dx = pos.X - _lastPointerPos.X;
            var dy = pos.Y - _lastPointerPos.Y;
            _lastPointerPos = pos;
            _offsetX -= dx / _zoom;
            _offsetZ -= dy / _zoom;
            InvalidateVisual();
            return; // hover has no visible effect while dragging — skip the lookup/scan
        }

        UpdateHover(pos);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        EndDrag();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        EndDrag();
        HoveredBlockChanged?.Invoke(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var factor = e.Delta.Y > 0 ? 1.15 : 1 / 1.15;
        _zoom = Math.Max(0.5, Math.Min(64, _zoom * factor));
        InvalidateVisual();
        UpdateHover(e.GetPosition(this));
        e.Handled = true;
    }

    private void CaptureDragSnapshot()
    {
        int w = (int)Math.Ceiling(Bounds.Width);
        int h = (int)Math.Ceiling(Bounds.Height);
        if (w <= 0 || h <= 0) return;

        // Render into a local first: _dragSnapshot is still the old value
        // (null) while this runs, so the nested Render() call below takes
        // the normal precise path rather than trying to blit itself.
        var snapshot = new RenderTargetBitmap(new PixelSize(w, h));
        snapshot.Render(this);

        _dragSnapshot?.Dispose();
        _dragSnapshot = snapshot;
    }

    private void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        _dragSnapshot?.Dispose();
        _dragSnapshot = null;
        InvalidateVisual();
    }

    private void UpdateHover(Point pos)
    {
        if (_chunks is null) return;

        double w = Bounds.Width, h = Bounds.Height;
        int blockX = (int)Math.Floor(_offsetX + (pos.X - w / 2) / _zoom);
        int blockZ = (int)Math.Floor(_offsetZ + (pos.Y - h / 2) / _zoom);
        int chunkX = FloorDiv(blockX, ChunkSize);
        int chunkZ = FloorDiv(blockZ, ChunkSize);

        if (_chunks.TryGetValue((chunkX, chunkZ), out var chunk))
        {
            int lx = ((blockX % ChunkSize) + ChunkSize) % ChunkSize;
            int lz = ((blockZ % ChunkSize) + ChunkSize) % ChunkSize;
            int y = ChunkRenderer.FindDisplayY(chunk, _config, lx, lz);
            string name = chunk.GetBlock(lx, y, lz);
            HoveredBlockChanged?.Invoke(new HoveredBlock { X = blockX, Y = y, Z = blockZ, Name = name });
        }
        else
        {
            HoveredBlockChanged?.Invoke(null);
        }
    }

    private static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double w = Bounds.Width, h = Bounds.Height;
        context.FillRectangle(Brushes.Black, new Rect(0, 0, w, h));
        if (w <= 0 || h <= 0) return;

        double pixelsPerBlock = _zoom;

        if (_dragging && _dragSnapshot is not null)
        {
            var dx = _lastPointerPos.X - _dragStartScreenPos.X;
            var dy = _lastPointerPos.Y - _dragStartScreenPos.Y;
            context.DrawImage(_dragSnapshot, new Rect(0, 0, w, h), new Rect(dx, dy, w, h));
            return;
        }

        DrawOverview(context, pixelsPerBlock, w, h);

        var chunks = _chunks;
        if (chunks is null) return;

        double pixelsPerChunk = ChunkSize * pixelsPerBlock;

        double startBlockX = _offsetX - w / 2 / pixelsPerBlock;
        double startBlockZ = _offsetZ - h / 2 / pixelsPerBlock;
        double endBlockX = _offsetX + w / 2 / pixelsPerBlock;
        double endBlockZ = _offsetZ + h / 2 / pixelsPerBlock;

        int startChunkX = FloorDiv((int)Math.Floor(startBlockX), ChunkSize);
        int startChunkZ = FloorDiv((int)Math.Floor(startBlockZ), ChunkSize);
        int endChunkX = (int)Math.Ceiling(endBlockX / ChunkSize);
        int endChunkZ = (int)Math.Ceiling(endBlockZ / ChunkSize);

        long visibleChunkCount = (long)(endChunkX - startChunkX + 1) * (endChunkZ - startChunkZ + 1);

        // Full-detail tiles aren't safe to build/cache while the chunk set can still change
        // underneath them (see IsLoading) — the overview drawn above is all we show until
        // loading settles.
        if (!_isLoading)
        {
            var interp = pixelsPerBlock >= 1 ? BitmapInterpolationMode.None : BitmapInterpolationMode.LowQuality;

            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = interp }))
            {
                if (visibleChunkCount > MacroTileThreshold)
                {
                    RenderMacroTiles(context, chunks, startChunkX, startChunkZ, endChunkX, endChunkZ, pixelsPerBlock, w, h);
                }
                else
                {
                    RenderChunkTiles(context, chunks, startChunkX, startChunkZ, endChunkX, endChunkZ, pixelsPerBlock, pixelsPerChunk, w, h);
                }
            }
        }

        // Grid lines at high zoom
        if (_zoom >= 8)
        {
            for (int cx = startChunkX; cx <= endChunkX; cx++)
            {
                double sx = (cx * ChunkSize - _offsetX) * pixelsPerBlock + w / 2;
                context.DrawLine(GridPen, new Point(sx, 0), new Point(sx, h));
            }
            for (int cz = startChunkZ; cz <= endChunkZ; cz++)
            {
                double sz = (cz * ChunkSize - _offsetZ) * pixelsPerBlock + h / 2;
                context.DrawLine(GridPen, new Point(0, sz), new Point(w, sz));
            }
        }

        // Crosshair at world origin
        double ox = (0 - _offsetX) * pixelsPerBlock + w / 2;
        double oz = (0 - _offsetZ) * pixelsPerBlock + h / 2;
        context.DrawLine(CrosshairPen, new Point(ox - 8, oz), new Point(ox + 8, oz));
        context.DrawLine(CrosshairPen, new Point(ox, oz - 8), new Point(ox, oz + 8));
    }

    private void DrawOverview(DrawingContext context, double pixelsPerBlock, double w, double h)
    {
        var overview = _overview;
        if (overview is null) return;

        double screenX = (_overviewOriginX * ChunkSize - _offsetX) * pixelsPerBlock + w / 2;
        double screenZ = (_overviewOriginZ * ChunkSize - _offsetZ) * pixelsPerBlock + h / 2;
        double destW = overview.PixelSize.Width * ChunkSize * pixelsPerBlock;
        double destH = overview.PixelSize.Height * ChunkSize * pixelsPerBlock;

        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.LowQuality }))
        {
            context.DrawImage(
                overview,
                new Rect(0, 0, overview.PixelSize.Width, overview.PixelSize.Height),
                new Rect(screenX, screenZ, destW, destH));
        }
    }

    private void RenderChunkTiles(
        DrawingContext context, ConcurrentDictionary<(int, int), ChunkData> chunks,
        int startChunkX, int startChunkZ, int endChunkX, int endChunkZ,
        double pixelsPerBlock, double pixelsPerChunk, double w, double h)
    {
        for (int cz = startChunkZ; cz <= endChunkZ; cz++)
        {
            for (int cx = startChunkX; cx <= endChunkX; cx++)
            {
                if (!chunks.TryGetValue((cx, cz), out var chunk)) continue;

                var tile = GetOrRequestTile(cx, cz, chunk);
                if (tile is null) continue; // build in flight — overview shows through until it lands

                double screenX = (cx * ChunkSize - _offsetX) * pixelsPerBlock + w / 2;
                double screenZ = (cz * ChunkSize - _offsetZ) * pixelsPerBlock + h / 2;
                // Slightly overshoot each tile's nominal size so neighboring tiles
                // overlap by a fraction of a pixel. Without this, faint seams
                // appear at every chunk boundary from edge sampling in the
                // rasterizer (each tile is a separate DrawImage call) — a known
                // category of artifact in tile-based 2D renderers generally.
                var destRect = new Rect(screenX, screenZ, pixelsPerChunk + 0.75, pixelsPerChunk + 0.75);
                context.DrawImage(tile, new Rect(0, 0, ChunkSize, ChunkSize), destRect);
            }
        }
    }

    private void RenderMacroTiles(
        DrawingContext context, ConcurrentDictionary<(int, int), ChunkData> chunks,
        int startChunkX, int startChunkZ, int endChunkX, int endChunkZ,
        double pixelsPerBlock, double w, double h)
    {
        int startMacroX = FloorDiv(startChunkX, MacroTileChunks);
        int startMacroZ = FloorDiv(startChunkZ, MacroTileChunks);
        int endMacroX = FloorDiv(endChunkX, MacroTileChunks);
        int endMacroZ = FloorDiv(endChunkZ, MacroTileChunks);

        int macroSizeBlocks = MacroTileChunks * ChunkSize;
        double macroSizePixels = macroSizeBlocks * pixelsPerBlock;

        for (int mz = startMacroZ; mz <= endMacroZ; mz++)
        {
            for (int mx = startMacroX; mx <= endMacroX; mx++)
            {
                var tile = GetOrRequestMacroTile(mx, mz, chunks);
                if (tile is null) continue; // no loaded chunks here, or build still in flight — overview shows through

                double screenX = (mx * macroSizeBlocks - _offsetX) * pixelsPerBlock + w / 2;
                double screenZ = (mz * macroSizeBlocks - _offsetZ) * pixelsPerBlock + h / 2;
                var destRect = new Rect(screenX, screenZ, macroSizePixels + 0.75, macroSizePixels + 0.75);
                context.DrawImage(tile, new Rect(0, 0, macroSizeBlocks, macroSizeBlocks), destRect);
            }
        }
    }

    /// <summary>Returns the cached chunk tile, or kicks off a background build and returns null if one isn't ready yet.</summary>
    private WriteableBitmap? GetOrRequestTile(int cx, int cz, ChunkData chunk)
    {
        var key = (cx, cz);
        lock (_cacheLock)
        {
            if (_tileCache.TryGetValue(key, out var cached)) return cached;
            if (!_pendingChunkTiles.Add(key)) return null; // already building
        }

        var config = _config;
        var chunksRef = _chunks;
        Task.Run(() =>
        {
            // If anything here throws, pixels stays null and the tile is simply retried on a
            // later frame — the alternative (letting the exception propagate) would abandon
            // this Task before the Dispatcher.Post below runs, which would never clear the
            // pending flag and permanently stick this tile at overview-only resolution.
            byte[]? pixels;
            try
            {
                pixels = new byte[ChunkSize * ChunkSize * 4];
                for (int lz = 0; lz < ChunkSize; lz++)
                {
                    for (int lx = 0; lx < ChunkSize; lx++)
                    {
                        var color = ChunkRenderer.GetChunkPixelColor(chunk, config, lx, lz);
                        WritePixel(pixels, ChunkSize, lx, lz, color);
                    }
                }
            }
            catch
            {
                pixels = null;
            }

            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    bool stillCurrent = pixels is not null && ReferenceEquals(_chunks, chunksRef) && _config == config;
                    if (stillCurrent)
                    {
                        var bmp = CreateBitmapFromPixels(pixels!, ChunkSize, ChunkSize);
                        lock (_cacheLock) { _tileCache.Add(key, bmp); }
                    }
                }
                finally
                {
                    // Always runs, even if bitmap creation above throws — the pending flag
                    // must be cleared no matter what, or this tile is stuck forever.
                    lock (_cacheLock) { _pendingChunkTiles.Remove(key); }
                    InvalidateVisual();
                }
            });
        });

        return null;
    }

    /// <summary>
    /// Returns the cached macro tile (covering a MacroTileChunks x MacroTileChunks block of
    /// chunks at 1px/block — pixel-identical to drawing each chunk's own tile, just batched
    /// into one DrawImage call per MacroTileChunks^2 chunks instead of one per chunk), kicks
    /// off a background build if none is in flight, or returns null if the area has no loaded
    /// chunks at all.
    /// </summary>
    private WriteableBitmap? GetOrRequestMacroTile(int mx, int mz, ConcurrentDictionary<(int, int), ChunkData> chunks)
    {
        var key = (mx, mz);
        int baseChunkX = mx * MacroTileChunks;
        int baseChunkZ = mz * MacroTileChunks;

        // Check cache/pending quickly under lock.
        lock (_cacheLock)
        {
            if (_macroTileCache.TryGetValue(key, out var cached)) return cached;
            if (_pendingMacroTiles.Contains(key)) return null; // already building
        }

        // Avoid holding _cacheLock while scanning the chunks dictionary.
        bool anyChunkLoaded = false;
        for (int dz = 0; dz < MacroTileChunks && !anyChunkLoaded; dz++)
        {
            for (int dx = 0; dx < MacroTileChunks; dx++)
            {
                if (chunks.ContainsKey((baseChunkX + dx, baseChunkZ + dz))) { anyChunkLoaded = true; break; }
            }
        }

        if (!anyChunkLoaded) return null;

        lock (_cacheLock)
        {
            if (_macroTileCache.TryGetValue(key, out var cached)) return cached;
            if (_pendingMacroTiles.Contains(key)) return null; // already building
            _pendingMacroTiles.Add(key);
        }

        int sizeBlocks = MacroTileChunks * ChunkSize;
        var config = _config;
        var chunksRef = _chunks;
        Task.Run(() =>
        {
            // See GetOrRequestTile for why failures here must not propagate: an unhandled
            // exception would skip the Dispatcher.Post below entirely, and with it the only
            // code that clears the pending flag — permanently sticking this tile.
            byte[]? pixels;
            try
            {
                pixels = new byte[sizeBlocks * sizeBlocks * 4];
                for (int dz = 0; dz < MacroTileChunks; dz++)
                {
                    int cz = baseChunkZ + dz;
                    for (int dx = 0; dx < MacroTileChunks; dx++)
                    {
                        int cx = baseChunkX + dx;
                        if (!chunks.TryGetValue((cx, cz), out var chunk)) continue;

                        int destBaseX = dx * ChunkSize;
                        int destBaseZ = dz * ChunkSize;
                        for (int lz = 0; lz < ChunkSize; lz++)
                        {
                            for (int lx = 0; lx < ChunkSize; lx++)
                            {
                                var color = ChunkRenderer.GetChunkPixelColor(chunk, config, lx, lz);
                                WritePixel(pixels, sizeBlocks, destBaseX + lx, destBaseZ + lz, color);
                            }
                        }
                    }
                }
            }
            catch
            {
                pixels = null;
            }

            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    bool stillCurrent = pixels is not null && ReferenceEquals(_chunks, chunksRef) && _config == config;
                    if (stillCurrent)
                    {
                        var bmp = CreateBitmapFromPixels(pixels!, sizeBlocks, sizeBlocks);
                        lock (_cacheLock) { _macroTileCache.Add(key, bmp); }
                    }
                }
                finally
                {
                    lock (_cacheLock) { _pendingMacroTiles.Remove(key); }
                    InvalidateVisual();
                }
            });
        });

        return null;
    }

    /// <summary>
    /// Kicks off a background rebuild of the whole-world, 1px/chunk backdrop bitmap. Cheap —
    /// one representative color sample per chunk rather than 256 — so it can run to
    /// completion on every chunks/config change without needing incremental updates.
    /// </summary>
    private void RequestOverviewRebuild()
    {
        var chunks = _chunks;
        var config = _config;
        int generation = ++_overviewGeneration;

        if (chunks is null || chunks.IsEmpty)
        {
            _overview?.Dispose();
            _overview = null;
            return;
        }

        Task.Run(() =>
        {
            int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
            foreach (var (cx, cz) in chunks.Keys)
            {
                if (cx < minX) minX = cx;
                if (cx > maxX) maxX = cx;
                if (cz < minZ) minZ = cz;
                if (cz > maxZ) maxZ = cz;
            }

            int width = maxX - minX + 1;
            int height = maxZ - minZ + 1;
            var pixels = new byte[width * height * 4];

            foreach (var kv in chunks)
            {
                var (cx, cz) = kv.Key;
                var color = ChunkRenderer.GetChunkPixelColor(kv.Value, config, ChunkSize / 2, ChunkSize / 2);
                WritePixel(pixels, width, cx - minX, cz - minZ, color);
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (generation != _overviewGeneration) return; // superseded by a newer chunks/config change
                var next = CreateBitmapFromPixels(pixels, width, height);
                var old = _overview;
                _overview = next;
                old?.Dispose();
                _overviewOriginX = minX;
                _overviewOriginZ = minZ;
                InvalidateVisual();
            });
        });
    }

    private static void WritePixel(byte[] pixels, int stridePx, int x, int y, Rgb color)
    {
        int idx = (y * stridePx + x) * 4;
        pixels[idx + 0] = color.B;
        pixels[idx + 1] = color.G;
        pixels[idx + 2] = color.R;
        pixels[idx + 3] = 255;
    }

    /// <summary>Copies a tightly-packed BGRA8888 buffer into a new WriteableBitmap. Must run on the UI thread.</summary>
    private static WriteableBitmap CreateBitmapFromPixels(byte[] pixels, int width, int height)
    {
        var bmp = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = bmp.Lock();
        unsafe
        {
            fixed (byte* src = pixels)
            {
                byte* dst = (byte*)fb.Address;
                int srcStride = width * 4;
                int copyBytes = Math.Min(srcStride, fb.RowBytes);
                for (int y = 0; y < height; y++)
                {
                    Buffer.MemoryCopy(src + y * srcStride, dst + y * fb.RowBytes, fb.RowBytes, copyBytes);
                }
            }
        }
        return bmp;
    }

    /// <summary>Fixed-capacity, most-recently-used-first bitmap cache. Evicted bitmaps are disposed to free their native buffers promptly.</summary>
    private sealed class LruBitmapCache
    {
        private readonly int _capacity;
        private readonly Dictionary<(int, int), LinkedListNode<((int, int) Key, WriteableBitmap Bitmap)>> _map = new();
        private readonly LinkedList<((int, int) Key, WriteableBitmap Bitmap)> _order = new();

        public LruBitmapCache(int capacity) => _capacity = capacity;

        public bool TryGetValue((int, int) key, out WriteableBitmap bitmap)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                bitmap = node.Value.Bitmap;
                return true;
            }
            bitmap = null!;
            return false;
        }

        public void Add((int, int) key, WriteableBitmap bitmap)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                existing.Value.Bitmap.Dispose();
            }

            var node = new LinkedListNode<((int, int) Key, WriteableBitmap Bitmap)>((key, bitmap));
            _order.AddFirst(node);
            _map[key] = node;

            while (_map.Count > _capacity)
            {
                var last = _order.Last!;
                _order.RemoveLast();
                _map.Remove(last.Value.Key);
                last.Value.Bitmap.Dispose();
            }
        }

        public void Clear()
        {
            foreach (var entry in _order) entry.Bitmap.Dispose();
            _map.Clear();
            _order.Clear();
        }
    }
}
