using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MapExplorer.Core.World;

namespace MapExplorer.App.ViewModels;

// Ported from WorldLoader.tsx's WorldPicker component + find-worlds.ts-backed
// discovery. The "browse for folder" native dialog needs a TopLevel/Window
// reference that a ViewModel shouldn't hold, so that part is handled by
// WorldPickerView's code-behind, which then calls ChooseWorld directly.
public partial class WorldPickerViewModel : ObservableObject
{
    public ObservableCollection<WorldCardViewModel> Worlds { get; } = [];

    [ObservableProperty]
    private bool _isScanning = true;

    public bool HasNoWorlds => !IsScanning && Worlds.Count == 0;

    /// <summary>Fired when a world is chosen, either from the grid or the browse dialog.</summary>
    public event Action<SelectedWorld>? WorldChosen;

    public async Task ScanAsync()
    {
        IsScanning = true;
        OnPropertyChanged(nameof(HasNoWorlds));

        var worlds = await Task.Run(WorldDiscovery.DiscoverWorlds);

        Worlds.Clear();
        foreach (var w in worlds) Worlds.Add(new WorldCardViewModel(w));

        IsScanning = false;
        OnPropertyChanged(nameof(HasNoWorlds));
    }

    [RelayCommand]
    private void SelectWorld(WorldCardViewModel world) => WorldChosen?.Invoke(world.ToSelectedWorld());

    [RelayCommand]
    private async Task Refresh() => await ScanAsync();

    public void ChooseWorld(SelectedWorld world) => WorldChosen?.Invoke(world);
}
