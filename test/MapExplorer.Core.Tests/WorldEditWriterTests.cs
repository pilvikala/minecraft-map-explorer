using MapExplorer.Core.Chunk;
using MapExplorer.Core.World;
using static MapExplorer.Core.Tests.NbtFixtureBuilder;

namespace MapExplorer.Core.Tests;

public sealed class WorldEditWriterTests : IDisposable
{
    private readonly string _regionDir = Path.Combine(Path.GetTempPath(), $"region-{Guid.NewGuid():N}");

    public WorldEditWriterTests()
    {
        Directory.CreateDirectory(_regionDir);
    }

    public void Dispose()
    {
        Directory.Delete(_regionDir, recursive: true);
    }

    private string WriteFixtureRegion(int regionX, int regionZ, params (int LocalX, int LocalZ, byte[] Nbt)[] chunks)
    {
        var path = Path.Combine(_regionDir, $"r.{regionX}.{regionZ}.mca");
        File.WriteAllBytes(path, RegionFileFixtureBuilder.BuildRegion(chunks));
        return path;
    }

    private static EditOverlay MakeOverlay(string regionDir)
    {
        var overlay = new EditOverlay();
        overlay.RebindWorld(new WorldChunkStore(regionDir));
        return overlay;
    }

    [Fact]
    public void Save_WritesEditedBlocksAndCreatesABackupOnlyOnce()
    {
        var nbt = BuildChunkNbt([new SectionFixture(Y: 0, BlockPalette: ["minecraft:stone"])]);
        var path = WriteFixtureRegion(0, 0, (LocalX: 2, LocalZ: 3, Nbt: nbt));
        var originalBytes = File.ReadAllBytes(path);

        var overlay = MakeOverlay(_regionDir);
        overlay.BeginBatch();
        overlay.Set(x: 2 * 16 + 1, y: 5, z: 3 * 16 + 4, "minecraft:dirt");
        overlay.EndBatch();

        var result = WorldEditWriter.Save(_regionDir, overlay);

        Assert.Null(result.Error);
        Assert.Equal(1, result.RegionsWritten);
        Assert.Equal(1, result.ChunksWritten);

        var backupPath = path + ".bak";
        Assert.True(File.Exists(backupPath));
        Assert.Equal(originalBytes, File.ReadAllBytes(backupPath));

        var savedBuffer = File.ReadAllBytes(path);
        var chunk = MapExplorer.Core.Region.RegionFile.ParseChunk(savedBuffer, "r.0.0.mca", 2, 3);
        Assert.NotNull(chunk);
        var decoded = ChunkDecoder.Decode(chunk.Value.Data, 2, 3);
        Assert.Equal("minecraft:dirt", decoded.GetBlock(1, 5, 4));
        Assert.Equal("minecraft:stone", decoded.GetBlock(0, 5, 4));

        // A second save (e.g. after more edits) must not clobber the backup with the already-edited file.
        overlay.BeginBatch();
        overlay.Set(x: 2 * 16 + 1, y: 5, z: 3 * 16 + 4, "minecraft:glass");
        overlay.EndBatch();
        WorldEditWriter.Save(_regionDir, overlay);

        Assert.Equal(originalBytes, File.ReadAllBytes(backupPath));
    }

    [Fact]
    public void Save_WithNoDirtyChunks_IsANoOp()
    {
        var overlay = MakeOverlay(_regionDir);

        var result = WorldEditWriter.Save(_regionDir, overlay);

        Assert.Equal(0, result.RegionsWritten);
        Assert.Equal(0, result.ChunksWritten);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Save_SkipsRegionsThatDoNotExistOnDisk()
    {
        var overlay = MakeOverlay(_regionDir);
        overlay.BeginBatch();
        overlay.Set(x: 999, y: 5, z: 999, "minecraft:dirt");
        overlay.EndBatch();

        var result = WorldEditWriter.Save(_regionDir, overlay);

        Assert.Equal(0, result.RegionsWritten);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Save_SurfacesARealIoError_InsteadOfTreatingItAsAnUngeneratedRegion()
    {
        // A directory sitting where the region file should be: File.ReadAllBytes throws for this
        // (UnauthorizedAccessException on both Windows and Unix, per .NET's directory handling) but
        // it is NOT FileNotFoundException/DirectoryNotFoundException — the path genuinely exists.
        // Must surface as a save failure, not be silently swallowed as "ungenerated area."
        Directory.CreateDirectory(Path.Combine(_regionDir, "r.0.0.mca"));

        var overlay = MakeOverlay(_regionDir);
        overlay.BeginBatch();
        overlay.Set(x: 0, y: 5, z: 0, "minecraft:dirt");
        overlay.EndBatch();

        var result = WorldEditWriter.Save(_regionDir, overlay);

        Assert.NotNull(result.Error);
        Assert.Equal(0, result.RegionsWritten);
    }

    [Fact]
    public void Save_ReportsProgressForEveryDirtyChunkAndEndsAtTheTotal()
    {
        var stoneNbt = BuildChunkNbt([new SectionFixture(Y: 0, BlockPalette: ["minecraft:stone"])]);
        WriteFixtureRegion(0, 0,
            (LocalX: 0, LocalZ: 0, Nbt: stoneNbt),
            (LocalX: 1, LocalZ: 0, Nbt: stoneNbt),
            (LocalX: 2, LocalZ: 0, Nbt: stoneNbt));

        var overlay = MakeOverlay(_regionDir);
        overlay.BeginBatch();
        overlay.Set(x: 0, y: 5, z: 0, "minecraft:dirt");
        overlay.Set(x: 16, y: 5, z: 0, "minecraft:dirt");
        overlay.Set(x: 32, y: 5, z: 0, "minecraft:dirt");
        overlay.EndBatch();

        var reports = new List<WorldEditSaveProgress>();
        WorldEditWriter.Save(_regionDir, overlay, new SynchronousProgress<WorldEditSaveProgress>(reports.Add));

        Assert.Equal(3, reports.Count);
        Assert.All(reports, r => Assert.Equal(3, r.TotalChunks));
        Assert.Equal([1, 2, 3], reports.Select(r => r.ChunksProcessed));
    }

    // Progress<T> posts its callback back through whatever SynchronizationContext was current at
    // construction time — in a test host that's often no context at all, which falls back to an
    // asynchronous ThreadPool post, so Save returning wouldn't guarantee every report has landed yet.
    // This trivial synchronous IProgress<T> sidesteps that entirely for deterministic assertions.
    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
