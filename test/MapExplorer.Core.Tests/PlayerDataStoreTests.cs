using MapExplorer.Core.Nbt.RoundTrip;
using MapExplorer.Core.Players;

namespace MapExplorer.Core.Tests;

public class PlayerDataStoreTests : IDisposable
{
    private readonly string _worldPath = Path.Combine(Path.GetTempPath(), $"world-{Guid.NewGuid():N}");

    public PlayerDataStoreTests()
    {
        Directory.CreateDirectory(_worldPath);
    }

    public void Dispose()
    {
        Directory.Delete(_worldPath, recursive: true);
    }

    private static NbtCompoundTag BuildPlayerCompound(
        double x, double y, double z,
        string dimension = "minecraft:overworld",
        float health = 20f,
        int gameType = 0,
        short deathTime = 0,
        int? previousGameType = null)
    {
        var fields = new Dictionary<string, NbtTag>
        {
            ["Pos"] = new NbtListTag(NbtTagType.Double, [new NbtDoubleTag(x), new NbtDoubleTag(y), new NbtDoubleTag(z)]),
            ["Dimension"] = new NbtStringTag(dimension),
            ["Health"] = new NbtFloatTag(health),
            ["playerGameType"] = new NbtIntTag(gameType),
            ["DeathTime"] = new NbtShortTag(deathTime),
            ["Inventory"] = new NbtListTag(NbtTagType.Compound, [])
        };
        if (previousGameType is not null) fields["previousPlayerGameType"] = new NbtIntTag(previousGameType.Value);
        return new NbtCompoundTag(fields);
    }

    private void WriteLevelDat(NbtCompoundTag? player, bool hardcore = false)
    {
        var data = new Dictionary<string, NbtTag>
        {
            ["hardcore"] = new NbtByteTag((sbyte)(hardcore ? 1 : 0)),
            ["LevelName"] = new NbtStringTag("Test World")
        };
        if (player is not null) data["Player"] = player;

        var root = new NbtCompoundTag(new Dictionary<string, NbtTag> { ["Data"] = new NbtCompoundTag(data) });
        NbtRoundTrip.WriteGZipFile(Path.Combine(_worldPath, "level.dat"), new NbtDocument("", root));
    }

    private void WritePlayerDataFile(string uuid, NbtCompoundTag player)
    {
        var dir = Path.Combine(_worldPath, "playerdata");
        Directory.CreateDirectory(dir);
        NbtRoundTrip.WriteGZipFile(Path.Combine(dir, $"{uuid}.dat"), new NbtDocument("", player));
    }

    [Fact]
    public void LoadPlayers_ReturnsLocalHostFromLevelDat()
    {
        WriteLevelDat(BuildPlayerCompound(10.5, 64, -20.25));

        var players = PlayerDataStore.LoadPlayers(_worldPath);

        var local = Assert.Single(players);
        Assert.True(local.IsLocalPlayer);
        Assert.Equal(10.5, local.Location.X);
        Assert.Equal(64, local.Location.Y);
        Assert.Equal(-20.25, local.Location.Z);
        Assert.Equal(PlayerDimension.Overworld, local.Location.Dimension);
        Assert.False(local.IsDead);
    }

    [Fact]
    public void LoadPlayers_ReturnsPlayerDataEntriesAlongsideLocalHost()
    {
        WriteLevelDat(BuildPlayerCompound(0, 70, 0));
        var uuid = "11111111-2222-3333-4444-555555555555";
        WritePlayerDataFile(uuid, BuildPlayerCompound(100, 80, -5, dimension: "minecraft:the_nether"));

        var players = PlayerDataStore.LoadPlayers(_worldPath);

        Assert.Equal(2, players.Count);
        var joined = players.Single(p => !p.IsLocalPlayer);
        Assert.Equal(uuid, joined.Id);
        Assert.Equal(PlayerDimension.Nether, joined.Location.Dimension);
        Assert.Equal(100, joined.Location.X);
    }

