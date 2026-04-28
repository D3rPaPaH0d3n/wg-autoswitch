using System.Diagnostics;
using System.ServiceProcess;
using Microsoft.Extensions.Logging;
using WgAutoswitch.Shared;

namespace WgAutoswitch.Service;

public class TunnelController
{
    private readonly ILogger<TunnelController> _log;

    public TunnelController(ILogger<TunnelController> log)
    {
        _log = log;
    }

    private static string ServiceNameFor(string tunnel) => $"WireGuardTunnel${tunnel}";

    public TunnelStatus GetStatus(string tunnelName)
    {
        var svcName = ServiceNameFor(tunnelName);
        try
        {
            using var sc = new ServiceController(svcName);
            // Refresh erzwingt einen Lookup
            sc.Refresh();
            var status = sc.Status switch
            {
                ServiceControllerStatus.Running => "Running",
                ServiceControllerStatus.Stopped => "Stopped",
                ServiceControllerStatus.StartPending => "Starting",
                ServiceControllerStatus.StopPending => "Stopping",
                _ => sc.Status.ToString()
            };
            return new TunnelStatus(tunnelName, sc.Status == ServiceControllerStatus.Running, status);
        }
        catch (InvalidOperationException)
        {
            return new TunnelStatus(tunnelName, false, "NotInstalled");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Tunnel-Status für {Tunnel} fehlgeschlagen", tunnelName);
            return new TunnelStatus(tunnelName, false, "Error");
        }
    }

    public async Task<bool> SetActiveAsync(string tunnelName, bool activate, CancellationToken ct)
    {
        var svcName = ServiceNameFor(tunnelName);
        try
        {
            using var sc = new ServiceController(svcName);
            if (activate)
            {
                if (sc.Status == ServiceControllerStatus.Running) return true;
                sc.Start();
                await WaitForStatusAsync(sc, ServiceControllerStatus.Running, ct);
            }
            else
            {
                if (sc.Status == ServiceControllerStatus.Stopped) return true;
                sc.Stop();
                await WaitForStatusAsync(sc, ServiceControllerStatus.Stopped, ct);
            }
            return true;
        }
        catch (InvalidOperationException)
        {
            // Tunnel-Service existiert nicht (z.B. Tunnel wurde noch nicht in WireGuard angelegt)
            _log.LogWarning("Tunnel-Service {Service} existiert nicht. Tunnel zuerst in WireGuard importieren.", svcName);
            return false;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Tunnel {Tunnel} konnte nicht auf Active={Active} gesetzt werden", tunnelName, activate);
            return false;
        }
    }

    private static async Task WaitForStatusAsync(ServiceController sc, ServiceControllerStatus target, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            sc.Refresh();
            if (sc.Status == target) return;
            await Task.Delay(250, ct);
        }
    }
}
