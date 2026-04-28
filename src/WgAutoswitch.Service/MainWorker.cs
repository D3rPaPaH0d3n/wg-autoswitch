using System.Net.NetworkInformation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WgAutoswitch.Shared;

namespace WgAutoswitch.Service;

public class MainWorker : BackgroundService
{
    private readonly ILogger<MainWorker> _log;
    private readonly ServiceState _state;
    private readonly NetworkDetector _detector;
    private readonly TunnelController _controller;

    // Hysterese: zähle aufeinanderfolgende identische Detection-Ergebnisse
    private bool? _lastSeen;
    private int _consecutiveCount;
    // null = wir haben noch nie aktiv geschaltet, also beim ersten stabilen
    // Resultat zwingend einmal anwenden (sonst bleibt der Tunnel beim Erst-
    // Start "unterwegs" hängen, falls er gerade aus ist).
    private bool? _appliedAtHome;

    public MainWorker(ILogger<MainWorker> log, ServiceState state,
                      NetworkDetector detector, TunnelController controller)
    {
        _log = log;
        _state = state;
        _detector = detector;
        _controller = controller;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("wg-autoswitch gestartet");

        // Sofort reagieren bei Netzwerk-Änderungen (kein dummes Polling auf alles)
        NetworkChange.NetworkAvailabilityChanged += (s, e) => TriggerImmediate();
        NetworkChange.NetworkAddressChanged += (s, e) => TriggerImmediate();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Fehler im Hauptloop");
            }

            try
            {
                var interval = TimeSpan.FromSeconds(Math.Max(2, _state.Config.General.CheckIntervalSeconds));
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _wakeToken.Token);
                await Task.Delay(interval, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Entweder Stop oder Wake-Up
                if (stoppingToken.IsCancellationRequested) break;
                var old = _wakeToken;
                _wakeToken = new CancellationTokenSource();
                old.Dispose();
            }
        }
    }

    private CancellationTokenSource _wakeToken = new();
    private void TriggerImmediate()
    {
        // Sofort reagieren - wie vom User gewünscht
        try { _wakeToken.Cancel(); } catch { }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        // Tunnel-Status immer aktualisieren - auch wenn Auto-Modus aus ist,
        // damit das Tray etwas anzeigen kann
        foreach (var tunnel in _state.Config.Tunnels)
        {
            _state.CurrentTunnels[tunnel.Name] = _controller.GetStatus(tunnel.Name);
        }

        if (!_state.Config.General.Enabled || !_state.AutoModeEnabled)
        {
            _state.LastDetectionReason = !_state.Config.General.Enabled
                ? "Per Konfiguration deaktiviert"
                : "Per Tray pausiert";
            _state.NotifyChanged();
            return;
        }

        var detection = await _detector.DetectAsync(ct);
        _state.LastAtHome = detection.AtHome;
        _state.LastDetectionReason = detection.Reason;

        // Hysterese
        if (_lastSeen == detection.AtHome)
        {
            _consecutiveCount++;
        }
        else
        {
            _lastSeen = detection.AtHome;
            _consecutiveCount = 1;
        }

        var threshold = Math.Max(1, _state.Config.General.HysteresisCount);
        var stable = _consecutiveCount >= threshold;

        if (stable && detection.AtHome != _appliedAtHome)
        {
            // Aktion: zuhause -> alle Tunnel AUS, unterwegs -> alle Tunnel AN
            var shouldBeActive = !detection.AtHome;
            _log.LogInformation("Wechsle Tunnel-Zustand: AtHome={AtHome}, ShouldBeActive={Active}. Grund: {Reason}",
                                detection.AtHome, shouldBeActive, detection.Reason);

            foreach (var tunnel in _state.Config.Tunnels)
            {
                await _controller.SetActiveAsync(tunnel.Name, shouldBeActive, ct);
                _state.CurrentTunnels[tunnel.Name] = _controller.GetStatus(tunnel.Name);
            }

            _appliedAtHome = detection.AtHome;
            _state.LastChange = DateTime.Now;
            _state.LastChangeReason = detection.AtHome
                ? $"Heimnetz erkannt → Tunnel AUS ({detection.Reason})"
                : $"Heimnetz verlassen → Tunnel AN ({detection.Reason})";
        }

        _state.NotifyChanged();
    }
}
