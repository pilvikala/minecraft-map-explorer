using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using MapExplorer.App.ViewModels;
using MapExplorer.App.Views;
using MapExplorer.Core.World;
using MapExplorer.Rendering;

namespace MapExplorer.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private CancellationTokenSource? _loadCts;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        MapCanvas.Config = _viewModel.Map.BuildLayerConfig();
        _viewModel.Map.RenderConfigChanged += () => MapCanvas.Config = _viewModel.Map.BuildLayerConfig();
        MapCanvas.EditContext = _viewModel.Edit;
        _viewModel.Edit.Saved += ResummarizeDirtyChunks;
        MapCanvas.HoveredBlockChanged += OnHoveredBlockChanged;
        MapCanvas.ZoomChanged += OnZoomChanged;
        _viewModel.RegionDirRequested += async regionDir =>
        {
            // A dimension switch mid-load supersedes whatever's in flight — cancel it rather than
            // let it keep burning CPU/IO for a dimension the user already navigated away from.
            _loadCts?.Cancel();
            var cts = new CancellationTokenSource();
            _loadCts = cts;
            await LoadWorld(regionDir, cts.Token);
        };
        _viewModel.ViewRescaleRequested += factor => MapCanvas.RescaleView(factor);
        UpdateZoomLevelText(MapCanvas.Zoom);

        if (Environment.GetEnvironmentVariable("MAPEXPLORER_AUTOLOAD") == "1")
        {
            // Exercises the real picker -> selection -> load flow (not a
            // hardcoded-path bypass): waits for the picker's real world scan,
            // then picks the first discovered world, same as a card click would.
            Opened += async (_, _) =>
            {
                await _viewModel.WorldPicker.ScanAsync();
                if (_viewModel.WorldPicker.Worlds.Count > 0)
                {
                    var first = _viewModel.WorldPicker.Worlds[0];
                    _viewModel.WorldPicker.ChooseWorld(first.ToSelectedWorld());
                }
            };
        }
    }

    private void OnBackClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        MapCanvas.World = null;
        _viewModel.BackToPicker();
    }

    private void OnDimensionClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Avalonia.Controls.Button { Tag: Dimension dimension })
        {
            _viewModel.SwitchDimension(dimension);
        }
    }

    private void OnPlayersClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _viewModel.TogglePlayersPanel();

    private void OnEditClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _viewModel.ToggleEditMode();

    private void OnHoveredBlockChanged(HoveredBlock? block)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _viewModel.Map.HoverText = block is null ? "" : $"X:{block.X} Y:{block.Y} Z:{block.Z}\n{block.Name}";
        });
    }

    private void OnZoomChanged(double zoom)
    {
        Dispatcher.UIThread.Post(() => UpdateZoomLevelText(zoom));
    }

    private void UpdateZoomLevelText(double zoom)
    {
        ZoomLevelText.Text = $"{zoom * 100:0.#}%";
    }

    private void OnZoomInClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => MapCanvas.ZoomIn();

    private void OnZoomOutClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => MapCanvas.ZoomOut();

    private async Task LoadWorld(string regionDir, CancellationToken cancellationToken)
    {
        var map = _viewModel.Map;
        map.StatusText = "Loading…";
        MapCanvas.IsLoading = true;

        // Full ChunkData is only ever needed transiently, per chunk, to derive the summaries that
        // actually get kept — see WorldLoader.Load<T>, ChunkSummaryBuilder and OreSummaryBuilder.
        // blockNames/biomeNames intern block/biome name strings across the whole load so summaries
        // don't each hold their own copies. chunkStore decodes on demand (LRU-bounded), at whichever
        // granularity the active view mode needs — see ChunkRenderer.GetDataNeed.
        var blockNames = new NamePalette();
        var biomeNames = new NamePalette();
        var chunkStore = new WorldChunkStore(regionDir);
        _viewModel.Edit.SetRegionDir(regionDir, chunkStore);

        // WorldLoader.Load only hands back one summary per chunk (the return value below), so the
        // ore summary is captured as a side effect of the same summarize callback instead of a
        // second pass — it's built from the same transient ChunkData the ChunkSummary comes from.
        var oreSummaries = new ConcurrentDictionary<(int, int), OreSummary>();

        // The loader hands back the same (still-filling) ConcurrentDictionary on every
        // progress tick, so the canvas can bind to it once, up front, and watch it grow —
        // the map paints in progressively as regions decode instead of staying black with
        // just a "N/M regions" counter until the whole load finishes.
        bool boundChunks = false;
        var refreshThrottle = Stopwatch.StartNew();

        var progress = new Progress<LoadProgress<ChunkSummary>>(p =>
        {
            // Iterations already in flight when cancellation was requested can still report one
            // more tick after the fact — drop it so it can't clobber MapCanvas.World once the next
            // dimension's load has already bound its own data there.
            if (cancellationToken.IsCancellationRequested) return;

            map.LoadedRegions = p.LoadedRegions;
            map.TotalRegions = p.TotalRegions;
            map.StatusText = $"Loading regions… {p.LoadedRegions}/{p.TotalRegions}";

            if (!boundChunks)
            {
                boundChunks = true;
                MapCanvas.World = new LoadedChunkData
                {
                    Summaries = p.Chunks,
                    OreSummaries = oreSummaries,
                    ChunkStore = chunkStore,
                    BlockNames = blockNames,
                    BiomeNames = biomeNames
                };
            }
            else if (refreshThrottle.ElapsedMilliseconds >= 300)
            {
                refreshThrottle.Restart();
                MapCanvas.NotifyChunksGrew();
            }
        });

        var sw = Stopwatch.StartNew();
        LoadResult<ChunkSummary> result;
        try
        {
            result = await Task.Run(() => WorldLoader.Load(
                regionDir,
                chunk =>
                {
                    oreSummaries[(chunk.ChunkX, chunk.ChunkZ)] = OreSummaryBuilder.Build(chunk);
                    return ChunkSummaryBuilder.Build(chunk, blockNames, biomeNames);
                },
                progress: progress,
                cancellationToken: cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a later dimension switch, which owns MapCanvas/status now — leave
            // both alone rather than let this stale load's tail overwrite them.
            return;
        }
        sw.Stop();

        // Flips MapCanvas over to building/caching full-detail tiles — safe now that the
        // chunk set is final — and discards anything a straddling-region race might have
        // cached with incomplete data while it was still growing.
        MapCanvas.IsLoading = false;

        // Normally already bound and just needs a final refresh; falls back to a plain bind
        // for the edge case where the region dir had nothing to report progress for.
        if (boundChunks)
        {
            MapCanvas.NotifyChunksGrew();
        }
        else
        {
            MapCanvas.World = new LoadedChunkData
            {
                Summaries = result.Chunks,
                OreSummaries = oreSummaries,
                ChunkStore = chunkStore,
                BlockNames = blockNames,
                BiomeNames = biomeNames
            };
        }

        map.StatusText = $"Loaded {result.RegionCount} regions, {result.Chunks.Count} chunks in {sw.ElapsedMilliseconds}ms " +
                          $"(threads: {Environment.ProcessorCount})";
    }

    /// <summary>After a successful edit-mode Save, refreshes the always-resident summary/ore data for
    /// just the chunks that changed, so Surface/Heightmap/ore-overlay views reflect the edit without
    /// requiring a full world reload. Slice-mode rendering doesn't need this at all — it already
    /// renders straight from the edit overlay regardless of save state (see MapCanvasControl).</summary>
    private void ResummarizeDirtyChunks()
    {
        var world = MapCanvas.World;
        if (world is null) return;

        foreach (var (cx, cz) in _viewModel.Edit.Overlay.DirtyChunks)
        {
            world.ChunkStore.InvalidateChunk(cx, cz);
            var chunkData = world.ChunkStore.GetOrDecode(cx, cz);
            if (chunkData is null) continue;

            world.Summaries[(cx, cz)] = ChunkSummaryBuilder.Build(chunkData, world.BlockNames, world.BiomeNames);
            world.OreSummaries[(cx, cz)] = OreSummaryBuilder.Build(chunkData);
        }

        MapCanvas.NotifyChunksGrew();
    }
}
