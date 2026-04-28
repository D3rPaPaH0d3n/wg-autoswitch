using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using WgAutoswitch.Shared;

namespace WgAutoswitch.Tray;

public class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _icon = new();
    private readonly PipeClient _client = new();
    private readonly System.Windows.Forms.Timer _pollTimer;
    private StatusMessage? _last;

    private ToolStripMenuItem _miStatus = null!;
    private ToolStripMenuItem _miPause = null!;
    private ToolStripSeparator _sep1 = null!;
    private ToolStripMenuItem _miReloadCfg = null!;
    private ToolStripMenuItem _miOpenLog = null!;
    private ToolStripMenuItem _miOpenCfg = null!;
    private ToolStripMenuItem _miExit = null!;

    public TrayApp()
    {
        BuildMenu();
        _icon.Text = "wg-autoswitch";
        _icon.Visible = true;
        _icon.DoubleClick += async (_, _) => await RefreshAsync();
        SetIcon(IconState.Unknown);

        _pollTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _pollTimer.Tick += async (_, _) => await RefreshAsync();
        _pollTimer.Start();

        _ = RefreshAsync();
    }

    private void BuildMenu()
    {
        var menu = new ContextMenuStrip();
        _miStatus = new ToolStripMenuItem("Status wird geladen…") { Enabled = false };
        _miPause = new ToolStripMenuItem("Auto-Modus pausieren");
        _miPause.Click += async (_, _) => await TogglePauseAsync();

        _sep1 = new ToolStripSeparator();
        _miReloadCfg = new ToolStripMenuItem("Konfiguration neu laden");
        _miReloadCfg.Click += async (_, _) =>
        {
            var resp = await _client.SendAsync(new ReloadConfigCommand(), CancellationToken.None);
            ShowResult(resp, "Config neu geladen");
        };

        _miOpenLog = new ToolStripMenuItem("Log öffnen");
        _miOpenLog.Click += (_, _) =>
        {
            try { Process.Start("explorer.exe", Paths.LogFile); }
            catch { /* nichts */ }
        };

        _miOpenCfg = new ToolStripMenuItem("Konfiguration öffnen");
        _miOpenCfg.Click += (_, _) =>
        {
            try { Process.Start("notepad.exe", Paths.ConfigFile); }
            catch { /* nichts */ }
        };

        _miExit = new ToolStripMenuItem("Tray beenden");
        _miExit.Click += (_, _) => ExitThread();

        menu.Items.AddRange(new ToolStripItem[]
        {
            _miStatus, _miPause, _sep1,
            _miReloadCfg, _miOpenCfg, _miOpenLog,
            new ToolStripSeparator(),
            _miExit
        });
        _icon.ContextMenuStrip = menu;
    }

    private async Task RefreshAsync()
    {
        var resp = await _client.SendAsync(new GetStatusCommand(), CancellationToken.None);
        if (!resp.Success || resp.Status == null)
        {
            _last = null;
            SetIcon(IconState.Error);
            _icon.Text = $"wg-autoswitch: {resp.Error ?? "Service nicht erreichbar"}";
            _miStatus.Text = "Service nicht erreichbar";
            _miPause.Enabled = false;
            UpdateTunnelMenu(null);
            return;
        }

        _last = resp.Status;
        _miPause.Enabled = true;
        _miPause.Text = resp.Status.AutoModeEnabled ? "Auto-Modus pausieren" : "Auto-Modus aktivieren";

        var iconState = !resp.Status.AutoModeEnabled
            ? IconState.Paused
            : resp.Status.AtHome ? IconState.Home : IconState.Away;
        SetIcon(iconState);

        var tooltip = resp.Status.AutoModeEnabled
            ? (resp.Status.AtHome ? "Zuhause - Tunnel aus" : "Unterwegs - Tunnel an")
            : "Pausiert";
        _icon.Text = $"wg-autoswitch: {tooltip}";
        _miStatus.Text = $"{tooltip} ({resp.Status.LastDetectionReason})";

        UpdateTunnelMenu(resp.Status);
    }

    private void UpdateTunnelMenu(StatusMessage? status)
    {
        // Vorhandene Tunnel-Einträge entfernen (zwischen _sep1 und _miReloadCfg eingehängt)
        var menu = _icon.ContextMenuStrip!;
        for (int i = menu.Items.Count - 1; i >= 0; i--)
        {
            if (menu.Items[i].Tag is "tunnel") menu.Items.RemoveAt(i);
        }
        if (status == null) return;

        int insertAt = menu.Items.IndexOf(_sep1) + 1;
        foreach (var (name, st) in status.Tunnels)
        {
            var label = $"Tunnel \"{name}\": {st.ServiceState}";
            var item = new ToolStripMenuItem(label) { Tag = "tunnel" };
            var activate = new ToolStripMenuItem("Manuell EIN");
            activate.Click += async (_, _) =>
            {
                var r = await _client.SendAsync(new ManualTunnelCommand(name, true), CancellationToken.None);
                ShowResult(r, $"Tunnel {name} eingeschaltet");
            };
            var deactivate = new ToolStripMenuItem("Manuell AUS");
            deactivate.Click += async (_, _) =>
            {
                var r = await _client.SendAsync(new ManualTunnelCommand(name, false), CancellationToken.None);
                ShowResult(r, $"Tunnel {name} ausgeschaltet");
            };
            item.DropDownItems.Add(activate);
            item.DropDownItems.Add(deactivate);
            menu.Items.Insert(insertAt++, item);
        }
        menu.Items.Insert(insertAt, new ToolStripSeparator { Tag = "tunnel" });
    }

    private async Task TogglePauseAsync()
    {
        if (_last == null) return;
        var newState = !_last.AutoModeEnabled;
        var resp = await _client.SendAsync(new SetAutoModeCommand(newState), CancellationToken.None);
        ShowResult(resp, newState ? "Auto-Modus aktiv" : "Auto-Modus pausiert");
    }

    private void ShowResult(CommandResponse resp, string okText)
    {
        if (resp.Success)
            _icon.ShowBalloonTip(2000, "wg-autoswitch", okText, ToolTipIcon.Info);
        else
            _icon.ShowBalloonTip(3000, "wg-autoswitch - Fehler", resp.Error ?? "Unbekannt", ToolTipIcon.Warning);
        _ = RefreshAsync();
    }

    // Generiert farbige Tray-Icons live, kein Asset-Pflegen nötig
    private enum IconState { Home, Away, Paused, Error, Unknown }
    private void SetIcon(IconState s)
    {
        var color = s switch
        {
            IconState.Home => Color.FromArgb(76, 175, 80),    // grün
            IconState.Away => Color.FromArgb(33, 150, 243),   // blau
            IconState.Paused => Color.FromArgb(158, 158, 158),// grau
            IconState.Error => Color.FromArgb(244, 67, 54),   // rot
            _ => Color.FromArgb(120, 120, 120),
        };
        _icon.Icon?.Dispose();
        _icon.Icon = MakeCircleIcon(color);
    }

    private static Icon MakeCircleIcon(Color color)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 2, 2, size - 4, size - 4);
            using var pen = new Pen(Color.White, 2);
            g.DrawEllipse(pen, 2, 2, size - 4, size - 4);
        }
        IntPtr hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _icon.Visible = false;
            _icon.Dispose();
            _pollTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
