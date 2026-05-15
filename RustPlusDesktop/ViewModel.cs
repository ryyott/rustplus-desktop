using RustPlusDesk.Models;
using RustPlusDesk.Services;
using RustPlusDesk.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace RustPlusDesk.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    // ─── Day/Night cycle learning + extrapolation ────────────────────────────
    // Every 1s we extrapolate the in-game clock locally from the last server reading,
    // using exponentially-smoothed observations of how fast time actually advances on
    // this specific server. Result: a smooth ticking clock and an accurate "until day/
    // until night" countdown even between status polls (which arrive every few seconds).
    private readonly System.Windows.Threading.DispatcherTimer _clockTimer;

    public MainViewModel()
    {
        _clockTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += (_, __) => TickClock();
        _clockTimer.Start();
    }

    private DateTime? _lastStatusRealTime;
    private double? _lastStatusGameTime;
    private string? _lastConnectedServer;

    // Game-hours per real-minute. Defaults to vanilla Rust (Day ~50m, Night ~10m, each covering 12 game-hours).
    private double _observedDaySpeed = 12.0 / 50.0;
    private double _observedNightSpeed = 12.0 / 10.0;

    private void TickClock()
    {
        if (!_lastStatusRealTime.HasValue || !_lastStatusGameTime.HasValue) return;

        double elapsedRealMins = (DateTime.UtcNow - _lastStatusRealTime.Value).TotalMinutes;
        double currentHours = _lastStatusGameTime.Value;

        double speed = (currentHours >= 8 && currentHours < 20) ? _observedDaySpeed : _observedNightSpeed;
        double extrapolatedHours = (currentHours + (elapsedRealMins * speed)) % 24;
        if (extrapolatedHours < 0) extrapolatedHours += 24;

        UpdateDisplayProperties(extrapolatedHours);
    }

    private void UpdateDisplayProperties(double hours)
    {
        int h = (int)Math.Floor(hours);
        int m = (int)Math.Floor((hours - h) * 60);
        string newTime = $"{h:00}:{m:00}";

        // Update the displayed string directly (bypass the setter so we don't re-learn from our own
        // extrapolation — only the actual server response should drive the learning step).
        if (_serverTime != newTime && _serverTime != "-" && _serverTime != "–")
        {
            _serverTime = newTime;
            OnPropertyChanged(nameof(ServerTime));
        }

        if (hours >= 8 && hours < 20)
        {
            IsDay = true;
            double remainingGameHours = 20 - hours;
            double remainingRealMins = remainingGameHours / _observedDaySpeed;
            TimeUntilNextPhase = FormatDuration(remainingRealMins) + " until night";
        }
        else
        {
            IsDay = false;
            double remainingGameHours = (hours >= 20) ? (24 - hours) + 8 : 8 - hours;
            double remainingRealMins = remainingGameHours / _observedNightSpeed;
            TimeUntilNextPhase = FormatDuration(remainingRealMins) + " until day";
        }
    }

    private void UpdateInGameTimeProperties(string timeStr)
    {
        if (string.IsNullOrWhiteSpace(timeStr) || timeStr == "-" || timeStr == "–")
        {
            TimeUntilNextPhase = "";
            return;
        }

        // Reset baseline + adopt the new server's learned speeds when switching servers.
        string currentServer = Selected?.Host ?? "";
        if (currentServer != _lastConnectedServer)
        {
            _lastConnectedServer = currentServer;
            _lastStatusRealTime = null;
            _lastStatusGameTime = null;

            if (Selected != null)
            {
                _observedDaySpeed = Selected.LearnedDaySpeed > 0 ? Selected.LearnedDaySpeed : (12.0 / 50.0);
                _observedNightSpeed = Selected.LearnedNightSpeed > 0 ? Selected.LearnedNightSpeed : (12.0 / 10.0);
            }
            else
            {
                _observedDaySpeed = 12.0 / 50.0;
                _observedNightSpeed = 12.0 / 10.0;
            }
        }

        try
        {
            if (!TimeSpan.TryParse(timeStr, out var ts)) return;

            double currentHours = ts.TotalHours;
            DateTime now = DateTime.UtcNow;

            if (_lastStatusRealTime.HasValue && _lastStatusGameTime.HasValue)
            {
                double deltaRealMins = (now - _lastStatusRealTime.Value).TotalMinutes;
                if (deltaRealMins > 0.05)
                {
                    double deltaGameHours = currentHours - _lastStatusGameTime.Value;
                    if (deltaGameHours < -12) deltaGameHours += 24; // midnight wrap

                    // Sanity: positive and < 2 game-hours per poll keeps manual time-sets / huge jitter out.
                    if (deltaGameHours > 0 && deltaGameHours < 2)
                    {
                        double speed = deltaGameHours / deltaRealMins;
                        if (currentHours >= 8 && currentHours < 20)
                        {
                            _observedDaySpeed = (_observedDaySpeed * 0.95) + (speed * 0.05);
                            if (Selected != null) Selected.LearnedDaySpeed = _observedDaySpeed;
                        }
                        else
                        {
                            _observedNightSpeed = (_observedNightSpeed * 0.95) + (speed * 0.05);
                            if (Selected != null) Selected.LearnedNightSpeed = _observedNightSpeed;
                        }
                    }
                }
            }

            _lastStatusRealTime = now;
            _lastStatusGameTime = currentHours;

            UpdateDisplayProperties(currentHours);
        }
        catch { }
    }

    private static string FormatDuration(double realMinutes)
    {
        if (realMinutes < 0) realMinutes = 0;
        int m = (int)Math.Floor(realMinutes);
        int s = (int)Math.Round((realMinutes - m) * 60);
        if (s == 60) { m++; s = 0; }
        return m > 0 ? $"{m}m {s}s" : $"{s}s";
    }

    private bool _isDay;
    public bool IsDay { get => _isDay; set { _isDay = value; OnPropertyChanged(); } }

    private string _timeUntilNextPhase = "";
    public string TimeUntilNextPhase
    {
        get => _timeUntilNextPhase;
        set { _timeUntilNextPhase = value; OnPropertyChanged(); }
    }

    private int _iconsTotal;
    private int _iconsDownloaded;

    public int IconsTotal
    {
        get => _iconsTotal;
        set { _iconsTotal = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDownloadingIcons)); OnPropertyChanged(nameof(IconDownloadProgress)); }
    }

    public int IconsDownloaded
    {
        get => _iconsDownloaded;
        set { _iconsDownloaded = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDownloadingIcons)); OnPropertyChanged(nameof(IconDownloadProgress)); }
    }

    public bool IsDownloadingIcons => _iconsTotal > 0 && _iconsDownloaded < _iconsTotal;
    public double IconDownloadProgress => _iconsTotal > 0 ? (double)_iconsDownloaded / _iconsTotal * 100 : 0;

    public ObservableCollection<ServerProfile> Servers { get; } = new();

    private bool _isBusy;
    private bool _isPairingRunning;
    public bool IsPairingRunning
    {
        get => _isPairingRunning;
        set { _isPairingRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanStartPairing)); }
    }

    public bool CanStartPairing => !_isPairingRunning && !IsBusy;

    private bool _isTrackingActive;
    public bool IsTrackingActive
    {
        get => _isTrackingActive;
        set { _isTrackingActive = value; OnPropertyChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); }
    }

    private string _busyText = "Bitte warten …";
    public string BusyText
    {
        get => _busyText;
        set { _busyText = value; OnPropertyChanged(); }
    }

    private string _steamId64 = "";
    public string SteamId64
    {
        get => _steamId64;
        set { _steamId64 = value; OnPropertyChanged(); }
    }

    private ServerProfile? _selected;
    public ServerProfile? Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value; 
            OnPropertyChanged();                   // "Selected"
            OnPropertyChanged(nameof(CurrentDevices));
        }
    }

    public sealed class StorageSnapshot
    {
        public bool IsToolCupboard { get; init; }
        public int? UpkeepSeconds { get; init; }        // nur TC
        public DateTime SnapshotUtc { get; init; } = DateTime.UtcNow;
        public List<StorageItemVM> Items { get; init; } = new();
    }

    public sealed class StorageItemVM : INotifyPropertyChanged
    {
        public int ItemId { get; init; }
        public string? ShortName { get; init; }
        public int Amount { get; init; }
        public int? MaxStack { get; init; }

        public string Display => MainWindow.ResolveItemName(ItemId, ShortName);
        public ImageSource? Icon => MainWindow.ResolveItemIcon(ItemId, ShortName, 32);
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private string _serverPlayers = "-/-";
    public string ServerPlayers { get => _serverPlayers; set { _serverPlayers = value; OnPropertyChanged(); } }

    private string _serverQueue = "-";
    public string ServerQueue { get => _serverQueue; set { _serverQueue = value; OnPropertyChanged(); } }

    private string _serverTime = "-";
    public string ServerTime
    {
        get => _serverTime;
        set
        {
            _serverTime = value;
            OnPropertyChanged();
            UpdateInGameTimeProperties(value);
        }
    }

    private string _serverWipe = "-";
    public string ServerWipe { get => _serverWipe; set { _serverWipe = value; OnPropertyChanged(); } }

    // NEU: Abgeleitete Binding-Quelle für die Liste
    public ObservableCollection<SmartDevice>? CurrentDevices
        => Selected?.Devices;

    // Auswahl im UI
    private SmartDevice? _selectedDevice;
    public SmartDevice? SelectedDevice
    {
        get => _selectedDevice;
        set { _selectedDevice = value; OnPropertyChanged(); }
    }

    public void AddServer(ServerProfile p) => Servers.Add(p);

    public void Load()
    {
        Servers.Clear();
        foreach (var p in StorageService.LoadProfiles())
        {
            p.Devices ??= new ObservableCollection<SmartDevice>(); // niemals null
            p.CameraIds ??= new ObservableCollection<string>();      // NEU: ebenso niemals null
            p.IsConnected = false; // Reset connection state on load
            Servers.Add(p);
        }

        // WICHTIG: Vorauswahl, sonst bleibt CurrentDevices=null
        if (Servers.Count > 0 && Selected == null)
            Selected = Servers[0];
    }


    public void NotifyCamerasChanged() => OnPropertyChanged(nameof(Selected));
    public void Save() => StorageService.SaveProfiles(Servers);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // HILFSMETHODE: UI anstupsen, wenn Devices in-place aktualisiert wurden
    public void NotifyDevicesChanged()
        => OnPropertyChanged(nameof(CurrentDevices));
}
