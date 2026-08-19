using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MapExplorer.Core.World;
using RenderColors = MapExplorer.Rendering.Colors;

namespace MapExplorer.App.ViewModels;

public enum EditTool
{
    Paint,
    Fill,
    Copy,
    Paste,
    Picker
}

public enum MaterialSlot
{
    Left,
    Right
}

/// <summary>A copied rectangle of block names, sized Width (X) x Depth (Z), captured at the layer
/// that was active at copy time — Paste always writes it into the currently active layer.</summary>
public sealed record ClipboardData(int Width, int Depth, string[,] Blocks);

/// <summary>One row in the material palette/search list/recents — mirrors OreOption's shape (a swatch
/// color alongside the raw data), sharing the single AssignMaterialCommand instance rather than each
/// row carrying its own, since selecting any row does exactly the same thing.</summary>
public sealed class MaterialOption
{
    public required string BlockName { get; init; }
    public required IBrush SwatchBrush { get; init; }
    public required IRelayCommand<string> AssignCommand { get; init; }
}

/// <summary>
/// Edit-mode state: which tool is active, the two paintable materials (left/right click), the
/// searchable material palette, recent materials, and undo/redo/save — wraps a Core EditOverlay the
/// same way MapViewModel wraps a LayerConfig. Owned by MainViewModel alongside Map/Players.
/// </summary>
public partial class EditViewModel : ObservableObject
{
    private const int MaxRecentMaterials = 10;

    public EditOverlay Overlay { get; } = new();

    public ObservableCollection<MaterialOption> RecentMaterials { get; } = [];

    /// <summary>Every block this app can paint — reuses the curated color table (Colors.BlockColors)
    /// rather than a separate ~1000-entry vanilla block registry, since every paintable material
    /// needs a swatch color anyway.</summary>
    public IReadOnlyList<MaterialOption> AllMaterials { get; }

