using Avalonia.Controls;
using Avalonia.Platform.Storage;
using MapExplorer.App.ViewModels;

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
        var regionDir = Directory.Exists(subRegionDir) && Directory.EnumerateFiles(subRegionDir, "*.mca").Any()
            ? subRegionDir
            : dir;

        var displayName = new DirectoryInfo(dir).Name;
        vm.ChooseWorld(regionDir, displayName);
    }
}
