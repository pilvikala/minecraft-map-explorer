using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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
// per-chunk tile cache keyed by layer config. Deliberately drops the TS
// version's "macro tile" downsampling system (built to bound Canvas2D
// drawImage call counts) — see the rewrite plan: Avalonia's Skia compositor
// blits+scales per DrawImage call in hardware, so the same mitigation may not
// be needed here. If the Milestone 4 stress test shows otherwise, that's
// where a fallback gets added, not preemptively here.
public sealed class MapCanvasControl : Control
{
    private const int ChunkSize = 16;

    // Viewport lives as plain mutable control-local state, not an observable
    // property — it changes on every pan/zoom frame, and routing that through
    // INotifyPropertyChanged would be pure overhead on the hottest path in the
    // app (see rewrite plan §4).
    private double _offsetX;
    private double _offsetZ;
    private double _zoom = 2.0;

    private ConcurrentDictionary<(int, int), ChunkData>? _chunks;
    private LayerConfig _config = new();
    private readonly Dictionary<(int cx, int cz), WriteableBitmap> _tileCache = new();

    private bool _dragging;
    private Point _lastPointerPos;

    public event Action<HoveredBlock?>? HoveredBlockChanged;

    public ConcurrentDictionary<(int, int), ChunkData>? Chunks
    {
        get => _chunks;
        set
        {
            _chunks = value;
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
            _tileCache.Clear(); // config affects every tile's pixels — cheap to just rebuild lazily on next paint
            InvalidateVisual();
        }
    }

    /// <summary>Call after committing newly-decoded chunks; does not clear the tile cache (new chunks don't invalidate old ones).</summary>
    public void NotifyChunksChanged() => InvalidateVisual();

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
        }

        UpdateHover(pos);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _dragging = false;
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

        var chunks = _chunks;
        if (chunks is null) return;

        double pixelsPerBlock = _zoom;
        double pixelsPerChunk = ChunkSize * pixelsPerBlock;

        double startBlockX = _offsetX - w / 2 / pixelsPerBlock;
        double startBlockZ = _offsetZ - h / 2 / pixelsPerBlock;
        double endBlockX = _offsetX + w / 2 / pixelsPerBlock;
        double endBlockZ = _offsetZ + h / 2 / pixelsPerBlock;

        int startChunkX = FloorDiv((int)Math.Floor(startBlockX), ChunkSize);
        int startChunkZ = FloorDiv((int)Math.Floor(startBlockZ), ChunkSize);
        int endChunkX = (int)Math.Ceiling(endBlockX / ChunkSize);
        int endChunkZ = (int)Math.Ceiling(endBlockZ / ChunkSize);

        var interp = pixelsPerBlock >= 1 ? BitmapInterpolationMode.None : BitmapInterpolationMode.LowQuality;

        for (int cz = startChunkZ; cz <= endChunkZ; cz++)
        {
            for (int cx = startChunkX; cx <= endChunkX; cx++)
            {
                if (!chunks.TryGetValue((cx, cz), out var chunk)) continue;

                var tile = GetOrCreateTile(cx, cz, chunk);
                double screenX = (cx * ChunkSize - _offsetX) * pixelsPerBlock + w / 2;
                double screenZ = (cz * ChunkSize - _offsetZ) * pixelsPerBlock + h / 2;
                // Slightly overshoot each tile's nominal size so neighboring tiles
                // overlap by a fraction of a pixel. Without this, faint seams
                // appear at every chunk boundary from edge sampling in the
                // rasterizer (each tile is a separate DrawImage call) — a known
                // category of artifact in tile-based 2D renderers generally.
                var destRect = new Rect(screenX, screenZ, pixelsPerChunk + 0.75, pixelsPerChunk + 0.75);

                using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = interp }))
                {
                    context.DrawImage(tile, new Rect(0, 0, ChunkSize, ChunkSize), destRect);
                }
            }
        }

        // Grid lines at high zoom
        if (_zoom >= 8)
        {
            var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)));
            for (int cx = startChunkX; cx <= endChunkX; cx++)
            {
                double sx = (cx * ChunkSize - _offsetX) * pixelsPerBlock + w / 2;
                context.DrawLine(gridPen, new Point(sx, 0), new Point(sx, h));
            }
            for (int cz = startChunkZ; cz <= endChunkZ; cz++)
            {
                double sz = (cz * ChunkSize - _offsetZ) * pixelsPerBlock + h / 2;
                context.DrawLine(gridPen, new Point(0, sz), new Point(w, sz));
            }
        }

        // Crosshair at world origin
        var crossPen = new Pen(new SolidColorBrush(Color.FromArgb(153, 255, 60, 60)));
        double ox = (0 - _offsetX) * pixelsPerBlock + w / 2;
        double oz = (0 - _offsetZ) * pixelsPerBlock + h / 2;
        context.DrawLine(crossPen, new Point(ox - 8, oz), new Point(ox + 8, oz));
        context.DrawLine(crossPen, new Point(ox, oz - 8), new Point(ox, oz + 8));
    }

    private WriteableBitmap GetOrCreateTile(int cx, int cz, ChunkData chunk)
    {
        if (_tileCache.TryGetValue((cx, cz), out var cached)) return cached;

        var bmp = new WriteableBitmap(new PixelSize(ChunkSize, ChunkSize), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = bmp.Lock())
        {
            unsafe
            {
                var ptr = (byte*)fb.Address;
                int stride = fb.RowBytes;
                for (int lz = 0; lz < ChunkSize; lz++)
                {
                    byte* row = ptr + lz * stride;
                    for (int lx = 0; lx < ChunkSize; lx++)
                    {
                        var color = ChunkRenderer.GetChunkPixelColor(chunk, _config, lx, lz);
                        byte* px = row + lx * 4;
                        px[0] = color.B;
                        px[1] = color.G;
                        px[2] = color.R;
                        px[3] = 255;
                    }
                }
            }
        }

        _tileCache[(cx, cz)] = bmp;
        return bmp;
    }
}