    private readonly Dictionary<string, MaterialOption> _materialOptionsByName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPaintTool))]
    [NotifyPropertyChangedFor(nameof(IsFillTool))]
    [NotifyPropertyChangedFor(nameof(IsCopyTool))]
    [NotifyPropertyChangedFor(nameof(IsPasteTool))]
    [NotifyPropertyChangedFor(nameof(IsPickerTool))]
    private EditTool _selectedTool = EditTool.Paint;

    public bool IsPaintTool => SelectedTool == EditTool.Paint;
    public bool IsFillTool => SelectedTool == EditTool.Fill;
    public bool IsCopyTool => SelectedTool == EditTool.Copy;
    public bool IsPasteTool => SelectedTool == EditTool.Paste;
    public bool IsPickerTool => SelectedTool == EditTool.Picker;

    [ObservableProperty]
    private bool _isEditModeOn;

    [ObservableProperty]
    private string _primaryMaterial = "minecraft:dirt";

    [ObservableProperty]
    private string _secondaryMaterial = "minecraft:air";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLeftArmed))]
    [NotifyPropertyChangedFor(nameof(IsRightArmed))]
    private MaterialSlot _armedSlot = MaterialSlot.Left;

    public bool IsLeftArmed => ArmedSlot == MaterialSlot.Left;
    public bool IsRightArmed => ArmedSlot == MaterialSlot.Right;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredMaterials))]
    private string _materialSearchText = "";

    [ObservableProperty]
    private string _statusText = "";

    // Set only while SaveCommand is running (see Save()) — separate from StatusText, which holds the
    // final result message once a save finishes, so the two don't clobber each other mid-save.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveProgressText))]
    private int _saveProgressCurrent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveProgressText))]
    private int _saveProgressTotal;

    public string SaveProgressText => SaveProgressTotal > 0 ? $"Saving… {SaveProgressCurrent}/{SaveProgressTotal} chunks" : "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasClipboard))]
    private ClipboardData? _clipboard;

    public bool HasClipboard => Clipboard is not null;

    public bool HasRecentMaterials => RecentMaterials.Count > 0;

    public string? RegionDir { get; private set; }

    public bool CanUndo => Overlay.CanUndo;
    public bool CanRedo => Overlay.CanRedo;

    public IEnumerable<MaterialOption> FilteredMaterials =>
        string.IsNullOrWhiteSpace(MaterialSearchText)
            ? AllMaterials
            : AllMaterials.Where(m => m.BlockName.Contains(MaterialSearchText, StringComparison.OrdinalIgnoreCase));

    /// <summary>Raised when IsEditModeOn turns on — MainViewModel forces Y-Slice mode in response,
    /// since editing always targets a single layer.</summary>
    public event Action? EditModeEnabled;

    /// <summary>Raised after a Save that actually wrote something — MainWindow re-summarizes the
    /// affected chunks so Surface/Heightmap/ore views reflect the edit without a full reload.</summary>
    public event Action? Saved;

    public EditViewModel()
    {
        Overlay.ChunksInvalidated += _ => RaiseUndoRedoChanged();
        Overlay.Reset += RaiseUndoRedoChanged;

        AllMaterials = [.. RenderColors.BlockColors.Keys
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new MaterialOption
            {
                BlockName = name,
                SwatchBrush = SwatchFor(name),
                AssignCommand = AssignMaterialCommand
            })];
        _materialOptionsByName = AllMaterials.ToDictionary(o => o.BlockName);
        RecentMaterials.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecentMaterials));
    }

    private static IBrush SwatchFor(string blockName)
    {
        var rgb = RenderColors.GetBlockColor(blockName);
        return new SolidColorBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B));
    }

    partial void OnIsEditModeOnChanged(bool value)
    {
        if (value) EditModeEnabled?.Invoke();
    }

    /// <summary>Called whenever the active world/dimension changes — rebinds the overlay to the new
    /// region's chunk store (discarding any unsaved edits from the previous one, which are keyed by
    /// coordinates that mean something different there) and remembers where Save should write to.</summary>
    public void SetRegionDir(string regionDir, WorldChunkStore chunkStore)
    {
        RegionDir = regionDir;
        Overlay.RebindWorld(chunkStore);
        Clipboard = null;
        StatusText = "";
    }

    private void RaiseUndoRedoChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void SetTool(EditTool tool) => SelectedTool = tool;

    [RelayCommand]
    private void ArmLeft() => ArmedSlot = MaterialSlot.Left;

    [RelayCommand]
    private void ArmRight() => ArmedSlot = MaterialSlot.Right;

    [RelayCommand]
    private void AssignMaterial(string? blockName)
    {
        if (string.IsNullOrEmpty(blockName)) return;
        AssignToSlot(blockName, ArmedSlot);
    }

    /// <summary>Also used directly by the eyedropper tool (MapCanvasControl), which knows which
    /// button/slot was clicked without going through the "armed slot" UI concept.</summary>
    public void AssignToSlot(string blockName, MaterialSlot slot)
    {
        if (slot == MaterialSlot.Left) PrimaryMaterial = blockName;
        else SecondaryMaterial = blockName;
        PushRecent(blockName);
    }

    public void PushRecent(string blockName)
    {
        // Materials painted via the eyedropper/AssignMaterial aren't guaranteed to be in the curated
        // palette (e.g. picking up a block this app doesn't have a color for) — fall back to a
        // synthesized option rather than dropping it from Recents.
        if (!_materialOptionsByName.TryGetValue(blockName, out var option))
        {
            option = new MaterialOption { BlockName = blockName, SwatchBrush = SwatchFor(blockName), AssignCommand = AssignMaterialCommand };
        }

        var existing = RecentMaterials.FirstOrDefault(o => o.BlockName == blockName);
        if (existing is not null) RecentMaterials.Remove(existing);
        RecentMaterials.Insert(0, option);
        while (RecentMaterials.Count > MaxRecentMaterials) RecentMaterials.RemoveAt(RecentMaterials.Count - 1);
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => Overlay.Undo();

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => Overlay.Redo();

    // [RelayCommand] on an async method generates an IAsyncRelayCommand whose IsRunning property is
    // exactly the "save in progress" flag the UI needs (bind Button.Command to SaveCommand and it
    // auto-disables for the duration too — CanExecute returns false while IsRunning is true by
    // default) — no separate IsSaving flag needed.
    [RelayCommand]
    private async Task Save()
    {
        if (RegionDir is null) return;

        var regionDir = RegionDir;
        SaveProgressCurrent = 0;
        SaveProgressTotal = Overlay.DirtyChunks.Count;
        // Progress<T> captures the UI thread's SynchronizationContext at construction, so each
        // Report() below marshals back automatically — safe to assign these properties directly.
        var progress = new Progress<WorldEditSaveProgress>(p =>
        {
            SaveProgressCurrent = p.ChunksProcessed;
            SaveProgressTotal = p.TotalChunks;
        });

        try
        {
            // WorldEditWriter.Save is synchronous file I/O/compression that can run long for a big
            // batch of edits — off the UI thread so the window (and the progress bar above) keeps
            // updating instead of freezing for the duration.
            var result = await Task.Run(() => WorldEditWriter.Save(regionDir, Overlay, progress));
            StatusText = result.Error is not null
                ? $"Save failed: {result.Error}"
                : result.ChunksWritten == 0
                    ? "Nothing to save"
                    : $"Saved {result.ChunksWritten} chunk(s) across {result.RegionsWritten} region file(s)";

            if (result.Error is null && result.ChunksWritten > 0) Saved?.Invoke();
        }
        finally
        {
            SaveProgressCurrent = 0;
            SaveProgressTotal = 0;
        }
    }
}
