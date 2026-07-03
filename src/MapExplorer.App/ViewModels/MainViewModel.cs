using CommunityToolkit.Mvvm.ComponentModel;

namespace MapExplorer.App.ViewModels;

public enum AppPhase
{
    Picker,
    Map
}

// Ported from App.tsx's `phase: 'pick' | 'map'` state.
public partial class MainViewModel : ObservableObject
{
    public WorldPickerViewModel WorldPicker { get; } = new();
    public MapViewModel Map { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPicker))]
    [NotifyPropertyChangedFor(nameof(ShowMap))]
    private AppPhase _phase = AppPhase.Picker;

    public bool ShowPicker => Phase == AppPhase.Picker;
    public bool ShowMap => Phase == AppPhase.Map;

    /// <summary>Raised when the user picks a world; args are (regionDir, displayName).</summary>
    public event Action<string, string>? WorldSelected;

    public MainViewModel()
    {
        WorldPicker.WorldChosen += (regionDir, name) =>
        {
            Phase = AppPhase.Map;
            WorldSelected?.Invoke(regionDir, name);
        };
    }

    public void BackToPicker()
    {
        Phase = AppPhase.Picker;
        _ = WorldPicker.ScanAsync();
    }
}
