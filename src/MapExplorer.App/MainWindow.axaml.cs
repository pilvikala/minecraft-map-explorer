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
    // Hardcoded for this milestone (world picker UI is Milestone 7) — same
    // real-world save used throughout the Electron optimization + prototype work.
    private const string RegionDir =
        "/home/michal/snap/mc-installer/current/.minecraft/saves/New World/dimensions/minecraft/overworld/region";

    private readonly MapViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        MapCanvas.Config = _viewModel.BuildLayerConfig();
        _viewModel.RenderConfigChanged += () => MapCanvas.Config = _viewModel.BuildLayerConfig();
        MapCanvas.HoveredBlockChanged += OnHoveredBlockChanged;

        if (Environment.GetEnvironmentVariable("MAPEXPLORER_AUTOLOAD") == "1")
        {
            Opened += async (_, _) => await LoadWorld();
        }
    }

    private void OnHoveredBlockChanged(HoveredBlock? block)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _viewModel.HoverText = block is null ? "" : $"X:{block.X} Y:{block.Y} Z:{block.Z}\n{block.Name}";
        });
    }

    private async void OnLoadClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => await LoadWorld();

    private async Task LoadWorld()
    {
        LoadButton.IsEnabled = false;
        _viewModel.StatusText = "Loading…";

        var sw = Stopwatch.StartNew();
        var result = await Task.Run(() => WorldLoader.Load(RegionDir));
        sw.Stop();

        MapCanvas.Chunks = result.Chunks;
        MapCanvas.NotifyChunksChanged();

        _viewModel.StatusText = $"Loaded {result.RegionCount} regions, {result.Chunks.Count} chunks in {sw.ElapsedMilliseconds}ms " +
                                 $"(threads: {Environment.ProcessorCount})";
        LoadButton.IsEnabled = true;
    }
}
