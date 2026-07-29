using System;
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

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        MapCanvas.Config = _viewModel.Map.BuildLayerConfig();
        _viewModel.Map.RenderConfigChanged += () => MapCanvas.Config = _viewModel.Map.BuildLayerConfig();
        MapCanvas.HoveredBlockChanged += OnHoveredBlockChanged;
        MapCanvas.ZoomChanged += OnZoomChanged;
        _viewModel.RegionDirRequested += async regionDir => await LoadWorld(regionDir);
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

    private async Task LoadWorld(string regionDir)
    {
        var map = _viewModel.Map;
        map.StatusText = "Loading…";
        MapCanvas.IsLoading = true;

        // Full ChunkData is only ever needed transiently, per chunk, to derive the summary that
        // actually gets kept — see WorldLoader.Load<T> and ChunkSummaryBuilder. blockNames/biomeNames
        // intern block/biome name strings across the whole load so summaries don't each hold their
        // own copies. chunkStore decodes on demand (LRU-bounded), at whichever granularity the active
        // view mode needs — see ChunkRenderer.GetDataNeed.
        var blockNames = new NamePalette();
        var biomeNames = new NamePalette();
        var chunkStore = new WorldChunkStore(regionDir);

        // The loader hands back the same (still-filling) ConcurrentDictionary on every
        // progress tick, so the canvas can bind to it once, up front, and watch it grow —
        // the map paints in progressively as regions decode instead of staying black with
        // just a "N/M regions" counter until the whole load finishes.
        bool boundChunks = false;
        var refreshThrottle = Stopwatch.StartNew();

        var progress = new Progress<LoadProgress<ChunkSummary>>(p =>
        {
            map.LoadedRegions = p.LoadedRegions;
            map.TotalRegions = p.TotalRegions;
            map.StatusText = $"Loading regions… {p.LoadedRegions}/{p.TotalRegions}";

            if (!boundChunks)
            {
                boundChunks = true;
                MapCanvas.World = new LoadedChunkData
                {
                    Summaries = p.Chunks,
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
        var result = await Task.Run(() => WorldLoader.Load(
            regionDir,
            chunk => ChunkSummaryBuilder.Build(chunk, blockNames, biomeNames),
            progress: progress));
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
                ChunkStore = chunkStore,
                BlockNames = blockNames,
                BiomeNames = biomeNames
            };
        }

        map.StatusText = $"Loaded {result.RegionCount} regions, {result.Chunks.Count} chunks in {sw.ElapsedMilliseconds}ms " +
                          $"(threads: {Environment.ProcessorCount})";
    }
}
