namespace MapExplorer.Rendering;

public enum LayerMode
{
    Surface,
    Slice,
    Heightmap,
    Biome
}

// Structural equality (record) is deliberate: this doubles as the tile-cache
// invalidation key (see MapCanvasControl in the App project) — comparing two
// configs for "did anything that affects rendering change" is just `!=`.
public sealed record LayerConfig
{
    public LayerMode Mode { get; init; } = LayerMode.Surface;
    public int SliceY { get; init; } = 64; // used for Slice mode
    public IReadOnlySet<string> OreFilter { get; init; } = new HashSet<string>(); // ore block names to highlight (empty = none)
    public bool OreOverlay { get; init; } // if true, overlay ores on top of current mode

    public bool Equals(LayerConfig? other) =>
        other is not null &&
        Mode == other.Mode &&
        SliceY == other.SliceY &&
        OreOverlay == other.OreOverlay &&
        OreFilter.SetEquals(other.OreFilter);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Mode);
        hash.Add(SliceY);
        hash.Add(OreOverlay);
        // Order-independent hash contribution so equal sets hash equally.
        int oreHash = 0;
        foreach (var ore in OreFilter) oreHash ^= ore.GetHashCode();
        hash.Add(oreHash);
        return hash.ToHashCode();
    }
}
