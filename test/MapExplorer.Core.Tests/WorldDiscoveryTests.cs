using MapExplorer.Core.World;

namespace MapExplorer.Core.Tests;

// No equivalent test existed for find-worlds.ts in the Electron app — this is
// new coverage for genuinely platform-sensitive path-resolution logic.
// FindOverworldRegionDir is internal (see InternalsVisibleTo in the Core
// csproj) so these tests can exercise real layout detection against a real
// temp directory, without needing to fake HOME/APPDATA/the OS.
public sealed class WorldDiscoveryTests : IDisposable
{
    private readonly string _tempDir;

    public WorldDiscoveryTests()
    {
        _tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mapexplorer-test-" + Guid.NewGuid())).FullName;
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort cleanup */ }
    }

    private void CreateMcaFile(string regionDir)
    {
        Directory.CreateDirectory(regionDir);
        File.WriteAllBytes(Path.Combine(regionDir, "r.0.0.mca"), new byte[8192]);
    }

    [Fact]
    public void FindsClassicRegionLayout()
    {
        var world = Directory.CreateDirectory(Path.Combine(_tempDir, "MyWorld")).FullName;
        var regionDir = Path.Combine(world, "region");
        CreateMcaFile(regionDir);

        var result = WorldDiscovery.FindOverworldRegionDir(world);

        Assert.Equal(regionDir, result);
    }

    [Fact]
    public void FindsModernDimensionsLayoutWhenNoClassicRegionExists()
    {
        var world = Directory.CreateDirectory(Path.Combine(_tempDir, "MyWorld")).FullName;
        var regionDir = Path.Combine(world, "dimensions", "minecraft", "overworld", "region");
        CreateMcaFile(regionDir);

        var result = WorldDiscovery.FindOverworldRegionDir(world);

        Assert.Equal(regionDir, result);
    }

    [Fact]
    public void PrefersClassicLayoutOverModernWhenBothExist()
    {
        var world = Directory.CreateDirectory(Path.Combine(_tempDir, "MyWorld")).FullName;
        var classicRegionDir = Path.Combine(world, "region");
        var modernRegionDir = Path.Combine(world, "dimensions", "minecraft", "overworld", "region");
        CreateMcaFile(classicRegionDir);
        CreateMcaFile(modernRegionDir);

        var result = WorldDiscovery.FindOverworldRegionDir(world);

        Assert.Equal(classicRegionDir, result);
    }

    [Fact]
    public void FindsCustomNamespaceDimensionWhenNoStandardOverworldExists()
    {
        // Modded/custom dimension layout: dimensions/<namespace>/<dim>/region,
        // where <namespace> isn't "minecraft" and <dim> isn't "overworld".
        var world = Directory.CreateDirectory(Path.Combine(_tempDir, "MyWorld")).FullName;
        var customRegionDir = Path.Combine(world, "dimensions", "mymod", "customdim", "region");
        CreateMcaFile(customRegionDir);

        var result = WorldDiscovery.FindOverworldRegionDir(world);

        Assert.Equal(customRegionDir, result);
    }

    [Fact]
    public void ReturnsNullWhenNoRegionFilesExistAnywhere()
    {
        var world = Directory.CreateDirectory(Path.Combine(_tempDir, "MyWorld")).FullName;
        // Empty region dir (no .mca files) should not count.
        Directory.CreateDirectory(Path.Combine(world, "region"));

        var result = WorldDiscovery.FindOverworldRegionDir(world);

        Assert.Null(result);
    }

    [Fact]
    public void ReturnsNullForNonexistentWorldPath()
    {
        var result = WorldDiscovery.FindOverworldRegionDir(Path.Combine(_tempDir, "DoesNotExist"));

        Assert.Null(result);
    }

    [Fact]
    public void DiscoverWorldsRunsWithoutThrowingOnThisMachine()
    {
        // Smoke test: DiscoverWorlds() scans real, hardcoded OS-specific paths
        // (HOME/APPDATA-relative) — it can't be redirected to a temp dir without
        // a larger refactor, so this just verifies it completes cleanly and
        // returns internally-consistent results, rather than asserting on
        // machine-specific world contents.
        var worlds = WorldDiscovery.DiscoverWorlds();

        Assert.NotNull(worlds);
        foreach (var world in worlds)
        {
            Assert.True(Directory.Exists(world.RegionDir));
            Assert.True(world.RegionCount > 0);
        }
    }
}
