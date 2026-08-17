using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MapExplorer.Core.Players;

namespace MapExplorer.App.ViewModels;

public partial class PlayersViewModel : ObservableObject
{
    private string? _worldPath;

    // Guards the IsHardcore setter below from writing back to level.dat when the
    // property is being assigned *from* a disk read (Refresh) rather than from the
    // user flipping the ToggleSwitch — otherwise every refresh would re-save it.
    private bool _suppressHardcoreWrite;

    public ObservableCollection<PlayerRowViewModel> Players { get; } = [];

    [ObservableProperty]
    private bool _isHardcore;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _hasWorld;

    public void LoadWorld(string worldPath)
    {
        _worldPath = worldPath;
        HasWorld = true;
        Refresh();
    }

    public void Clear()
    {
        _worldPath = null;
        HasWorld = false;
        Players.Clear();
        StatusText = "";
    }

    [RelayCommand]
    private void Refresh()
    {
        if (_worldPath is null) return;

        _suppressHardcoreWrite = true;
        try
        {
            IsHardcore = PlayerDataStore.GetHardcore(_worldPath);
        }
        catch
        {
            // level.dat missing/corrupt — leave the last-known hardcore state alone;
            // the player list load below will report the real problem if there is one.
        }
        finally
        {
            _suppressHardcoreWrite = false;
        }

        List<PlayerInfo> infos;
        try
        {
            infos = PlayerDataStore.LoadPlayers(_worldPath);
        }
        catch (Exception ex)
        {
            StatusText = $"Couldn't read player data: {ex.Message}";
            return;
        }

        // Reuse existing row VMs by player id so a refresh triggered by one row's save
        // doesn't clobber in-progress (unsaved) edits another row's textboxes hold.
        var existingById = Players.ToDictionary(p => p.Info.Id);
        Players.Clear();
        foreach (var info in infos)
        {
            if (!existingById.TryGetValue(info.Id, out var row))
            {
                row = new PlayerRowViewModel(info, this);
            }
            row.Refresh(info, IsHardcore);
            Players.Add(row);
        }

        StatusText = Players.Count == 0 ? "No players found in this world yet" : "";
    }

    // Fires for every IsHardcore assignment, including the one Refresh() does when
    // reloading from disk — _suppressHardcoreWrite is what tells those apart from an
    // actual user toggle of the switch.
    partial void OnIsHardcoreChanged(bool value)
    {
        if (_suppressHardcoreWrite || _worldPath is null) return;

        try
        {
            PlayerDataStore.SetHardcore(_worldPath, value);
            StatusText = $"Hardcore mode {(value ? "enabled" : "disabled")}";
            Refresh();
        }
        catch (Exception ex)
        {
            StatusText = $"Couldn't change hardcore mode: {ex.Message}";
            _suppressHardcoreWrite = true;
            IsHardcore = !value;
            _suppressHardcoreWrite = false;
        }
    }

    public void SaveLocation(PlayerRowViewModel row, double x, double y, double z)
    {
        try
        {
            PlayerDataStore.SetLocation(row.Info, x, y, z);
            Refresh();
            var saved = Players.FirstOrDefault(p => p.Info.Id == row.Info.Id);
            if (saved is not null) saved.StatusText = "Location saved";
        }
        catch (Exception ex)
        {
            row.StatusText = $"Save failed: {ex.Message}";
        }
    }

    public void ReviveRow(PlayerRowViewModel row)
    {
        try
        {
            PlayerDataStore.Revive(row.Info);
            Refresh();
            var revived = Players.FirstOrDefault(p => p.Info.Id == row.Info.Id);
            if (revived is not null) revived.StatusText = "Revived";
        }
        catch (Exception ex)
        {
            row.StatusText = $"Revive failed: {ex.Message}";
        }
    }
}
