using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MapExplorer.Core.Players;

namespace MapExplorer.App.ViewModels;

/// <summary>One row in the Players panel. Delegates actual file edits to the owning
/// PlayersViewModel so it stays the single place that knows about the world path.</summary>
public sealed partial class PlayerRowViewModel : ObservableObject
{
    private readonly PlayersViewModel _owner;

    public PlayerInfo Info { get; private set; }

    public string Name => Info.Name;
    public string DimensionText => Info.Location.DimensionLabel;
    public string HealthText => $"{Info.Health:0.#} HP";
    public bool IsDead => Info.IsDead;

    [ObservableProperty]
    private bool _showRevive;

    [ObservableProperty]
    private string _xText;

    [ObservableProperty]
    private string _yText;

    [ObservableProperty]
    private string _zText;

    [ObservableProperty]
    private string _statusText = "";

    public PlayerRowViewModel(PlayerInfo info, PlayersViewModel owner)
    {
        Info = info;
        _owner = owner;
        _xText = FormatCoord(info.Location.X);
        _yText = FormatCoord(info.Location.Y);
        _zText = FormatCoord(info.Location.Z);
    }

    // Invariant culture on both sides — coordinates are game data, not locale-formatted
    // text, so ',' vs '.' as the decimal separator can't depend on the user's OS locale.
    private static string FormatCoord(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Re-syncs this row from freshly-loaded data after a save/revive/refresh.</summary>
    public void Refresh(PlayerInfo info, bool worldIsHardcore)
    {
        Info = info;
        XText = FormatCoord(info.Location.X);
        YText = FormatCoord(info.Location.Y);
        ZText = FormatCoord(info.Location.Z);
        ShowRevive = worldIsHardcore && info.IsDead;
        StatusText = "";
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(DimensionText));
        OnPropertyChanged(nameof(HealthText));
        OnPropertyChanged(nameof(IsDead));
    }

    [RelayCommand]
    private void SaveLocation()
    {
        if (!double.TryParse(XText, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !double.TryParse(YText, NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
            !double.TryParse(ZText, NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
        {
            StatusText = "Enter valid numbers for X/Y/Z";
            return;
        }
        _owner.SaveLocation(this, x, y, z);
    }

    [RelayCommand]
    private void Revive() => _owner.ReviveRow(this);
}
