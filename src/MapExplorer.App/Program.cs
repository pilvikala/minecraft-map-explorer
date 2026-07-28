using Avalonia;
using MapExplorer.Core.Chunk;
using MapExplorer.Core.Region;
using MapExplorer.Core.World;

namespace MapExplorer.App;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Headless benchmark mode for clean timing measurement, bypassing the GUI
        // entirely: `dotnet run -- --bench <regionDir> [threads]`. Cheap regression
        // tooling — can run in CI against a small fixture world without a display.
        if (args.Length > 0 && args[0] == "--bench")
        {
            RunBench(args);
            return;
        }

        // Correctness spot-check against one region file: `dotnet run -- --check <regionFile.mca>`
        if (args.Length > 0 && args[0] == "--check")
        {
            RunCheck(args);
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void RunBench(string[] args)
    {
        string regionDir = args.Length > 1 ? args[1] : throw new ArgumentException("usage: --bench <regionDir> [threads]");
        int? threads = args.Length > 2 ? int.Parse(args[2]) : null;

        // Identity summarize: this benchmark measures raw region/decode throughput, not the
        // summary-building step MainWindow's real load path adds on top (see ChunkSummaryBuilder).
        var result = WorldLoader.Load(regionDir, chunk => chunk, threads);

        Console.WriteLine($"processorCount={Environment.ProcessorCount} threads={threads?.ToString() ?? "default"}");
        Console.WriteLine($"regions={result.RegionCount} chunks={result.Chunks.Count}");
        Console.WriteLine($"decodeMs={result.ElapsedMs}");
    }

    private static void RunCheck(string[] args)
    {
        string filePath = args.Length > 1 ? args[1] : throw new ArgumentException("usage: --check <regionFile.mca>");
        var buffer = File.ReadAllBytes(filePath);
        var raw = RegionFile.Parse(buffer, Path.GetFileName(filePath));
        Console.WriteLine($"rawChunks={raw.Count}");

        var chunks = raw.Select(r => ChunkDecoder.Decode(r.Data, r.ChunkX, r.ChunkZ)).ToList();
        foreach (var c in chunks.OrderBy(c => c.ChunkX).ThenBy(c => c.ChunkZ).Take(3))
        {
            long nonAir = c.Blocks.Count(b => c.Palette[b] != "minecraft:air");
            Console.WriteLine($"chunk({c.ChunkX},{c.ChunkZ}): paletteSize={c.Palette.Count} biomePaletteSize={c.BiomePalette.Count} nonAirBlocks={nonAir}");
            Console.WriteLine($"  y=70 -> {c.GetBlock(0, 70, 0)} | y=-60 -> {c.GetBlock(0, -60, 0)} | y=63,(8,8) -> {c.GetBlock(8, 63, 8)}");
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
