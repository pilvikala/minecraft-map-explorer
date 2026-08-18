using System.Text;
using System.Text.Json;
using MapExplorer.Core.Nbt.RoundTrip;

namespace MapExplorer.Core.Players;

// Reads and edits player state from a world's save files. Two on-disk layouts exist:
//   - legacy: the singleplayer host is embedded at level.dat -> Data -> Player; everyone
//     else who has joined is its own file under playerdata/<uuid>.dat (root compound).
//   - newer versions: there's no embedded Player compound at all — level.dat -> Data ->
//     singleplayer_uuid instead points at the host's own file, stored the same way as
//     joined players but under players/data/<uuid>.dat.
// Either way, once a player's compound is located it has the same field layout (Pos,
// Dimension, Health, playerGameType, ...), so the extraction/mutation logic below is
// written once against a plain NbtCompoundTag and each caller just resolves which
// compound that is.
public static class PlayerDataStore
{
    private const int GameTypeSurvival = 0;
    private const int GameTypeSpectator = 3;

    public static List<PlayerInfo> LoadPlayers(string worldPath)
    {
        var players = new List<PlayerInfo>();
        string? singleplayerUuid = null;

        var levelDatPath = Path.Combine(worldPath, "level.dat");
        if (File.Exists(levelDatPath))
        {
            try
            {
                var doc = NbtRoundTrip.ReadGZipFile(levelDatPath);
                if (doc.Root.Get("Data") is NbtCompoundTag data)
                {
                    if (data.Get("Player") is NbtCompoundTag player)
                    {
                        players.Add(BuildPlayerInfo(player, "local", "You (this world)", levelDatPath, isLocal: true));
                    }
                    else if (data.Get("singleplayer_uuid") is NbtIntArrayTag { Value.Length: 4 } uuidTag)
                    {
                        // Newer layout: the host isn't embedded here — remember its uuid so the
                        // playerdata scan below can recognize and relabel that entry.
                        singleplayerUuid = IntArrayToUuid(uuidTag.Value);
                    }
                }
            }
            catch
            {
                // Corrupt/unreadable level.dat — skip the local player rather than failing
                // the whole list; playerdata/ entries (if any) are still worth showing.
            }
        }

        var playerDataDir = ResolvePlayerDataDir(worldPath);
        if (playerDataDir is not null)
        {
            var names = LoadNameCache(worldPath);
            foreach (var file in Directory.EnumerateFiles(playerDataDir, "*.dat"))
            {
                var uuid = Path.GetFileNameWithoutExtension(file);
                try
                {
                    var doc = NbtRoundTrip.ReadGZipFile(file);
                    var isLocal = uuid.Equals(singleplayerUuid, StringComparison.OrdinalIgnoreCase);
                    var name = isLocal ? "You (this world)" : names.GetValueOrDefault(uuid, ShortUuid(uuid));
                    players.Add(BuildPlayerInfo(doc.Root, uuid, name, file, isLocal));
                }
                catch
                {
                    // Skip unreadable/corrupt player files rather than failing the whole list.
                }
            }
        }

        return players;
    }

    // playerdata/ moved to players/data/ in newer versions (advancements/ and stats/ made
    // the same move, alongside it under players/) — try the legacy top-level folder first
    // and fall back to the newer nested one.
    private static string? ResolvePlayerDataDir(string worldPath)
    {
        var legacy = Path.Combine(worldPath, "playerdata");
        if (Directory.Exists(legacy)) return legacy;

        var modern = Path.Combine(worldPath, "players", "data");
        return Directory.Exists(modern) ? modern : null;
    }

    // Mirrors Java's UUID(mostSigBits, leastSigBits) construction from the 4-int NBT
    // encoding used for the "UUID"/"singleplayer_uuid" tags, formatted to match the
    // dashed lowercase filenames used under playerdata/ and players/data/.
    private static string IntArrayToUuid(int[] ints)
    {
        var hex = new StringBuilder(32);
        foreach (var i in ints) hex.Append(((uint)i).ToString("x8"));
        var h = hex.ToString();
        return $"{h[..8]}-{h[8..12]}-{h[12..16]}-{h[16..20]}-{h[20..32]}";
    }

    public static bool GetHardcore(string worldPath)
    {
        var doc = NbtRoundTrip.ReadGZipFile(Path.Combine(worldPath, "level.dat"));
        if (doc.Root.Get("Data") is not NbtCompoundTag data) return false;

        // Newer versions nest difficulty under Data.difficulty_settings.hardcore and leave
        // the flat Data.hardcore field behind stale (0) — check the nested field first when
        // it exists, since it's the one the game itself actually reads.
        if (data.Get("difficulty_settings") is NbtCompoundTag settings)
            return settings.Get("hardcore") is NbtByteTag { Value: not 0 };

        return data.Get("hardcore") is NbtByteTag { Value: not 0 };
    }

    public static void SetHardcore(string worldPath, bool hardcore)
    {
        var levelDatPath = Path.Combine(worldPath, "level.dat");
        var doc = NbtRoundTrip.ReadGZipFile(levelDatPath);
        if (doc.Root.Get("Data") is not NbtCompoundTag data)
            throw new InvalidOperationException("level.dat is missing its Data compound");

        var value = new NbtByteTag((sbyte)(hardcore ? 1 : 0));
        if (data.Get("difficulty_settings") is NbtCompoundTag settings)
            settings.Set("hardcore", value);
        data.Set("hardcore", value);

        NbtRoundTrip.WriteGZipFile(levelDatPath, doc);
    }

