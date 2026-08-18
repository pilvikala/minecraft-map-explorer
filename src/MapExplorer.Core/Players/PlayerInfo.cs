namespace MapExplorer.Core.Players;

public enum PlayerDimension
{
    Overworld,
    Nether,
    End,
    Other
}

public sealed record PlayerLocation(double X, double Y, double Z, PlayerDimension Dimension, string RawDimension)
{
    public string DimensionLabel => Dimension switch
    {
        PlayerDimension.Overworld => "Overworld",
        PlayerDimension.Nether => "Nether",
        PlayerDimension.End => "End",
        _ => RawDimension
    };
}

/// <summary>
/// One player's saved state. <see cref="IsLocalPlayer"/> flags the singleplayer host, as
/// opposed to a joined player. On legacy worlds the host's data lives inside level.dat's
/// Data.Player compound and has no recorded UUID/name; on newer worlds it instead has its
/// own file (under playerdata/ or players/data/, alongside joined players) pointed to by
/// level.dat's Data.singleplayer_uuid. See <see cref="MapExplorer.Core.Players.PlayerDataStore"/>
/// for how each layout is detected.
/// </summary>
public sealed record PlayerInfo(
    string Id,
    string Name,
    bool IsLocalPlayer,
    string FilePath,
    PlayerLocation Location,
    float Health,
    int GameType,
    bool IsDead);
