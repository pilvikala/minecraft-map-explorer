using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MapExplorer.Rendering;

namespace MapExplorer.App.ViewModels;

// Ported from the Electron app's src/renderer/src/store/index.ts's
// layerConfig slice + LayerPanel.tsx's ORE_LABELS. Deliberately does NOT
// include the chunk dictionary or viewport (see the rewrite plan §4/§5):
// chunks are a plain shared ConcurrentDictionary read directly by
// MapCanvasControl, and viewport is control-local mutable state — neither
// needs to round-trip through MVVM property-changed notifications.
public partial class MapViewModel : ObservableObject
{
    private static readonly (string BlockName, string Label)[] OreLabels =
    [
        ("minecraft:diamond_ore", "Diamond"),
        ("minecraft:deepslate_diamond_ore", "Diamond (deep)"),
        ("minecraft:emerald_ore", "Emerald"),
        ("minecraft:deepslate_emerald_ore", "Emerald (deep)"),
        ("minecraft:gold_ore", "Gold"),
        ("minecraft:deepslate_gold_ore", "Gold (deep)"),
        ("minecraft:iron_ore", "Iron"),
        ("minecraft:deepslate_iron_ore", "Iron (deep)"),
        ("minecraft:copper_ore", "Copper"),
        ("minecraft:deepslate_copper_ore", "Copper (deep)"),
        ("minecraft:lapis_ore", "Lapis"),
        ("minecraft:deepslate_lapis_ore", "Lapis (deep)"),
        ("minecraft:redstone_ore", "Redstone"),
        ("minecraft:deepslate_redstone_ore", "Redstone (deep)"),
        ("minecraft:coal_ore", "Coal"),
        ("minecraft:deepslate_coal_ore", "Coal (deep)"),
        ("minecraft:ancient_debris", "Ancient Debris"),
        ("minecraft:nether_quartz_ore", "Quartz"),
        ("minecraft:nether_gold_ore", "Nether Gold"),
    ];

    public ObservableCollection<OreOption> Ores { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSliceMode))]
    [NotifyPropertyChangedFor(nameof(IsSurfaceModeActive))]
    [NotifyPropertyChangedFor(nameof(IsSliceModeActive))]
    [NotifyPropertyChangedFor(nameof(IsHeightmapModeActive))]
    [NotifyPropertyChangedFor(nameof(IsBiomeModeActive))]
    private LayerMode _mode = LayerMode.Surface;

    public bool IsSurfaceModeActive => Mode == LayerMode.Surface;
    public bool IsSliceModeActive => Mode == LayerMode.Slice;
    public bool IsHeightmapModeActive => Mode == LayerMode.Heightmap;
    public bool IsBiomeModeActive => Mode == LayerMode.Biome;

    [ObservableProperty]
    private int _sliceY = 64;

    [ObservableProperty]
    private int _sliceYMin = -64;

    [ObservableProperty]
    private int _sliceYMax = 319;

    [ObservableProperty]
    private bool _oreOverlay;

    [ObservableProperty]
    private string _statusText = "Idle";

    [ObservableProperty]
    private string _hoverText = "";

    [ObservableProperty]
    private int _loadedRegions;

    [ObservableProperty]
    private int _totalRegions;

    public bool IsSliceMode => Mode == LayerMode.Slice;

    /// <summary>Raised whenever anything that affects rendered pixels changes — MainWindow pushes a fresh LayerConfig into MapCanvasControl on this.</summary>
    public event Action? RenderConfigChanged;

    public MapViewModel()
    {
        Ores = new ObservableCollection<OreOption>(OreLabels.Select(o => new OreOption
        {
            BlockName = o.BlockName,
            Label = o.Label,
            Color = Colors.OreBlocks[o.BlockName]
        }));
        foreach (var ore in Ores) ore.PropertyChanged += (_, _) => RenderConfigChanged?.Invoke();
    }

    partial void OnModeChanged(LayerMode value) => RenderConfigChanged?.Invoke();
    partial void OnSliceYChanged(int value) => RenderConfigChanged?.Invoke();
    partial void OnOreOverlayChanged(bool value) => RenderConfigChanged?.Invoke();

    [RelayCommand]
    private void SetMode(LayerMode mode) => Mode = mode;

    [RelayCommand]
    private void DecrementSliceY() => SliceY = Math.Max(SliceYMin, SliceY - 1);

    [RelayCommand]
    private void IncrementSliceY() => SliceY = Math.Min(SliceYMax, SliceY + 1);

    /// <summary>Clamps the Y-Slice range (and current SliceY) to the given dimension's real world height.</summary>
    public void SetDimension(Dimension dimension)
    {
        (SliceYMin, SliceYMax) = dimension.YRange();
        SliceY = Math.Clamp(SliceY, SliceYMin, SliceYMax);
    }

    public LayerConfig BuildLayerConfig() => new()
    {
        Mode = Mode,
        SliceY = SliceY,
        OreOverlay = OreOverlay,
        OreFilter = Ores.Where(o => o.IsSelected).Select(o => o.BlockName).ToHashSet()
    };
}
