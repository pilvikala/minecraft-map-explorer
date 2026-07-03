using System.IO.Compression;
using MapExplorer.Core.Nbt;

namespace MapExplorer.Core.World;

// Minecraft world discovery for all common installation types. Each "finder"
// knows how to locate the saves/ directories for one installer/launcher.
// DiscoverWorlds() collects them all. Ported from the Electron app's
// src/main/find-worlds.ts — same source list, same layout-detection rules.
public sealed record WorldInfo(
    string Name, // LevelName from level.dat, or folder name
    string FolderName, // directory name under saves/
    string Path, // absolute path to the world root
    string RegionDir, // absolute path to <world>/region/
    double LastModifiedMs, // mtime of level.dat, ms since epoch
    int RegionCount, // number of .mca files in region/
    string Source // human-readable installation label
);

internal sealed record SavesSource(string SavesDir, string Label);

public static class WorldDiscovery
{
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // ─── Filesystem helpers ────────────────────────────────────────────────

    private static bool IsDir(string p)
    {
        try { return Directory.Exists(p); } catch { return false; }
    }

    private static string[] ListDirs(string dir)
    {
        try { return Directory.GetDirectories(dir).Select(System.IO.Path.GetFileName).ToArray()!; }
        catch { return []; }
    }

    private static bool HasMcaFiles(string dir)
    {
        try { return Directory.EnumerateFiles(dir).Any(f => f.EndsWith(".mca", StringComparison.OrdinalIgnoreCase)); }
        catch { return false; }
    }

    private static int CountMcaFiles(string dir)
    {
        try { return Directory.EnumerateFiles(dir).Count(f => f.EndsWith(".mca", StringComparison.OrdinalIgnoreCase)); }
        catch { return 0; }
    }

    // ─── Snap ──────────────────────────────────────────────────────────────
    //
    // Layout: ~/snap/<name>/<revision>/.minecraft/saves/
    //   <revision> is a number; "current" is always a symlink to the active one.
    //
    // Strategy: scan ~/snap/ for any directory that looks like a Minecraft
    // install (contains .minecraft/saves/ under current/ or the highest
    // numeric revision).

    private static List<SavesSource> SnapSources()
    {
        var snapBase = System.IO.Path.Combine(Home, "snap");
        var sources = new List<SavesSource>();
        if (!IsDir(snapBase)) return sources;

        foreach (var snapName in ListDirs(snapBase))
        {
            var packageDir = System.IO.Path.Combine(snapBase, snapName);

            // Try the "current" symlink first — it always resolves to the active revision.
            var currentSaves = System.IO.Path.Combine(packageDir, "current", ".minecraft", "saves");
            if (IsDir(currentSaves))
            {
                sources.Add(new SavesSource(currentSaves, $"Snap ({snapName})"));
                continue;
            }

            // Fallback: pick the highest numeric revision (in case current/ is absent).
            var latestRev = ListDirs(packageDir)
                .Where(d => d.All(char.IsDigit) && d.Length > 0)
                .OrderByDescending(d => int.Parse(d))
                .FirstOrDefault();

            if (latestRev != null)
            {
                var revSaves = System.IO.Path.Combine(packageDir, latestRev, ".minecraft", "saves");
                if (IsDir(revSaves))
                {
                    sources.Add(new SavesSource(revSaves, $"Snap ({snapName} rev {latestRev})"));
                }
            }
        }

        return sources;
    }

    // ─── Flatpak ───────────────────────────────────────────────────────────
    //
    // Layout: ~/.var/app/<app-id>/data/minecraft/saves/

    private static readonly (string Id, string Label)[] FlatpakAppIds =
    [
        ("com.mojang.Minecraft", "Flatpak (com.mojang.Minecraft)"),
        ("com.mojangsales.Minecraft", "Flatpak (com.mojangsales.Minecraft)")
    ];

    private static List<SavesSource> FlatpakSources()
    {
        var sources = new List<SavesSource>();
        foreach (var (id, label) in FlatpakAppIds)
        {
            var saves = System.IO.Path.Combine(Home, ".var", "app", id, "data", "minecraft", "saves");
            if (IsDir(saves)) sources.Add(new SavesSource(saves, label));
        }
        return sources;
    }

    // ─── Official launcher ─────────────────────────────────────────────────

    private static List<SavesSource> OfficialSources()
    {
        string savesDir;
        if (OperatingSystem.IsWindows())
        {
            var appdata = Environment.GetEnvironmentVariable("APPDATA")
                ?? System.IO.Path.Combine(Home, "AppData", "Roaming");
            savesDir = System.IO.Path.Combine(appdata, ".minecraft", "saves");
        }
        else if (OperatingSystem.IsMacOS())
        {
            savesDir = System.IO.Path.Combine(Home, "Library", "Application Support", "minecraft", "saves");
        }
        else
        {
            savesDir = System.IO.Path.Combine(Home, ".minecraft", "saves");
        }

        return IsDir(savesDir) ? [new SavesSource(savesDir, "Official Launcher")] : [];
    }

    // ─── Instance-based launchers (MultiMC / Prism / ATLauncher …) ────────
    //
    // Layout: <base>/instances/<instance-name>/.minecraft/saves/

