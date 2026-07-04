namespace MapExplorer.App.ViewModels;

public enum Dimension
{
    Overworld,
    Nether,
    End
}

public static class DimensionExtensions
{
    /// <summary>The real world-height Y range for a dimension (Overworld's is a superset of the other two).</summary>
    public static (int Min, int Max) YRange(this Dimension dimension) => dimension switch
    {
        Dimension.Nether => (0, 127),
        Dimension.End => (0, 255),
        _ => (-64, 319)
    };
}

/// <summary>A world chosen from the picker or browse dialog, with the region dir for each dimension it has generated.</summary>
public sealed record SelectedWorld(string Name, string OverworldDir, string? NetherDir, string? EndDir)
{
    public string? RegionDirFor(Dimension dimension) => dimension switch
    {
        Dimension.Overworld => OverworldDir,
        Dimension.Nether => NetherDir,
        Dimension.End => EndDir,
        _ => null
    };
}
