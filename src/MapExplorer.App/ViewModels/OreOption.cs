using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MapExplorer.Rendering;

namespace MapExplorer.App.ViewModels;

// One row in the ore-overlay checklist. Ported from LayerPanel.tsx's
// ORE_LABELS map (block name -> display label), paired with its swatch color
// from Colors.OreBlocks.
public partial class OreOption : ObservableObject
{
    public required string BlockName { get; init; }
    public required string Label { get; init; }
    public required Rgb Color { get; init; }

    public IBrush SwatchBrush => new SolidColorBrush(Avalonia.Media.Color.FromRgb(Color.R, Color.G, Color.B));

    [ObservableProperty]
    private bool _isSelected;
}
