using System.Collections.Concurrent;
using WgAutoswitch.Shared;

namespace WgAutoswitch.Service;

public class ServiceState
{
    private readonly object _lock = new();

    public AppConfig Config { get; private set; } = AppConfig.Load(Paths.ConfigFile);

    // Vom Tray gesteuert: gilt nur für die Auto-Logik. Service läuft weiter.
    public bool AutoModeEnabled { get; set; } = true;

    public bool LastAtHome { get; set; }
    public string LastDetectionReason { get; set; } = "Noch nicht ermittelt";
    public DateTime LastChange { get; set; }
    public string LastChangeReason { get; set; } = "";

    // Wird vom MainWorker geschrieben, vom Pipe-Server gelesen → thread-safe.
    public ConcurrentDictionary<string, TunnelStatus> CurrentTunnels { get; } = new();

    public event Action? StateChanged;
    public void NotifyChanged() => StateChanged?.Invoke();

    public void ReloadConfig()
    {
        lock (_lock)
        {
            Config = AppConfig.Load(Paths.ConfigFile);
        }
        NotifyChanged();
    }

    public StatusMessage Snapshot()
    {
        lock (_lock)
        {
            return new StatusMessage(
                ServiceRunning: true,
                AutoModeEnabled: AutoModeEnabled,
                AtHome: LastAtHome,
                LastDetectionReason: LastDetectionReason,
                Tunnels: CurrentTunnels.ToDictionary(kv => kv.Key, kv => kv.Value),
                LastChange: LastChange,
                LastChangeReason: LastChangeReason
            );
        }
    }
}
