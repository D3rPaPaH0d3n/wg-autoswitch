using Tomlyn;
using Tomlyn.Model;

namespace WgAutoswitch.Shared;

public class AppConfig
{
    public GeneralConfig General { get; set; } = new();
    public List<TunnelConfig> Tunnels { get; set; } = new();
    public HomeDetectionConfig HomeDetection { get; set; } = new();

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            var defaultConfig = CreateDefault();
            defaultConfig.Save(path);
            return defaultConfig;
        }

        var toml = File.ReadAllText(path);
        var model = Toml.ToModel(toml);
        return FromModel(model);
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# wg-autoswitch configuration");
        sb.AppendLine("# Edit this file then restart the service.");
        sb.AppendLine();
        sb.AppendLine("[general]");
        sb.AppendLine($"enabled = {General.Enabled.ToString().ToLower()}");
        sb.AppendLine($"check_interval_seconds = {General.CheckIntervalSeconds}");
        sb.AppendLine($"hysteresis_count = {General.HysteresisCount}");
        sb.AppendLine($"min_checks_required = {General.MinChecksRequired}");
        sb.AppendLine();
        foreach (var tunnel in Tunnels)
        {
            sb.AppendLine("[[tunnels]]");
            sb.AppendLine($"name = \"{tunnel.Name}\"");
            sb.AppendLine();
        }
        sb.AppendLine("[home_detection]");
        if (!string.IsNullOrEmpty(HomeDetection.GatewayMac))
            sb.AppendLine($"gateway_mac = \"{HomeDetection.GatewayMac}\"");
        if (!string.IsNullOrEmpty(HomeDetection.Ssid))
            sb.AppendLine($"ssid = \"{HomeDetection.Ssid}\"");
        if (!string.IsNullOrEmpty(HomeDetection.ReachableHost))
        {
            sb.AppendLine($"reachable_host = \"{HomeDetection.ReachableHost}\"");
            sb.AppendLine($"reachable_port = {HomeDetection.ReachablePort}");
        }
        File.WriteAllText(path, sb.ToString());
    }

    private static AppConfig FromModel(TomlTable model)
    {
        var cfg = new AppConfig();
        if (model.TryGetValue("general", out var gen) && gen is TomlTable gt)
        {
            cfg.General.Enabled = gt.TryGetValue("enabled", out var e) && (bool)e;
            cfg.General.CheckIntervalSeconds = gt.TryGetValue("check_interval_seconds", out var ci) ? (int)(long)ci : 10;
            cfg.General.HysteresisCount = gt.TryGetValue("hysteresis_count", out var h) ? (int)(long)h : 2;
            cfg.General.MinChecksRequired = gt.TryGetValue("min_checks_required", out var m) ? (int)(long)m : 2;
        }
        if (model.TryGetValue("tunnels", out var tArr) && tArr is TomlTableArray arr)
        {
            foreach (var t in arr)
                cfg.Tunnels.Add(new TunnelConfig { Name = (string)t["name"] });
        }
        if (model.TryGetValue("home_detection", out var hd) && hd is TomlTable hdt)
        {
            cfg.HomeDetection.GatewayMac = hdt.TryGetValue("gateway_mac", out var gm) ? (string)gm : "";
            cfg.HomeDetection.Ssid = hdt.TryGetValue("ssid", out var s) ? (string)s : "";
            cfg.HomeDetection.ReachableHost = hdt.TryGetValue("reachable_host", out var rh) ? (string)rh : "";
            cfg.HomeDetection.ReachablePort = hdt.TryGetValue("reachable_port", out var rp) ? (int)(long)rp : 0;
        }
        return cfg;
    }

    private static AppConfig CreateDefault() => new()
    {
        General = new GeneralConfig
        {
            Enabled = true,
            CheckIntervalSeconds = 10,
            HysteresisCount = 2,
            MinChecksRequired = 2
        },
        Tunnels = new List<TunnelConfig>
        {
            new() { Name = "home" }
        },
        HomeDetection = new HomeDetectionConfig
        {
            GatewayMac = "",
            Ssid = "",
            ReachableHost = "",
            ReachablePort = 0
        }
    };
}

public class GeneralConfig
{
    public bool Enabled { get; set; } = true;
    public int CheckIntervalSeconds { get; set; } = 10;
    public int HysteresisCount { get; set; } = 2;
    public int MinChecksRequired { get; set; } = 2;
}

public class TunnelConfig
{
    public string Name { get; set; } = "";
}

public class HomeDetectionConfig
{
    public string GatewayMac { get; set; } = "";
    public string Ssid { get; set; } = "";
    public string ReachableHost { get; set; } = "";
    public int ReachablePort { get; set; }
}

public static class Paths
{
    public static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "wg-autoswitch");
    public static string ConfigFile => Path.Combine(ConfigDir, "config.toml");
    public static string LogFile => Path.Combine(ConfigDir, "log.txt");
    public const string PipeName = "wg-autoswitch";
}
