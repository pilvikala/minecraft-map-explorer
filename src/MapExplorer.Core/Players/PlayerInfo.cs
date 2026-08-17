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
/// One player's saved state. <see cref="IsLocalPlayer"/> distinguishes the singleplayer
/// host — whose data lives inside level.dat's Data.Player compound and has no recorded
/// UUID/name — from a joined player, whose data is its own file under playerdata/.
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