    public static void SetLocation(PlayerInfo player, double x, double y, double z)
    {
        var doc = NbtRoundTrip.ReadGZipFile(player.FilePath);
        var target = ResolvePlayerCompound(doc);

        target.Set("Pos", new NbtListTag(NbtTagType.Double,
        [
            new NbtDoubleTag(x),
            new NbtDoubleTag(y),
            new NbtDoubleTag(z)
        ]));

        NbtRoundTrip.WriteGZipFile(player.FilePath, doc);
    }

    /// <summary>Undoes a hardcore death: restores health/air and, if the death forced the
    /// player into locked Spectator mode, restores whatever mode they were in before.</summary>
    public static void Revive(PlayerInfo player)
    {
        var doc = NbtRoundTrip.ReadGZipFile(player.FilePath);
        var target = ResolvePlayerCompound(doc);

        target.Set("Health", new NbtFloatTag(20f));
        target.Set("DeathTime", new NbtShortTag(0));
        target.Set("Air", new NbtShortTag(300));

        if (target.Get("playerGameType") is NbtIntTag { Value: GameTypeSpectator })
        {
            int previousGameType = target.Get("previousPlayerGameType") switch
            {
                NbtIntTag { Value: >= 0 } i => i.Value,
                _ => GameTypeSurvival
            };
            target.Set("playerGameType", new NbtIntTag(previousGameType));
        }

        NbtRoundTrip.WriteGZipFile(player.FilePath, doc);
    }

    // A local player's compound is embedded at Data.Player only on the legacy layout (see
    // LoadPlayers); on newer worlds it's its own file with the fields directly on the root,
    // same as a joined player. Rather than trust IsLocalPlayer to know which, just look at
    // the document actually loaded from player.FilePath — legacy level.dat has a Data.Player
    // to descend into, everything else's fields are already on the root.
    private static NbtCompoundTag ResolvePlayerCompound(NbtDocument doc)
    {
        if (doc.Root.Get("Data") is NbtCompoundTag data && data.Get("Player") is NbtCompoundTag embedded)
            return embedded;
        return doc.Root;
    }

    private static PlayerInfo BuildPlayerInfo(NbtCompoundTag player, string id, string name, string filePath, bool isLocal)
    {
        var location = ReadLocation(player);
        float health = player.Get("Health") switch { NbtFloatTag f => f.Value, _ => 20f };
        int gameType = player.Get("playerGameType") switch { NbtIntTag i => i.Value, _ => GameTypeSurvival };
        short deathTime = player.Get("DeathTime") switch { NbtShortTag s => s.Value, _ => (short)0 };
        bool isDead = health <= 0f || deathTime > 0;
        return new PlayerInfo(id, name, isLocal, filePath, location, health, gameType, isDead);
    }

    private static PlayerLocation ReadLocation(NbtCompoundTag player)
    {
        double x = 0, y = 64, z = 0;
        if (player.Get("Pos") is NbtListTag { Items.Count: >= 3 } pos)
        {
            x = DoubleOf(pos.Items[0]);
            y = DoubleOf(pos.Items[1]);
            z = DoubleOf(pos.Items[2]);
        }

        var (dimension, raw) = ReadDimension(player);
        return new PlayerLocation(x, y, z, dimension, raw);
    }

    private static double DoubleOf(NbtTag tag) => tag switch
    {
        NbtDoubleTag d => d.Value,
        NbtFloatTag f => f.Value,
        _ => 0
    };

    private static (PlayerDimension Dimension, string Raw) ReadDimension(NbtCompoundTag player)
    {
        switch (player.Get("Dimension"))
        {
            case NbtStringTag s:
                return (s.Value switch
                {
                    "minecraft:overworld" => PlayerDimension.Overworld,
                    "minecraft:the_nether" => PlayerDimension.Nether,
                    "minecraft:the_end" => PlayerDimension.End,
                    _ => PlayerDimension.Other
                }, s.Value);
            case NbtIntTag i:
                // Pre-1.16 numeric dimension ids.
                return (i.Value switch
                {
                    0 => PlayerDimension.Overworld,
                    -1 => PlayerDimension.Nether,
                    1 => PlayerDimension.End,
                    _ => PlayerDimension.Other
                }, $"dim {i.Value}");
            default:
                return (PlayerDimension.Overworld, "minecraft:overworld");
        }
    }

    private static string ShortUuid(string uuid) => uuid.Length > 8 ? uuid[..8] : uuid;

    // usercache.json (uuid -> last-seen name) lives at the launcher root, two directory
    // levels above <launcherRoot>/saves/<world> for every installer WorldDiscovery knows
    // about. Best-effort only: a missing file or unrecognized layout just falls back to
    // showing short UUIDs instead of failing the whole player list.
    private static Dictionary<string, string> LoadNameCache(string worldPath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var savesDir = Directory.GetParent(worldPath)?.FullName;
            var launcherRoot = savesDir is null ? null : Directory.GetParent(savesDir)?.FullName;
            if (launcherRoot is null) return result;

            var cachePath = Path.Combine(launcherRoot, "usercache.json");
            if (!File.Exists(cachePath)) return result;

            using var stream = File.OpenRead(cachePath);
            using var json = JsonDocument.Parse(stream);
            foreach (var entry in json.RootElement.EnumerateArray())
            {
                if (entry.TryGetProperty("uuid", out var uuidProp) && entry.TryGetProperty("name", out var nameProp))
                {
                    var uuid = uuidProp.GetString();
                    var name = nameProp.GetString();
                    if (uuid is not null && name is not null) result[uuid] = name;
                }
            }
        }
        catch
        {
            // Best-effort — fall back to short UUIDs.
        }
        return result;
    }
}
