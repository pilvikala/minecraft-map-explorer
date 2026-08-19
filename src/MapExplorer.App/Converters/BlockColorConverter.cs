using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using RenderColors = MapExplorer.Rendering.Colors;

namespace MapExplorer.App.Converters;

/// <summary>Converts a block name (e.g. "minecraft:dirt") to its curated swatch color — used by the
/// edit-mode material palette/search list/recents, which bind directly to plain block-name strings
/// rather than a wrapping view-model per entry (unlike OreOption, this list is filtered/reordered
/// live by search text and MRU order, so a lightweight converter is simpler than keeping a parallel
/// ObservableCollection of wrapper objects in sync).</summary>
public sealed class BlockColorConverter : IValueConverter
{
    public static readonly BlockColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var rgb = RenderColors.GetBlockColor(value as string ?? "minecraft:air");
        return new SolidColorBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
