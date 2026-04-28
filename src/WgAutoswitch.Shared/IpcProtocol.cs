using System.Text.Json.Serialization;

namespace WgAutoswitch.Shared;

// Status, den der Service per Pipe pusht
public record StatusMessage(
    bool ServiceRunning,
    bool AutoModeEnabled,         // false = User hat per Tray pausiert
    bool AtHome,                  // letzte Detection
    string LastDetectionReason,   // z.B. "Gateway-MAC stimmt überein, Unraid erreichbar"
    Dictionary<string, TunnelStatus> Tunnels,
    DateTime LastChange,
    string LastChangeReason
);

public record TunnelStatus(
    string Name,
    bool Active,        // Windows-Service WireGuardTunnel$<Name> läuft
    string ServiceState // "Running", "Stopped", "NotInstalled" etc.
);

// Kommandos vom Tray an den Service
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(GetStatusCommand), "getStatus")]
[JsonDerivedType(typeof(SetAutoModeCommand), "setAutoMode")]
[JsonDerivedType(typeof(ManualTunnelCommand), "manualTunnel")]
[JsonDerivedType(typeof(ReloadConfigCommand), "reloadConfig")]
public abstract record Command;

public record GetStatusCommand : Command;
public record SetAutoModeCommand(bool Enabled) : Command;
public record ManualTunnelCommand(string TunnelName, bool Activate) : Command;
public record ReloadConfigCommand : Command;

public record CommandResponse(bool Success, string? Error, StatusMessage? Status);