    private static (string Label, string Base)[] InstanceLaunchers() =>
    [
        ("Prism Launcher", System.IO.Path.Combine(Home, ".local", "share", "PrismLauncher", "instances")),
        ("Prism Launcher (Flatpak)", System.IO.Path.Combine(Home, ".var", "app", "org.prismlauncher.PrismLauncher", "data", "PrismLauncher", "instances")),
        ("MultiMC", System.IO.Path.Combine(Home, ".local", "share", "multimc", "instances")),
        ("MultiMC (Flatpak)", System.IO.Path.Combine(Home, ".var", "app", "org.multimc.MultiMC", "data", "multimc", "instances")),
        ("ATLauncher", System.IO.Path.Combine(Home, ".local", "share", "ATLauncher", "instances")),
        ("GDLauncher Carbon", System.IO.Path.Combine(Home, ".local", "share", "gdlauncher_carbon", "instances"))
    ];

    private static List<SavesSource> InstanceSources()
    {
        var sources = new List<SavesSource>();
        foreach (var (label, baseDir) in InstanceLaunchers())
        {
            if (!IsDir(baseDir)) continue;
            foreach (var instance in ListDirs(baseDir))
            {
                var saves = System.IO.Path.Combine(baseDir, instance, ".minecraft", "saves");
                if (IsDir(saves)) sources.Add(new SavesSource(saves, $"{label} — {instance}"));
            }
        }
        return sources;
    }

    // ─── level.dat name extraction ─────────────────────────────────────────
    //
    // Uses the real NBT parser (Core/Nbt) rather than an ad-hoc byte scan —
    // level.dat's root is a gzip'd compound with a "Data" sub-compound
    // containing "LevelName" among many other fields.

    private static string ReadLevelName(string worldPath, string fallback)
    {
        try
        {
            var levelDatPath = System.IO.Path.Combine(worldPath, "level.dat");
            using var input = new FileStream(levelDatPath, FileMode.Open, FileAccess.Read);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);

            var nbt = Nbt.Nbt.Parse(output.ToArray());
            var data = nbt.Get("Data") as NbtCompound ?? nbt;
            if (data.Get("LevelName") is NbtString levelName) return levelName.Value;
        }
        catch
        {
            // fall through to fallback
        }
        return fallback;
    }

    // ─── Region directory detection ────────────────────────────────────────
    //
    // Two world layouts exist:
    //   Classic (<=1.17):  <world>/region/*.mca
    //   Dimensions (1.18+): <world>/dimensions/<namespace>/<dim>/region/*.mca
    //
    // Always returns the overworld region dir, preferring classic then modern.

    // internal (not private) so WorldDiscoveryTests can exercise layout detection
    // directly against a temp directory, without needing to fake HOME/APPDATA.
    internal static string? FindOverworldRegionDir(string worldPath)
    {
        // 1. Classic layout: <world>/region/
        var classic = System.IO.Path.Combine(worldPath, "region");
        if (IsDir(classic) && HasMcaFiles(classic)) return classic;

        // 2. Standard dimensions layout: <world>/dimensions/minecraft/overworld/region/
        var modern = System.IO.Path.Combine(worldPath, "dimensions", "minecraft", "overworld", "region");
        if (IsDir(modern) && HasMcaFiles(modern)) return modern;

        // 3. Any namespace/dimension with region files (custom dimensions / modded)
        var dimBase = System.IO.Path.Combine(worldPath, "dimensions");
        if (IsDir(dimBase))
        {
            foreach (var ns in ListDirs(dimBase))
            {
                foreach (var dim in ListDirs(System.IO.Path.Combine(dimBase, ns)))
                {
                    var r = System.IO.Path.Combine(dimBase, ns, dim, "region");
                    if (IsDir(r) && HasMcaFiles(r)) return r;
                }
            }
        }

        return null;
    }

    // ─── Public API ─────────────────────────────────────────────────────────

    /// <summary>Collect saves/ directories from all known installation types on this OS.</summary>
    internal static List<SavesSource> FindSavesDirs()
    {
        var sources = new List<SavesSource>();
        sources.AddRange(OfficialSources());
        if (OperatingSystem.IsLinux())
        {
            sources.AddRange(SnapSources());
            sources.AddRange(FlatpakSources());
        }
        sources.AddRange(InstanceSources());
        return sources;
    }

    /// <summary>Scan all discovered saves/ directories and return a flat, sorted world list.</summary>
    public static List<WorldInfo> DiscoverWorlds()
    {
        var worlds = new List<WorldInfo>();
        var seen = new HashSet<string>();

        foreach (var (savesDir, label) in FindSavesDirs())
        {
            foreach (var folderName in ListDirs(savesDir))
            {
                var worldPath = System.IO.Path.Combine(savesDir, folderName);
                if (!seen.Add(worldPath)) continue;

                var regionDir = FindOverworldRegionDir(worldPath);
                if (regionDir is null) continue;

                var levelDatPath = System.IO.Path.Combine(worldPath, "level.dat");
                var lastModified = File.Exists(levelDatPath)
                    ? new DateTimeOffset(File.GetLastWriteTimeUtc(levelDatPath)).ToUnixTimeMilliseconds()
                    : new DateTimeOffset(Directory.GetLastWriteTimeUtc(worldPath)).ToUnixTimeMilliseconds();

                worlds.Add(new WorldInfo(
                    Name: ReadLevelName(worldPath, folderName),
                    FolderName: folderName,
                    Path: worldPath,
                    RegionDir: regionDir,
                    LastModifiedMs: lastModified,
                    RegionCount: CountMcaFiles(regionDir),
                    Source: label
                ));
            }
        }

        return worlds.OrderByDescending(w => w.LastModifiedMs).ToList();
    }
}
