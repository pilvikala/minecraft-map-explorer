using System.Collections.Concurrent;
using System.Diagnostics;
using MapExplorer.Core.Chunk;
using MapExplorer.Core.Region;

namespace MapExplorer.Core.World;

public sealed record LoadResult<T>(ConcurrentDictionary<(int, int), T> Chunks, long ElapsedMs, int RegionCount);

public sealed record LoadProgress<T>(int LoadedRegions, int TotalRegions, int LoadedChunks, ConcurrentDictionary<(int, int), T> Chunks);

// Ported from the prototype (validated against the Electron app's WorldLoader.tsx
// + worker-pool.ts). In C#, decode and the caller share one process/one heap, so
// this single Parallel.ForEach is the only concurrency layer needed — the TS app
// needed a second "lanes" layer on top of its worker pool purely to hide
// Electron IPC round-trip latency, which doesn't exist here.
//
// Generic over what's retained per chunk (T): Core has no notion of what the
// caller actually wants to keep resident (e.g. a full ChunkData vs. a much
// smaller derived summary) — `summarize` runs immediately after each chunk is
// decoded, and only its result is stored. The full ChunkData for that chunk
// is otherwise unreferenced and can be collected before the next one decodes.
public static class WorldLoader
{
    public static LoadResult<T> Load<T>(
        string regionDir,
        Func<ChunkData, T> summarize,
        int? degreeOfParallelism = null,
        IProgress<LoadProgress<T>>? progress = null,
        CancellationToken cancellationToken = default)
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

        var chunks = new ConcurrentDictionary<(int, int), T>();
        int loadedRegions = 0;
        int loadedChunks = 0;

        var options = new ParallelOptions
        {
            // Empirically, full logical core count performed best for this
            // workload in .NET (unlike the Electron port, where oversubscribing
            // physical cores via SMT measurably hurt due to memory/cache
            // contention with no compensating benefit) — re-verify via --bench
            // if this ever regresses.
            MaxDegreeOfParallelism = degreeOfParallelism ?? Environment.ProcessorCount,
            CancellationToken = cancellationToken
        };

        Parallel.ForEach(files, options, fileInfo =>
        {
            // Iterations already dispatched when cancellation is requested aren't interrupted by
            // ParallelOptions.CancellationToken alone (it only stops new ones from starting) — bail
            // out before doing the actual read/decode work so a stale dimension switch stops fast.
            if (cancellationToken.IsCancellationRequested) return;

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
                chunks[(raw.ChunkX, raw.ChunkZ)] = summarize(decoded);
                regionChunkCount++;
            }
            Interlocked.Add(ref loadedChunks, regionChunkCount);
            ReportProgress();
        });

        sw.Stop();
        return new LoadResult<T>(chunks, sw.ElapsedMilliseconds, files.Count);

        void ReportProgress()
        {
            if (progress is null) return;
            int done = Interlocked.Increment(ref loadedRegions);
            progress.Report(new LoadProgress<T>(done, files.Count, Volatile.Read(ref loadedChunks), chunks));
        }
    }
}
