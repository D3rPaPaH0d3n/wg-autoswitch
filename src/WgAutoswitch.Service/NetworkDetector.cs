using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using WgAutoswitch.Shared;

namespace WgAutoswitch.Service;

public class NetworkDetector
{
    private readonly ILogger<NetworkDetector> _log;
    private readonly ServiceState _state;

    public NetworkDetector(ILogger<NetworkDetector> log, ServiceState state)
    {
        _log = log;
        _state = state;
    }

    public record DetectionResult(bool AtHome, string Reason, int Score, int MaxScore);

    public async Task<DetectionResult> DetectAsync(CancellationToken ct)
    {
        var cfg = _state.Config.HomeDetection;
        var min = _state.Config.General.MinChecksRequired;
        var reasons = new List<string>();
        int score = 0, available = 0;

        // 1. Gateway-MAC vergleichen (zuverlässigster Check)
        if (!string.IsNullOrWhiteSpace(cfg.GatewayMac))
        {
            available++;
            var mac = GetDefaultGatewayMac();
            if (mac != null && string.Equals(mac, NormalizeMac(cfg.GatewayMac), StringComparison.OrdinalIgnoreCase))
            {
                score++;
                reasons.Add("Gateway-MAC ✓");
            }
            else
            {
                reasons.Add($"Gateway-MAC ✗ (gefunden: {mac ?? "n/a"})");
            }
        }

        // 2. WLAN-SSID
        if (!string.IsNullOrWhiteSpace(cfg.Ssid))
        {
            available++;
            var ssid = GetCurrentSsid();
            if (string.Equals(ssid, cfg.Ssid, StringComparison.Ordinal))
            {
                score++;
                reasons.Add("SSID ✓");
            }
            else
            {
                reasons.Add($"SSID ✗ (aktuell: {ssid ?? "n/a"})");
            }
        }

        // 3. Reachability eines internen Hosts
        if (!string.IsNullOrWhiteSpace(cfg.ReachableHost) && cfg.ReachablePort > 0)
        {
            available++;
            if (await IsReachableAsync(cfg.ReachableHost, cfg.ReachablePort, TimeSpan.FromMilliseconds(800), ct))
            {
                score++;
                reasons.Add($"{cfg.ReachableHost}:{cfg.ReachablePort} ✓");
            }
            else
            {
                reasons.Add($"{cfg.ReachableHost}:{cfg.ReachablePort} ✗");
            }
        }

        // Mindestens N Checks müssen "zuhause" sagen UND mindestens N Checks müssen überhaupt verfügbar sein.
        // Damit wir nicht "zuhause" raten, wenn wir nichts wissen.
        var atHome = available >= min && score >= min;
        var reason = string.Join(", ", reasons);
        if (available == 0) reason = "Keine Heimerkennungs-Checks konfiguriert";

        return new DetectionResult(atHome, reason, score, available);
    }

    private static string NormalizeMac(string mac) =>
        mac.Replace("-", ":").Replace(" ", "").ToUpperInvariant();

    private string? GetDefaultGatewayMac()
    {
        try
        {
            // Default Gateway IP über aktive Interfaces ermitteln
            IPAddress? gateway = null;
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var gw = ni.GetIPProperties().GatewayAddresses
                    .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork
                                         && !g.Address.Equals(IPAddress.Any));
                if (gw != null) { gateway = gw.Address; break; }
            }
            if (gateway == null) return null;

            // ARP-Tabelle abfragen via "arp -a"
            var psi = new ProcessStartInfo("arp", $"-a {gateway}")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return null;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(2000);

            var match = Regex.Match(output, @"([0-9A-Fa-f]{2}-){5}[0-9A-Fa-f]{2}");
            return match.Success ? NormalizeMac(match.Value) : null;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Gateway-MAC konnte nicht ermittelt werden");
            return null;
        }
    }

    private string? GetCurrentSsid()
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", "wlan show interfaces")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };
            using var p = Process.Start(psi);
            if (p == null) return null;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(2000);

            // SSID erscheint als "SSID                   : MeinWLAN" (sprachabhängig auch "SSID   :")
            // BSSID ausschließen, das ist die MAC vom AP
            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase)) continue;
                if (!trimmed.StartsWith("SSID", StringComparison.OrdinalIgnoreCase)) continue;
                var idx = trimmed.IndexOf(':');
                if (idx < 0) continue;
                return trimmed[(idx + 1)..].Trim();
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "SSID konnte nicht ermittelt werden");
        }
        return null;
    }

    private async Task<bool> IsReachableAsync(string host, int port, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            using var tcp = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            await tcp.ConnectAsync(host, port, cts.Token);
            return tcp.Connected;
        }
        catch
        {
            return false;
        }
    }
}
