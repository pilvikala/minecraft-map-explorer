using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using MapExplorer.App.ViewModels;
using MapExplorer.App.Views;
using MapExplorer.Core.World;

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
        _viewModel.WorldSelected += async (regionDir, _) => await LoadWorld(regionDir);

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
                    _viewModel.WorldPicker.ChooseWorld(first.RegionDir, first.Name);
                }
            };
        }
    }

    private void OnBackClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        MapCanvas.Chunks = null;
        _viewModel.BackToPicker();
    }

    private void OnHoveredBlockChanged(HoveredBlock? block)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _viewModel.Map.HoverText = block is null ? "" : $"X:{block.X} Y:{block.Y} Z:{block.Z}\n{block.Name}";
        });
    }

    private async Task LoadWorld(string regionDir)
    {
        var map = _viewModel.Map;
        map.StatusText = "Loading…";
        MapCanvas.IsLoading = true;

        // The loader hands back the same (still-filling) ConcurrentDictionary on every
        // progress tick, so the canvas can bind to it once, up front, and watch it grow —
        // the map paints in progressively as regions decode instead of staying black with
        // just a "N/M regions" counter until the whole load finishes.
        bool boundChunks = false;
        var refreshThrottle = Stopwatch.StartNew();

        var progress = new Progress<LoadProgress>(p =>
        {
            map.LoadedRegions = p.LoadedRegions;
            map.TotalRegions = p.TotalRegions;
            map.StatusText = $"Loading regions… {p.LoadedRegions}/{p.TotalRegions}";

            if (!boundChunks)
            {
                boundChunks = true;
                MapCanvas.Chunks = p.Chunks;
            }
            else if (refreshThrottle.ElapsedMilliseconds >= 300)
            {
                refreshThrottle.Restart();
                MapCanvas.NotifyChunksGrew();
            }
        });

        var sw = Stopwatch.StartNew();
        var result = await Task.Run(() => WorldLoader.Load(regionDir, progress: progress));
        sw.Stop();

        // Flips MapCanvas over to building/caching full-detail tiles — safe now that the
        // chunk set is final — and discards anything a straddling-region race might have
        // cached with incomplete data while it was still growing.
        MapCanvas.IsLoading = false;

        // Normally already bound and just needs a final refresh; falls back to a plain bind
        // for the edge case where the region dir had nothing to report progress for.
        if (boundChunks) MapCanvas.NotifyChunksGrew();
        else MapCanvas.Chunks = result.Chunks;

        map.StatusText = $"Loaded {result.RegionCount} regions, {result.Chunks.Count} chunks in {sw.ElapsedMilliseconds}ms " +
                          $"(threads: {Environment.ProcessorCount})";
    }
}
