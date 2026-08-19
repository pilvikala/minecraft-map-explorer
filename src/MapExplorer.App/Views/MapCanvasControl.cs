using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using MapExplorer.App.ViewModels;
using MapExplorer.Core.Chunk;
using MapExplorer.Core.World;
using MapExplorer.Rendering;
using RenderColors = MapExplorer.Rendering.Colors;

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

    // Zoom bounds, in screen pixels per block. MinZoom is low enough that even a
    // world spanning tens of thousands of blocks (hundreds of regions) fits in a
    // single window — 1 px covers 64 blocks (4 chunks) at the floor.
    private const double MinZoom = 1.0 / 64;
    private const double MaxZoom = 64;
    private const double WheelZoomFactor = 1.15;
    private const double ButtonZoomFactor = 1.5;

    // Y-Slice mode decodes on demand per visible chunk (see ChunkRenderer.GetDataNeed /
    // WorldChunkStore.GetOrDecodeSlice) rather than reading from the always-resident summary —
    // zooming all the way out to MinZoom in that mode would mean decoding thousands of chunks per
    // frame. Floored at the app's default zoom level: never zoomed out further than what you see on
    // first loading a world.
    private const double SliceModeMinZoom = 2.0;

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
    private static readonly Pen SelectionPen = new(new SolidColorBrush(Color.FromArgb(220, 255, 220, 60)), 1.5);

    // Viewport lives as plain mutable control-local state, not an observable
    // property — it changes on every pan/zoom frame, and routing that through
    // INotifyPropertyChanged would be pure overhead on the hottest path in the
    // app (see rewrite plan §4).
    private double _offsetX;
    private double _offsetZ;
    private double _zoom = 2.0;

    private LoadedChunkData? _world;
    private LayerConfig _config = new();
    private readonly LruBitmapCache _tileCache = new(capacity: 8192);
    private readonly LruBitmapCache _macroTileCache = new(capacity: 384);

    // Builds/config-changes in flight, keyed so a slow background build
    // doesn't get requested twice while it's still running.
    private readonly HashSet<(int, int)> _pendingChunkTiles = new();
    private readonly HashSet<(int, int)> _pendingMacroTiles = new();

    // Chunk tiles known to be out of date (an edit landed since they were built) but still shown
    // while a fresh build is in flight — see MarkChunkTileStaleLocked/GetOrRequestTile. Without this,
    // painting would remove each edited chunk's tile from _tileCache immediately and only put it back
    // once the (async) rebuild finished, which reads as the whole chunk flickering to the blurry
    // overview backdrop and back on every single block painted.
    private readonly HashSet<(int, int)> _staleChunkTiles = new();

    // Bounds how many tile/macro-tile builds run at once. A first paint of a large viewport
    // can discover hundreds or thousands of missing tiles in one Render() pass; queuing all of
    // them as unbounded Task.Run work would flood the thread pool and delay unrelated work
    // (including chunk decoding) queued alongside it. Shared across both build kinds since
    // they compete for the same thread pool. WaitAsync (not a blocking Wait) means a build
    // queued behind a full semaphore gives its thread straight back to the pool instead of
    // parking it, so the only thing actually bounded is concurrent pixel-crunching, not the
    // (cheap) act of queuing.
    private static readonly SemaphoreSlim TileBuildConcurrency = new(Math.Max(2, Environment.ProcessorCount));

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

    private EditViewModel? _editContext;
    private bool _toolDragging;
    private bool _toolIsPrimary;
    private (int X, int Z)? _lastPaintedBlock;
    private (int X, int Z)? _selectionStart;
    private (int X, int Z)? _selectionCurrent;

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
                    _staleChunkTiles.Clear();
                }
            }
            InvalidateVisual();
        }
    }

    public event Action<HoveredBlock?>? HoveredBlockChanged;

    public LoadedChunkData? World
    {
        get => _world;
        set
        {
            _world = value;
            // New world data can reuse chunk-coordinate keys from a
            // previously loaded world with different block data — stale
            // cached tiles would otherwise render the old world's pixels.
            lock (_cacheLock)
            {
                _tileCache.Clear();
                _macroTileCache.Clear();
                _pendingChunkTiles.Clear();
                _pendingMacroTiles.Clear();
                _staleChunkTiles.Clear();
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
                _staleChunkTiles.Clear();
            }
            SetZoom(_zoom); // re-clamp against the new mode's zoom floor (e.g. entering Slice mode already zoomed out too far)
            RequestOverviewRebuild();
            InvalidateVisual();
        }
    }

    /// <summary>Wires up edit-mode tool state and the overlay whose unsaved edits render on top of
    /// the normal Slice-mode pixels (set once from MainWindow, alongside Config).</summary>
    public EditViewModel? EditContext
    {
        get => _editContext;
        set
        {
            if (ReferenceEquals(_editContext, value)) return;
            if (_editContext is not null)
            {
                _editContext.Overlay.ChunksInvalidated -= OnOverlayChunksInvalidated;
                _editContext.Overlay.Reset -= OnOverlayReset;
                _editContext.PropertyChanged -= OnEditContextPropertyChanged;
            }
            _editContext = value;
            if (_editContext is not null)
            {
                _editContext.Overlay.ChunksInvalidated += OnOverlayChunksInvalidated;
                _editContext.Overlay.Reset += OnOverlayReset;
                _editContext.PropertyChanged += OnEditContextPropertyChanged;
            }
            InvalidateVisual();
        }
    }

    // Toggling edit mode on/off changes whether the "layer below" preview renders through air (see
    // BuildChunkTilePixels) — already-cached tiles were built without knowing that, so they need to
    // be dropped for the new state to actually show up.
    private void OnEditContextPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EditViewModel.IsEditModeOn)) return;
        lock (_cacheLock)
        {
            _tileCache.Clear();
            _macroTileCache.Clear();
            _pendingChunkTiles.Clear();
            _pendingMacroTiles.Clear();
            _staleChunkTiles.Clear();
        }
        InvalidateVisual();
    }

    // Paint invalidates the single chunk it just touched itself (see PaintAt), for immediate
    // per-block feedback while dragging. This event instead covers the "one operation, redraw once
    // it's done" tools (Fill, Paste) plus Undo/Redo, none of which need per-block granularity.
    private void OnOverlayChunksInvalidated(IReadOnlyCollection<(int ChunkX, int ChunkZ)> chunks)
    {
        lock (_cacheLock)
        {
            foreach (var (cx, cz) in chunks) MarkChunkTileStaleLocked(cx, cz);
        }
        InvalidateVisual();
    }

    private void OnOverlayReset()
    {
        lock (_cacheLock)
        {
            _tileCache.Clear();
            _macroTileCache.Clear();
            _pendingChunkTiles.Clear();
            _pendingMacroTiles.Clear();
            _staleChunkTiles.Clear();
        }
        InvalidateVisual();
    }

    /// <summary>Flags a chunk's tile for rebuild without dropping the current (about-to-be-stale) one
    /// out of the cache — GetOrRequestTile keeps returning it until the rebuild lands, so the chunk
    /// never has a frame with nothing drawn for it. Macro tiles are still dropped outright: they're
    /// essentially never on screen while editing (SliceModeMinZoom keeps that zoomed in), so the
    /// same flicker risk doesn't apply and isn't worth the extra bookkeeping.</summary>
    private void MarkChunkTileStaleLocked(int cx, int cz)
    {
        _staleChunkTiles.Add((cx, cz));
        _macroTileCache.Remove((FloorDiv(cx, MacroTileChunks), FloorDiv(cz, MacroTileChunks)));
    }

    /// <summary>
    /// Call when the same World.Summaries dictionary just received more entries — e.g. on each
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

    /// <summary>Recenters the camera by scaling the current block-space center — e.g. by 1/8 or 8
    /// when following a Nether portal's coordinate link to/from the Overworld.</summary>
    public void RescaleView(double factor)
    {
        _offsetX *= factor;
        _offsetZ *= factor;
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
        var props = e.GetCurrentPoint(this).Properties;
        var edit = _editContext;

        if (edit is { IsEditModeOn: true })
        {
            // Left/right are the two materials in edit mode, so panning moves to the middle button.
            if (props.IsMiddleButtonPressed) StartPan(e.GetPosition(this));
            else if (props.IsLeftButtonPressed) BeginToolAction(e.GetPosition(this), edit, isPrimary: true);
            else if (props.IsRightButtonPressed) BeginToolAction(e.GetPosition(this), edit, isPrimary: false);
            return;
        }

        if (props.IsLeftButtonPressed) StartPan(e.GetPosition(this));
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

        if (_toolDragging && _editContext is { } edit)
        {
            ContinueToolAction(ScreenToBlock(pos), edit);
            return;
        }

        UpdateHover(pos);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging) { EndDrag(); return; }
        FinishToolAction();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        EndDrag();
        FinishToolAction();
        HoveredBlockChanged?.Invoke(null);
    }

    private void StartPan(Point pos)
    {
        _dragging = true;
        _lastPointerPos = pos;
        _dragStartScreenPos = pos;
        CaptureDragSnapshot();
    }

    private (int X, int Z) ScreenToBlock(Point pos)
    {
        double w = Bounds.Width, h = Bounds.Height;
        int blockX = (int)Math.Floor(_offsetX + (pos.X - w / 2) / _zoom);
        int blockZ = (int)Math.Floor(_offsetZ + (pos.Y - h / 2) / _zoom);
        return (blockX, blockZ);
    }

    private void BeginToolAction(Point pos, EditViewModel edit, bool isPrimary)
    {
        var world = _world;
        if (world is null) return;
        var (worldX, worldZ) = ScreenToBlock(pos);
        string material = isPrimary ? edit.PrimaryMaterial : edit.SecondaryMaterial;

        switch (edit.SelectedTool)
        {
            case EditTool.Paint:
                _toolDragging = true;
                _toolIsPrimary = isPrimary;
                _lastPaintedBlock = null;
                edit.Overlay.BeginBatch();
                PaintAt(worldX, worldZ, material, edit.Overlay);
                break;
            case EditTool.Fill:
                edit.Overlay.BeginBatch();
                FloodFillAt(worldX, worldZ, material, world, edit.Overlay);
                edit.Overlay.EndBatch();
                break;
            case EditTool.Copy:
                _toolDragging = true;
                _selectionStart = (worldX, worldZ);
                _selectionCurrent = (worldX, worldZ);
                InvalidateVisual();
                break;
            case EditTool.Paste:
                PasteAt(worldX, worldZ, edit);
                break;
            case EditTool.Picker:
                PickAt(worldX, worldZ, world, edit, isPrimary);
                break;
        }
    }

    private void ContinueToolAction((int X, int Z) block, EditViewModel edit)
    {
        switch (edit.SelectedTool)
        {
            case EditTool.Paint when _lastPaintedBlock != block:
                string material = _toolIsPrimary ? edit.PrimaryMaterial : edit.SecondaryMaterial;
                PaintAt(block.X, block.Z, material, edit.Overlay);
                break;
            case EditTool.Copy:
                _selectionCurrent = block;
                InvalidateVisual();
                break;
        }
    }

    private void FinishToolAction()
    {
        if (!_toolDragging) return;
        _toolDragging = false;
        _lastPaintedBlock = null;

        var edit = _editContext;
        switch (edit?.SelectedTool)
        {
            case EditTool.Paint:
                edit.Overlay.EndBatch();
                break;
            case EditTool.Copy:
                FinishCopy(edit);
                break;
        }
    }

    private void PaintAt(int worldX, int worldZ, string material, EditOverlay overlay)
    {
        overlay.Set(worldX, _config.SliceY, worldZ, material);
        _lastPaintedBlock = (worldX, worldZ);
        lock (_cacheLock) { MarkChunkTileStaleLocked(FloorDiv(worldX, ChunkSize), FloorDiv(worldZ, ChunkSize)); }
        InvalidateVisual();
    }

    // 4-connected, within the current layer only — matches "editing a single layer." Bounded so a
    // click on a huge contiguous area (e.g. the ocean) can't run away decoding the whole world.
    private const int FloodFillMaxCells = 200_000;

    private void FloodFillAt(int startX, int startZ, string material, LoadedChunkData world, EditOverlay overlay)
    {
        string target = GetEffectiveBlock(startX, startZ, world, overlay);
        if (target == material) return;

        var visited = new HashSet<(int, int)> { (startX, startZ) };
        var queue = new Queue<(int, int)>();
        queue.Enqueue((startX, startZ));
        var touchedChunks = new HashSet<(int, int)>();
        int touched = 0;

        while (queue.Count > 0 && touched < FloodFillMaxCells)
        {
            var (x, z) = queue.Dequeue();
            overlay.Set(x, _config.SliceY, z, material);
            touchedChunks.Add((FloorDiv(x, ChunkSize), FloorDiv(z, ChunkSize)));
            touched++;

            foreach (var next in new[] { (x + 1, z), (x - 1, z), (x, z + 1), (x, z - 1) })
            {
                if (!visited.Add(next)) continue;
                if (GetEffectiveBlock(next.Item1, next.Item2, world, overlay) == target) queue.Enqueue(next);
            }
        }

        lock (_cacheLock) { foreach (var (cx, cz) in touchedChunks) MarkChunkTileStaleLocked(cx, cz); }
        InvalidateVisual();
    }

    private void FinishCopy(EditViewModel edit)
    {
        var start = _selectionStart;
        var end = _selectionCurrent;
        _selectionStart = null;
        _selectionCurrent = null;
        InvalidateVisual();

        if (start is not { } s || end is not { } en || _world is not { } world) return;

        int minX = Math.Min(s.X, en.X), maxX = Math.Max(s.X, en.X);
        int minZ = Math.Min(s.Z, en.Z), maxZ = Math.Max(s.Z, en.Z);
        int width = maxX - minX + 1, depth = maxZ - minZ + 1;
        if ((long)width * depth > FloodFillMaxCells) return; // selection too large — silently ignored, same cap as flood fill

        var blocks = new string[width, depth];
        for (int dz = 0; dz < depth; dz++)
        for (int dx = 0; dx < width; dx++)
            blocks[dx, dz] = GetEffectiveBlock(minX + dx, minZ + dz, world, edit.Overlay);

        edit.Clipboard = new ClipboardData(width, depth, blocks);
    }

    private void PasteAt(int worldX, int worldZ, EditViewModel edit)
    {
        if (edit.Clipboard is not { } clip || _world is null) return;

        edit.Overlay.BeginBatch();
        var touchedChunks = new HashSet<(int, int)>();
        for (int dz = 0; dz < clip.Depth; dz++)
        for (int dx = 0; dx < clip.Width; dx++)
        {
            int x = worldX + dx, z = worldZ + dz;
            edit.Overlay.Set(x, _config.SliceY, z, clip.Blocks[dx, dz]);
            touchedChunks.Add((FloorDiv(x, ChunkSize), FloorDiv(z, ChunkSize)));
        }
        edit.Overlay.EndBatch();

        lock (_cacheLock) { foreach (var (cx, cz) in touchedChunks) MarkChunkTileStaleLocked(cx, cz); }
        InvalidateVisual();
    }

    private void PickAt(int worldX, int worldZ, LoadedChunkData world, EditViewModel edit, bool isPrimary)
    {
        string block = GetEffectiveBlock(worldX, worldZ, world, edit.Overlay);
        edit.AssignToSlot(block, isPrimary ? MaterialSlot.Left : MaterialSlot.Right);
    }

    /// <summary>The block at (worldX, worldZ, current SliceY) as it renders right now — the overlay's
    /// unsaved edit if there is one, otherwise whatever's decoded from disk. Used by every tool that
    /// needs to read before writing (Fill's match test, Copy, the eyedropper).</summary>
    private string GetEffectiveBlock(int worldX, int worldZ, LoadedChunkData world, EditOverlay overlay)
    {
        string? overridden = overlay.GetOverride(worldX, _config.SliceY, worldZ);
        if (overridden is not null) return overridden;

        int cx = FloorDiv(worldX, ChunkSize), cz = FloorDiv(worldZ, ChunkSize);
        int sectionY = FloorDiv(_config.SliceY, 16);
        var slice = world.ChunkStore.GetOrDecodeSlice(cx, cz, sectionY);
        if (slice is null) return "minecraft:air";
        return slice.GetBlock(worldX - cx * ChunkSize, _config.SliceY - sectionY * 16, worldZ - cz * ChunkSize);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var factor = e.Delta.Y > 0 ? WheelZoomFactor : 1 / WheelZoomFactor;
        SetZoom(_zoom * factor);
        UpdateHover(e.GetPosition(this));
        e.Handled = true;
    }

    /// <summary>Current screen pixels per block. Fired via <see cref="ZoomChanged"/> whenever it changes.</summary>
    public double Zoom => _zoom;

    public event Action<double>? ZoomChanged;

    public void ZoomIn() => SetZoom(_zoom * ButtonZoomFactor);

    public void ZoomOut() => SetZoom(_zoom / ButtonZoomFactor);

    private void SetZoom(double newZoom)
    {
        double minZoom = _config.Mode == LayerMode.Slice ? SliceModeMinZoom : MinZoom;
        var clamped = Math.Max(minZoom, Math.Min(MaxZoom, newZoom));
        if (clamped == _zoom) return;
        _zoom = clamped;
        ZoomChanged?.Invoke(_zoom);
        InvalidateVisual();
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
        var world = _world;
        if (world is null) return;

        var (blockX, blockZ) = ScreenToBlock(pos);
        int chunkX = FloorDiv(blockX, ChunkSize);
        int chunkZ = FloorDiv(blockZ, ChunkSize);
        int lx = ((blockX % ChunkSize) + ChunkSize) % ChunkSize;
        int lz = ((blockZ % ChunkSize) + ChunkSize) % ChunkSize;

        if (!world.Summaries.ContainsKey((chunkX, chunkZ)))
        {
            HoveredBlockChanged?.Invoke(null);
            return;
        }

        switch (ChunkRenderer.GetDataNeed(_config))
        {
            case ChunkDataNeed.Slice:
            {
                int sectionY = FloorDiv(_config.SliceY, 16);
                var slice = world.ChunkStore.GetOrDecodeSlice(chunkX, chunkZ, sectionY);
                if (slice is null) { HoveredBlockChanged?.Invoke(null); return; }
                string name = _editContext?.Overlay.GetOverride(blockX, _config.SliceY, blockZ)
                              ?? slice.GetBlock(lx, _config.SliceY - sectionY * 16, lz);
                HoveredBlockChanged?.Invoke(new HoveredBlock { X = blockX, Y = _config.SliceY, Z = blockZ, Name = name });
                break;
            }
            default:
            {
                var summary = world.Summaries[(chunkX, chunkZ)];
                int idx = lx * ChunkSize + lz;
                int y = summary.SurfaceY[idx];
                string name = world.BlockNames[summary.TopBlockIndex[idx]];
                HoveredBlockChanged?.Invoke(new HoveredBlock { X = blockX, Y = y, Z = blockZ, Name = name });
                break;
            }
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

        var world = _world;
        if (world is null) return;

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
                    RenderMacroTiles(context, world, startChunkX, startChunkZ, endChunkX, endChunkZ, pixelsPerBlock, w, h);
                }
                else
                {
                    RenderChunkTiles(context, world, startChunkX, startChunkZ, endChunkX, endChunkZ, pixelsPerBlock, pixelsPerChunk, w, h);
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

        // Copy-tool marquee, while dragging out a rectangle.
        if (_selectionStart is { } selStart && _selectionCurrent is { } selEnd)
        {
            int selMinX = Math.Min(selStart.X, selEnd.X), selMaxX = Math.Max(selStart.X, selEnd.X) + 1;
            int selMinZ = Math.Min(selStart.Z, selEnd.Z), selMaxZ = Math.Max(selStart.Z, selEnd.Z) + 1;
            double sx0 = (selMinX - _offsetX) * pixelsPerBlock + w / 2;
            double sz0 = (selMinZ - _offsetZ) * pixelsPerBlock + h / 2;
            double sx1 = (selMaxX - _offsetX) * pixelsPerBlock + w / 2;
            double sz1 = (selMaxZ - _offsetZ) * pixelsPerBlock + h / 2;
            context.DrawRectangle(SelectionPen, new Rect(sx0, sz0, sx1 - sx0, sz1 - sz0));
        }
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
        DrawingContext context, LoadedChunkData world,
        int startChunkX, int startChunkZ, int endChunkX, int endChunkZ,
        double pixelsPerBlock, double pixelsPerChunk, double w, double h)
    {
        for (int cz = startChunkZ; cz <= endChunkZ; cz++)
        {
            for (int cx = startChunkX; cx <= endChunkX; cx++)
            {
                if (!world.Summaries.ContainsKey((cx, cz))) continue;

                var tile = GetOrRequestTile(cx, cz, world);
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
        DrawingContext context, LoadedChunkData world,
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
                var tile = GetOrRequestMacroTile(mx, mz, world);
                if (tile is null) continue; // no loaded chunks here, or build still in flight — overview shows through

                double screenX = (mx * macroSizeBlocks - _offsetX) * pixelsPerBlock + w / 2;
                double screenZ = (mz * macroSizeBlocks - _offsetZ) * pixelsPerBlock + h / 2;
                var destRect = new Rect(screenX, screenZ, macroSizePixels + 0.75, macroSizePixels + 0.75);
                context.DrawImage(tile, new Rect(0, 0, macroSizeBlocks, macroSizeBlocks), destRect);
            }
        }
    }

    /// <summary>Returns the cached chunk tile, or kicks off a background build and returns null if one isn't ready yet.</summary>
    private WriteableBitmap? GetOrRequestTile(int cx, int cz, LoadedChunkData world)
    {
        var key = (cx, cz);
        WriteableBitmap? cached;
        lock (_cacheLock)
        {
            _tileCache.TryGetValue(key, out cached);
            // Not stale: nothing to do, whatever's cached (possibly null, on a first build) is current.
            if (cached is not null && !_staleChunkTiles.Contains(key)) return cached;
            // Already rebuilding (from a prior edit/config change) — keep showing the stale tile
            // meanwhile rather than starting a second concurrent build for the same key.
            if (!_pendingChunkTiles.Add(key)) return cached;
            _staleChunkTiles.Remove(key);
        }

        var config = _config;
        var worldRef = world;
        var overlay = _editContext?.Overlay;
        var showBelowLayer = _editContext?.IsEditModeOn ?? false;
        Task.Run(async () =>
        {
            await TileBuildConcurrency.WaitAsync().ConfigureAwait(false);
            try
            {
                // If anything here throws, pixels stays null and the tile is simply retried on a
                // later frame — the alternative (letting the exception propagate) would abandon
                // this Task before the Dispatcher.Post below runs, which would never clear the
                // pending flag and permanently stick this tile at overview-only resolution.
                byte[]? pixels = BuildChunkTilePixels(cx, cz, worldRef, config, overlay, showBelowLayer);

                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        bool stillCurrent = pixels is not null && ReferenceEquals(_world, worldRef) && _config == config;
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
            }
            finally
            {
                TileBuildConcurrency.Release();
            }
        });

        // Whatever was cached before this call (null on a first build, the still-good-enough stale
        // tile on a rebuild) keeps rendering until the background build above lands.
        return cached;
    }

    /// <summary>
    /// Builds one chunk's 16x16 BGRA8888 pixel buffer. For Surface/Heightmap/Biome, this is a pure
    /// in-memory lookup against the always-resident ChunkSummary — no I/O. For Slice mode, it decodes
    /// (or reuses a cached decode of) just the one section containing SliceY via
    /// world.ChunkStore.GetOrDecodeSlice. Either way, the ore overlay (if on) is applied per column
    /// from the chunk's OreSummary — another always-resident lookup, no decode of its own — rather
    /// than requiring a full ChunkData scan. Tile builds stay off the render thread even though these
    /// cases are all cheap enough to run inline, to keep this one code path uniform.
    /// </summary>
    private static byte[]? BuildChunkTilePixels(int cx, int cz, LoadedChunkData world, LayerConfig config, EditOverlay? overlay, bool showBelowLayer)
    {
        try
        {
            var pixels = new byte[ChunkSize * ChunkSize * 4];
            world.OreSummaries.TryGetValue((cx, cz), out var oreSummary);
            switch (ChunkRenderer.GetDataNeed(config))
            {
                case ChunkDataNeed.Slice:
                {
                    int sectionY = FloorDiv(config.SliceY, 16);
                    var slice = world.ChunkStore.GetOrDecodeSlice(cx, cz, sectionY);
                    if (slice is null) return null;

                    // Edit mode only: where the active layer is air, peek at the layer directly below
                    // (dimmed) instead of leaving it blank, so painting near a drop-off or over water
                    // doesn't happen blind. Only ever needs a second decode when SliceY sits at the
                    // bottom of its section — otherwise "below" is still inside the section already
                    // decoded above.
                    int localY = config.SliceY - sectionY * 16;
                    ChunkSliceData? belowSlice = null;
                    int belowLocalY = 0;
                    if (showBelowLayer)
                    {
                        if (localY > 0) { belowSlice = slice; belowLocalY = localY - 1; }
                        else { belowSlice = world.ChunkStore.GetOrDecodeSlice(cx, cz, sectionY - 1); belowLocalY = 15; }
                    }

                    for (int lz = 0; lz < ChunkSize; lz++)
                    for (int lx = 0; lx < ChunkSize; lx++)
                    {
                        string? ov = overlay?.GetOverride(cx * ChunkSize + lx, config.SliceY, cz * ChunkSize + lz);

                        Rgb color;
                        string currentBlock = ov ?? slice.GetBlock(lx, localY, lz);
                        if (belowSlice is not null && RenderColors.AirBlocks.Contains(currentBlock))
                        {
                            string? belowOv = overlay?.GetOverride(cx * ChunkSize + lx, config.SliceY - 1, cz * ChunkSize + lz);
                            string belowBlock = belowOv ?? belowSlice.GetBlock(lx, belowLocalY, lz);
                            color = ChunkRenderer.DimForBelowLayer(RenderColors.GetBlockColor(belowBlock));
                        }
                        else
                        {
                            color = ChunkRenderer.GetChunkPixelColor(slice, config, lx, lz, oreSummary, ov);
                        }

                        WritePixel(pixels, ChunkSize, lx, lz, color);
                    }
                    break;
                }
                default:
                {
                    if (!world.Summaries.TryGetValue((cx, cz), out var summary)) return null;
                    for (int lz = 0; lz < ChunkSize; lz++)
                    for (int lx = 0; lx < ChunkSize; lx++)
                        WritePixel(pixels, ChunkSize, lx, lz, ChunkRenderer.GetChunkPixelColor(summary, world.BlockNames, world.BiomeNames, config, lx, lz, oreSummary));
                    break;
                }
            }
            return pixels;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the cached macro tile (covering a MacroTileChunks x MacroTileChunks block of
    /// chunks at 1px/block — pixel-identical to drawing each chunk's own tile, just batched
    /// into one DrawImage call per MacroTileChunks^2 chunks instead of one per chunk), kicks
    /// off a background build if none is in flight, or returns null if the area has no loaded
    /// chunks at all.
    /// </summary>
    private WriteableBitmap? GetOrRequestMacroTile(int mx, int mz, LoadedChunkData world)
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

        // Avoid holding _cacheLock while scanning Summaries. Summaries covers every chunk the world
        // actually has, for both the summary and full-chunk-decode paths, so it's the right existence
        // check regardless of the current mode.
        bool anyChunkLoaded = false;
        for (int dz = 0; dz < MacroTileChunks && !anyChunkLoaded; dz++)
        {
            for (int dx = 0; dx < MacroTileChunks; dx++)
            {
                if (world.Summaries.ContainsKey((baseChunkX + dx, baseChunkZ + dz))) { anyChunkLoaded = true; break; }
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
        var worldRef = world;
        var overlay = _editContext?.Overlay;
        Task.Run(async () =>
        {
            await TileBuildConcurrency.WaitAsync().ConfigureAwait(false);
            try
            {
                // See GetOrRequestTile for why failures here must not propagate: an unhandled
                // exception would skip the Dispatcher.Post below entirely, and with it the only
                // code that clears the pending flag — permanently sticking this tile.
                byte[]? pixels;
                try
                {
                    var dataNeed = ChunkRenderer.GetDataNeed(config);
                    int sliceSectionY = dataNeed == ChunkDataNeed.Slice ? FloorDiv(config.SliceY, 16) : 0;
                    pixels = new byte[sizeBlocks * sizeBlocks * 4];
                    for (int dz = 0; dz < MacroTileChunks; dz++)
                    {
                        int cz = baseChunkZ + dz;
                        for (int dx = 0; dx < MacroTileChunks; dx++)
                        {
                            int cx = baseChunkX + dx;
                            if (!worldRef.Summaries.ContainsKey((cx, cz))) continue;

                            int destBaseX = dx * ChunkSize;
                            int destBaseZ = dz * ChunkSize;

                            // At macro-tile zoom, one chunk is only a handful of screen pixels — an
                            // individual ore block tinting its own column would be invisible anyway.
                            // So instead of the per-chunk-tile path's per-column OreSummary lookup,
                            // this finds the chunk's single shallowest matching ore (if any) once and
                            // tints the whole cell with it, which is both cheaper and actually legible
                            // at this zoom level. See ChunkRenderer.FindDominantOre.
                            Rgb? oreTint = GetOreTint(worldRef, cx, cz, config);

                            switch (dataNeed)
                            {
                                // Edit mode's "layer below through air" preview (see BuildChunkTilePixels)
                                // is deliberately not replicated here — SliceModeMinZoom already keeps
                                // editing zoomed in far enough that this macro path essentially never
                                // renders while painting, so the extra per-cell decode isn't worth it.
                                case ChunkDataNeed.Slice:
                                {
                                    var slice = worldRef.ChunkStore.GetOrDecodeSlice(cx, cz, sliceSectionY);
                                    if (slice is null) continue;
                                    for (int lz = 0; lz < ChunkSize; lz++)
                                    for (int lx = 0; lx < ChunkSize; lx++)
                                    {
                                        string? ov = overlay?.GetOverride(cx * ChunkSize + lx, config.SliceY, cz * ChunkSize + lz);
                                        var color = ChunkRenderer.GetChunkPixelColor(slice, config, lx, lz, blockNameOverride: ov);
                                        if (oreTint is not null) color = ChunkRenderer.BlendOre(color, oreTint.Value);
                                        WritePixel(pixels, sizeBlocks, destBaseX + lx, destBaseZ + lz, color);
                                    }
                                    break;
                                }
                                default:
                                {
                                    var summary = worldRef.Summaries[(cx, cz)];
                                    for (int lz = 0; lz < ChunkSize; lz++)
                                    for (int lx = 0; lx < ChunkSize; lx++)
                                    {
                                        var color = ChunkRenderer.GetChunkPixelColor(summary, worldRef.BlockNames, worldRef.BiomeNames, config, lx, lz);
                                        if (oreTint is not null) color = ChunkRenderer.BlendOre(color, oreTint.Value);
                                        WritePixel(pixels, sizeBlocks, destBaseX + lx, destBaseZ + lz, color);
                                    }
                                    break;
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
                        bool stillCurrent = pixels is not null && ReferenceEquals(_world, worldRef) && _config == config;
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
            }
            finally
            {
                TileBuildConcurrency.Release();
            }
        });

        return null;
    }

    /// <summary>
    /// Kicks off a background rebuild of the whole-world, 1px/chunk backdrop bitmap. Cheap —
    /// one representative color sample per chunk rather than 256 — so it can run to
    /// completion on every chunks/config change without needing incremental updates. Always
    /// sourced from Summaries, even in Slice/ore-overlay modes: decoding every chunk in the
    /// world just for a distant backdrop would defeat the point of the summary layer, so those
    /// modes show a Surface-style approximation here (the on-screen detail tiles are still exact —
    /// see BuildChunkTilePixels/GetOrRequestMacroTile, which fetch the right granularity for those modes).
    /// </summary>
    private void RequestOverviewRebuild()
    {
        var world = _world;
        var config = _config;
        int generation = ++_overviewGeneration;

        if (world is null || world.Summaries.IsEmpty)
        {
            _overview?.Dispose();
            _overview = null;
            return;
        }

        Task.Run(() =>
        {
            int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
            foreach (var (cx, cz) in world.Summaries.Keys)
            {
                if (cx < minX) minX = cx;
                if (cx > maxX) maxX = cx;
                if (cz < minZ) minZ = cz;
                if (cz > maxZ) maxZ = cz;
            }

            int width = maxX - minX + 1;
            int height = maxZ - minZ + 1;
            var pixels = new byte[width * height * 4];

            foreach (var kv in world.Summaries)
            {
                var (cx, cz) = kv.Key;
                var color = ChunkRenderer.GetChunkPixelColor(kv.Value, world.BlockNames, world.BiomeNames, config, ChunkSize / 2, ChunkSize / 2);
                // Same coarse per-chunk ore tint as macro tiles (see GetOrRequestMacroTile) — at
                // 1px/chunk a per-column tint would be meaningless anyway, so this turns the overview
                // into a world-wide "where's the ore" backdrop for free.
                var oreTint = GetOreTint(world, cx, cz, config);
                if (oreTint is not null) color = ChunkRenderer.BlendOre(color, oreTint.Value);
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

    /// <summary>The chunk's dominant ore color for coarse (whole-cell) tinting — see
    /// ChunkRenderer.FindDominantOre — or null if ore overlay is off, the chunk has no OreSummary yet,
    /// or it has no ore matching the current filter.</summary>
    private static Rgb? GetOreTint(LoadedChunkData world, int cx, int cz, LayerConfig config) =>
        world.OreSummaries.TryGetValue((cx, cz), out var oreSummary) ? ChunkRenderer.FindDominantOre(oreSummary, config) : null;

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

        public void Remove((int, int) key)
        {
            if (_map.Remove(key, out var node))
            {
                _order.Remove(node);
                node.Value.Bitmap.Dispose();
            }
        }

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
