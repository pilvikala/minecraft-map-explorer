using MapExplorer.Core.World;

namespace MapExplorer.App.ViewModels;

// One card in the world picker grid. Wraps WorldInfo with the display
// formatting WorldLoader.tsx's WorldPicker component did inline (formatAge, etc.).
public sealed class WorldCardViewModel(WorldInfo info)
{
    public WorldInfo Info { get; } = info;
    public string Name => Info.Name;
    public string Source => Info.Source;
    public string RegionDir => Info.RegionDir;
    public string RegionCountText => $"{Info.RegionCount} region{(Info.RegionCount != 1 ? "s" : "")}";
    public string AgeText => FormatAge(Info.LastModifiedMs);

    private static string FormatAge(double lastModifiedMs)
    {
        var diff = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - lastModifiedMs;
        var mins = (int)(diff / 60000);
        var hours = (int)(diff / 3600000);
        var days = (int)(diff / 86400000);
        if (days > 0) return $"{days} day{(days > 1 ? "s" : "")} ago";
        if (hours > 0) return $"{hours} hour{(hours > 1 ? "s" : "")} ago";
        if (mins > 0) return $"{mins} min ago";
        return "just now";
    }
}
