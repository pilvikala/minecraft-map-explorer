using System.Collections.Concurrent;
using System.Diagnostics;
using MapExplorer.Core.Chunk;
using MapExplorer.Core.Region;

namespace MapExplorer.Core.World;

public sealed record LoadResult(ConcurrentDictionary<(int, int), ChunkData> Chunks, long ElapsedMs, int RegionCount);

public sealed record LoadProgress(int LoadedRegions, int TotalRegions, int LoadedChunks);

// Ported from the prototype (validated against the Electron app's WorldLoader.tsx
// + worker-pool.ts). In C#, decode and the caller share one process/one heap, so
// this single Parallel.ForEach is the only concurrency layer needed — the TS app
// needed a second "lanes" layer on top of its worker pool purely to hide
// Electron IPC round-trip latency, which doesn't exist here.
public static class WorldLoader
{
    public static LoadResult Load(string regionDir, int? degreeOfParallelism = null, IProgress<LoadProgress>? progress = null)
    {
        var sw = Stopwatch.StartNew();

        var files = Directory.GetFiles(regionDir, "*.mca")
            .Select(path => new FileInfo(path))
            // Largest (most populated) regions first: an LPT-style scheduling
            // heuristic — file size is a good proxy for chunk count, and
            // dispatching the heaviest work first keeps every thread busy
            // instead of a few unlucky threads finishing all the small files
            // and idling while one thread grinds through a late-dispatched
            // heavy region alone.
            .OrderByDescending(f => f.Length)
            .ToList();

        var chunks = new ConcurrentDictionary<(int, int), ChunkData>();
        int loadedRegions = 0;
        int loadedChunks = 0;

        var options = new ParallelOptions
        {
            // Empirically, full logical core count performed best for this
            // workload in .NET (unlike the Electron port, where oversubscribing
            // physical cores via SMT measurably hurt due to memory/cache
            // contention with no compensating benefit) — re-verify via --bench
            // if this ever regresses.
            MaxDegreeOfParallelism = degreeOfParallelism ?? Environment.ProcessorCount
        };

        Parallel.ForEach(files, options, fileInfo =>
        {
            byte[] buffer;
            try
            {
                buffer = File.ReadAllBytes(fileInfo.FullName);
            }
            catch
            {
                ReportProgress();
                return;
            }

            var rawChunks = RegionFile.Parse(buffer, fileInfo.Name);
            int regionChunkCount = 0;
            foreach (var raw in rawChunks)
            {
                ChunkData decoded;
                try
                {
                    decoded = ChunkDecoder.Decode(raw.Data, raw.ChunkX, raw.ChunkZ);
                }
                catch
                {
                    continue;
                }
                chunks[(raw.ChunkX, raw.ChunkZ)] = decoded;
                regionChunkCount++;
            }
            Interlocked.Add(ref loadedChunks, regionChunkCount);
            ReportProgress();
        });

        sw.Stop();
        return new LoadResult(chunks, sw.ElapsedMilliseconds, files.Count);

        void ReportProgress()
        {
            if (progress is null) return;
            int done = Interlocked.Increment(ref loadedRegions);
            progress.Report(new LoadProgress(done, files.Count, Volatile.Read(ref loadedChunks)));
        }
    }
}
