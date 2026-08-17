using System.Text.Json;
using MapExplorer.Core.Nbt.RoundTrip;

namespace MapExplorer.Core.Players;

// Reads and edits player state from a world's save files:
//   - the singleplayer host: level.dat -> Data -> Player
//   - everyone else who has joined: playerdata/<uuid>.dat (its own root compound)
// Both share the same field layout (Pos, Dimension, Health, playerGameType, ...),
// so the extraction/mutation logic below is written once against a plain
// NbtCompoundTag and each caller just resolves which compound that is.
public static class PlayerDataStore
{
    private const int GameTypeSurvival = 0;
    private const int GameTypeSpectator = 3;

    public static List<PlayerInfo> LoadPlayers(string worldPath)
    {
        var players = new List<PlayerInfo>();

        var levelDatPath = Path.Combine(worldPath, "level.dat");
        if (File.Exists(levelDatPath))
        {
            try
            {
                var doc = NbtRoundTrip.ReadGZipFile(levelDatPath);
                if (doc.Root.Get("Data") is NbtCompoundTag data && data.Get("Player") is NbtCompoundTag player)
                {
                    players.Add(BuildPlayerInfo(player, "local", "You (this world)", levelDatPath, isLocal: true));
                }
            }
            catch
            {
                // Corrupt/unreadable level.dat — skip the local player rather than failing
                // the whole list; playerdata/ entries (if any) are still worth showing.
            }
        }

        var playerDataDir = Path.Combine(worldPath, "playerdata");
        if (Directory.Exists(playerDataDir))
        {
            var names = LoadNameCache(worldPath);
            foreach (var file in Directory.EnumerateFiles(playerDataDir, "*.dat"))
            {
                var uuid = Path.GetFileNameWithoutExtension(file);
                try
                {
                    var doc = NbtRoundTrip.ReadGZipFile(file);
                    var name = names.GetValueOrDefault(uuid, ShortUuid(uuid));
                    players.Add(BuildPlayerInfo(doc.Root, uuid, name, file, isLocal: false));
                }
                catch
                {
                    // Skip unreadable/corrupt player files rather than failing the whole list.
                }
            }
        }

        return players;
    }

    public static bool GetHardcore(string worldPath)
    {
        var doc = NbtRoundTrip.ReadGZipFile(Path.Combine(worldPath, "level.dat"));
        return doc.Root.Get("Data") is NbtCompoundTag data && data.Get("hardcore") is NbtByteTag { Value: not 0 };
    }

    public static void SetHardcore(string worldPath, bool hardcore)
    {
        var levelDatPath = Path.Combine(worldPath, "level.dat");
        var doc = NbtRoundTrip.ReadGZipFile(levelDatPath);
        if (doc.Root.Get("Data") is not NbtCompoundTag data)
            throw new InvalidOperationException("level.dat is missing its Data compound");

        data.Set("hardcore", new NbtByteTag((sbyte)(hardcore ? 1 : 0)));
        NbtRoundTrip.WriteGZipFile(levelDatPath, doc);
    }

    public static void SetLocation(PlayerInfo player, double x, double y, double z)
    {
        var doc = NbtRoundTrip.ReadGZipFile(player.FilePath);
        var target = ResolvePlayerCompound(doc, player);

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
        var target = ResolvePlayerCompound(doc, player);

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

    private static NbtCompoundTag ResolvePlayerCompound(NbtDocument doc, PlayerInfo player)
    {
        var target = player.IsLocalPlayer
            ? (doc.Root.Get("Data") as NbtCompoundTag)?.Get("Player") as NbtCompoundTag
            : doc.Root;
        return target ?? throw new InvalidOperationException($"Player data not found in {player.FilePath}");
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
