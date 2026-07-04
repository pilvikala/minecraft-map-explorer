using CommunityToolkit.Mvvm.ComponentModel;
using MapExplorer.Rendering;

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

    private SelectedWorld? _currentWorld;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPicker))]
    [NotifyPropertyChangedFor(nameof(ShowMap))]
    private AppPhase _phase = AppPhase.Picker;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverworldActive))]
    [NotifyPropertyChangedFor(nameof(IsNetherActive))]
    [NotifyPropertyChangedFor(nameof(IsEndActive))]
    private Dimension _currentDimension = Dimension.Overworld;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NetherTooltip))]
    private bool _isNetherAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EndTooltip))]
    private bool _isEndAvailable;

    public bool ShowPicker => Phase == AppPhase.Picker;
    public bool ShowMap => Phase == AppPhase.Map;

    public bool IsOverworldActive => CurrentDimension == Dimension.Overworld;
    public bool IsNetherActive => CurrentDimension == Dimension.Nether;
    public bool IsEndActive => CurrentDimension == Dimension.End;

    public string? NetherTooltip => IsNetherAvailable ? null : "Not generated in this world";
    public string? EndTooltip => IsEndAvailable ? null : "Not generated in this world";

    /// <summary>Raised whenever the map should (re)load a region dir — the initial world pick, or a later dimension switch.</summary>
    public event Action<string>? RegionDirRequested;

    /// <summary>Raised on an Overworld/Nether switch to recenter the camera on the linked coordinate (XZ * or / 8).</summary>
    public event Action<double>? ViewRescaleRequested;

    public MainViewModel()
    {
        WorldPicker.WorldChosen += world =>
        {
            _currentWorld = world;
            IsNetherAvailable = world.NetherDir is not null;
            IsEndAvailable = world.EndDir is not null;
            CurrentDimension = Dimension.Overworld;
            Map.SetDimension(Dimension.Overworld);
            Phase = AppPhase.Map;
            RegionDirRequested?.Invoke(world.OverworldDir);
        };
    }

    public void SwitchDimension(Dimension dimension)
    {
        var regionDir = _currentWorld?.RegionDirFor(dimension);
        if (regionDir is null) return;

        var previousDimension = CurrentDimension;
        CurrentDimension = dimension;
        Map.SetDimension(dimension);
        if (dimension == Dimension.Nether)
        {
            // Surface/Heightmap just render the solid bedrock roof in the Nether — a
            // mid-height slice is the useful default view there.
            Map.Mode = LayerMode.Slice;
            Map.SliceY = 32;
        }

        // Nether portals link Overworld<->Nether coordinates at an 8:1 XZ ratio (this is
        // how the game itself finds/creates the matching portal) — recenter the camera on
        // the corresponding spot. The End has no such coordinate relationship to the other
        // two, so no rescale happens for any switch involving it.
        var rescaleFactor = (previousDimension, dimension) switch
        {
            (Dimension.Overworld, Dimension.Nether) => 1.0 / 8,
            (Dimension.Nether, Dimension.Overworld) => 8.0,
            _ => (double?)null
        };
        if (rescaleFactor is not null) ViewRescaleRequested?.Invoke(rescaleFactor.Value);

        RegionDirRequested?.Invoke(regionDir);
    }

    public void BackToPicker()
    {
        _currentWorld = null;
        CurrentDimension = Dimension.Overworld;
        IsNetherAvailable = false;
        IsEndAvailable = false;
        Phase = AppPhase.Picker;
        _ = WorldPicker.ScanAsync();
    }
}