    [Fact]
    public void LoadPlayers_MarksLowHealthOrPendingDeathAsDead()
    {
        WriteLevelDat(BuildPlayerCompound(0, 70, 0, health: 0f, gameType: 3, deathTime: 20));

        var local = Assert.Single(PlayerDataStore.LoadPlayers(_worldPath));

        Assert.True(local.IsDead);
        Assert.Equal(3, local.GameType);
    }

    [Fact]
    public void SetLocation_UpdatesPositionAndPreservesOtherFields()
    {
        WriteLevelDat(BuildPlayerCompound(0, 70, 0));
        var local = Assert.Single(PlayerDataStore.LoadPlayers(_worldPath));

        PlayerDataStore.SetLocation(local, 123.5, 45, -67.5);

        var reloaded = Assert.Single(PlayerDataStore.LoadPlayers(_worldPath));
        Assert.Equal(123.5, reloaded.Location.X);
        Assert.Equal(45, reloaded.Location.Y);
        Assert.Equal(-67.5, reloaded.Location.Z);
        Assert.Equal(20f, reloaded.Health); // untouched field survives the edit
    }

    [Fact]
    public void SetLocation_WritesToPlayerDataFileForJoinedPlayers()
    {
        WriteLevelDat(BuildPlayerCompound(0, 70, 0));
        var uuid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
        WritePlayerDataFile(uuid, BuildPlayerCompound(1, 2, 3));
        var joined = PlayerDataStore.LoadPlayers(_worldPath).Single(p => !p.IsLocalPlayer);

        PlayerDataStore.SetLocation(joined, 500, 90, -500);

        var reloaded = PlayerDataStore.LoadPlayers(_worldPath).Single(p => !p.IsLocalPlayer);
        Assert.Equal(500, reloaded.Location.X);
        Assert.Equal(90, reloaded.Location.Y);
        Assert.Equal(-500, reloaded.Location.Z);
    }

    [Fact]
    public void GetSetHardcore_RoundTrips()
    {
        WriteLevelDat(BuildPlayerCompound(0, 70, 0), hardcore: false);
        Assert.False(PlayerDataStore.GetHardcore(_worldPath));

        PlayerDataStore.SetHardcore(_worldPath, true);
        Assert.True(PlayerDataStore.GetHardcore(_worldPath));

        PlayerDataStore.SetHardcore(_worldPath, false);
        Assert.False(PlayerDataStore.GetHardcore(_worldPath));
    }

    [Fact]
    public void Revive_RestoresHealthAndExitsForcedSpectatorMode()
    {
        // Vanilla hardcore death: health 0, locked into Spectator (3), previous mode remembered.
        WriteLevelDat(BuildPlayerCompound(0, 70, 0, health: 0f, gameType: 3, deathTime: 20, previousGameType: 0), hardcore: true);
        var local = Assert.Single(PlayerDataStore.LoadPlayers(_worldPath));

        PlayerDataStore.Revive(local);

        var revived = Assert.Single(PlayerDataStore.LoadPlayers(_worldPath));
        Assert.Equal(20f, revived.Health);
        Assert.False(revived.IsDead);
        Assert.Equal(0, revived.GameType); // back to Survival, not stuck in Spectator
    }

    [Fact]
    public void Revive_LeavesGameTypeAloneWhenNotForcedIntoSpectator()
    {
        WriteLevelDat(BuildPlayerCompound(0, 70, 0, health: 0f, gameType: 1, deathTime: 20));
        var local = Assert.Single(PlayerDataStore.LoadPlayers(_worldPath));

        PlayerDataStore.Revive(local);

        var revived = Assert.Single(PlayerDataStore.LoadPlayers(_worldPath));
        Assert.Equal(1, revived.GameType); // Creative wasn't touched — only forced Spectator (3) is restored
        Assert.False(revived.IsDead);
    }
}
