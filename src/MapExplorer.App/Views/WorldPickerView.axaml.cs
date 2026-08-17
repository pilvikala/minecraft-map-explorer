using Avalonia.Controls;
using Avalonia.Platform.Storage;
using MapExplorer.App.ViewModels;
using MapExplorer.Core.World;

namespace MapExplorer.App.Views;

public partial class WorldPickerView : UserControl
{
    public WorldPickerView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is WorldPickerViewModel vm) _ = vm.ScanAsync();
        };
    }

    private async void OnBrowseClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not WorldPickerViewModel vm) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Minecraft world or region folder",
            AllowMultiple = false
        });
        if (folders.Count == 0) return;

        var dir = folders[0].Path.LocalPath;

        // Ported from WorldLoader.tsx's browseForFolder: accept either a world
        // root (containing a region/ subfolder) or a region folder directly.
        var subRegionDir = Path.Combine(dir, "region");
        var isWorldRoot = Directory.Exists(subRegionDir) && Directory.EnumerateFiles(subRegionDir, "*.mca").Any();
        var regionDir = isWorldRoot ? subRegionDir : dir;

        // Only a world root (not a bare region folder picked directly) has DIM-1/DIM1
        // siblings to probe for Nether/End data.
        var netherDir = isWorldRoot ? WorldDiscovery.FindNetherRegionDir(dir) : null;
        var endDir = isWorldRoot ? WorldDiscovery.FindEndRegionDir(dir) : null;

        // A bare region folder's parent is always the world root (region dirs only ever
        // exist as <worldRoot>/region), so this resolves the world root either way.
        var worldPath = isWorldRoot ? dir : (Path.GetDirectoryName(dir) ?? dir);

        var displayName = new DirectoryInfo(dir).Name;
        vm.ChooseWorld(new SelectedWorld(displayName, worldPath, regionDir, netherDir, endDir));
    }
}
