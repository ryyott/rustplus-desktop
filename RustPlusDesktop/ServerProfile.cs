using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RustPlusDesk.Models;


public class ServerProfile : INotifyPropertyChanged
{
    private string _name = "";
    public string Name
    {
        get => _name;
        set { _name = value; OnProp(); }
    }

    private string _description = "";
    public string Description
    {
        get => _description;
        set { _description = value; OnProp(); }
    }

    public string Host { get; set; } = "";
    public int Port { get; set; } = 28082;
    public string SteamId64 { get; set; } = "";
    public string PlayerToken { get; set; } = "";

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        set { _isConnected = value; OnProp(); }
    }

    public bool UseFacepunchProxy { get; set; } = false;

    public ObservableCollection<SmartDevice> Devices { get; set; } = new();
    public ObservableCollection<string> CameraIds { get; set; } = new();

    // Learned in-game time progression rates, expressed in game-hours per real-minute.
    // Defaults match vanilla Rust (Day ~50 real-min for 12 game-hr, Night ~10 real-min for 12 game-hr).
    // MainViewModel updates these via an exponential moving average as it observes the server time
    // advance between polls, and StorageService persists them so they survive restarts.
    public double LearnedDaySpeed { get; set; } = 12.0 / 50.0;
    public double LearnedNightSpeed { get; set; } = 12.0 / 10.0;

    // ─── Chat command configuration (per-server) ────────────────────────────
    // Default-true so existing users get !pop/!time/!cargo/!leader without flipping anything.
    private bool _chatCommandsEnabled = true;
    public bool ChatCommandsEnabled
    {
        get => _chatCommandsEnabled;
        set { _chatCommandsEnabled = value; OnProp(); }
    }

    // The command word (without the leading "!"). CmdPromote defaults to "leader" to preserve the
    // fork's existing hardcoded !leader auto-promote behaviour. Rename via JSON to taste.
    public string CmdPop     { get; set; } = "pop";
    public string CmdTime    { get; set; } = "time";
    public string CmdPromote { get; set; } = "leader";
    public string CmdCargo   { get; set; } = "cargo";
    public string CmdSwitch1 { get; set; } = "switch1";
    public string CmdSwitch2 { get; set; } = "switch2";

    // Smart-switch EntityIds to toggle when CmdSwitch1/2 fires. null = unbound (default).
    // No UI for these yet — edit profiles.json to bind, or wire up via a follow-up Commands panel.
    public uint? BoundSwitchId1 { get; set; }
    public uint? BoundSwitchId2 { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnProp([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

