using MapExplorer.Core.Region;

namespace MapExplorer.Core.World;

public sealed record WorldEditSaveResult(int RegionsWritten, int ChunksWritten, string? Error);

/// <summary>Reported as Save works through the dirty-chunk set — ChunksProcessed counts every chunk
/// looked at (including ones that turn out to need no patch), not just ones actually written, so it
/// always reaches TotalChunks by the time Save returns.</summary>
public sealed record WorldEditSaveProgress(int ChunksProcessed, int TotalChunks);

/// <summary>Flushes an EditOverlay's dirty chunks to disk: backs up each touched region file (once
/// per session — an existing .bak is never overwritten, so it always holds the true pre-edit
/// original), patches every dirty chunk's NBT, and writes the region file back atomically.</summary>
public static class WorldEditWriter
{
    public static WorldEditSaveResult Save(string regionDir, EditOverlay overlay, IProgress<WorldEditSaveProgress>? progress = null)
    {
        var dirtyChunks = overlay.DirtyChunks;
        if (dirtyChunks.Count == 0) return new WorldEditSaveResult(0, 0, null);

        int totalChunks = dirtyChunks.Count;
        int processedChunks = 0;
        long nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        int regionsWritten = 0, chunksWritten = 0;

        foreach (var group in dirtyChunks.GroupBy(c => (FloorDiv(c.ChunkX, 32), FloorDiv(c.ChunkZ, 32))))
        {
            var (rx, rz) = group.Key;
            string path = Path.Combine(regionDir, $"r.{rx}.{rz}.mca");

            try
            {
                byte[] original;
                try
                {
                    original = File.ReadAllBytes(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // One report per chunk here too, not a single lump sum for the whole group — a
                    // caller driving a progress bar off this expects it to advance smoothly by one
                    // chunk at a time, not jump.
                    foreach (var _ in group)
                    {
                        processedChunks++;
                        progress?.Report(new WorldEditSaveProgress(processedChunks, totalChunks));
                    }
                    continue; // region file doesn't exist (ungenerated area) — nothing to patch
                }

                string backupPath = path + ".bak";
                if (!File.Exists(backupPath)) File.Copy(path, backupPath);

                var patches = new Dictionary<(int, int), byte[]>();
                foreach (var (cx, cz) in group)
                {
                    // Reported before each chunk's own (cheap but non-zero) patch work, so a caller
                    // watching this mid-save sees "N of Total" advance smoothly rather than in bursts.
                    processedChunks++;
                    progress?.Report(new WorldEditSaveProgress(processedChunks, totalChunks));

                    int localX = cx - rx * 32, localZ = cz - rz * 32;
                    var raw = MapExplorer.Core.Region.RegionFile.ParseChunk(original, Path.GetFileName(path), localX, localZ);
                    if (raw is null) continue; // ungenerated chunk — can't patch what isn't there

                    var edits = overlay.GetEditsForChunk(cx, cz);
                    if (edits.Count == 0) continue;

                    patches[(localX, localZ)] = RegionChunkPatcher.ApplyEdits(raw.Value.Data, cx, cz, edits);
                    chunksWritten++;
                }

                if (patches.Count == 0) continue;

                var rebuilt = RegionFileWriter.Rebuild(original, patches, nowSeconds);
                string tmpPath = path + ".tmp";
                File.WriteAllBytes(tmpPath, rebuilt);
                File.Move(tmpPath, path, overwrite: true);
                regionsWritten++;
            }
            catch (Exception ex)
            {
                return new WorldEditSaveResult(regionsWritten, chunksWritten, ex.Message);
            }
        }

        return new WorldEditSaveResult(regionsWritten, chunksWritten, null);
    }

    private static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);
}
