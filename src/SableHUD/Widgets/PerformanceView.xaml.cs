using System.Windows;
using SableHUD.Input;
using SableHUD.Shared;

namespace SableHUD.Widgets;

public partial class PerformanceView : HudPageBase
{
    public override string TabId => "Performance";

    public PerformanceView()
    {
        InitializeComponent();
        Focusable = false;
    }

    public override bool Navigate(NavDir dir) => false;

    public override void Refresh()
    {
        var s = Services.Demo
            ? Services.DemoSystem ?? SystemInfo.SystemSnapshot.Empty
            : Services.Monitor.Current;

        CpuValue.Text = $"{s.CpuPercent:0}";
        CpuRing.Value = s.CpuPercent / 100.0;
        CpuSub.Text = "across all cores";

        if (s.GpuAvailable)
        {
            GpuValue.Text = $"{s.GpuPercent:0}";
            GpuRing.Value = s.GpuPercent / 100.0;
            GpuSub.Text = "3D engine";
        }
        else
        {
            GpuValue.Text = "—";
            GpuRing.Value = 0;
            GpuSub.Text = "no GPU counters";
        }

        RamValue.Text = $"{s.RamPercent:0}";
        RamRing.Value = s.RamPercent / 100.0;
        RamSub.Text = $"{s.RamUsedGb:0.0} / {s.RamTotalGb:0.0} GB";

        if (s.Fps is double fps)
        {
            FpsValue.Text = $"{fps:0}";
            FpsRing.Value = Math.Clamp(fps / 240.0, 0, 1);
            FpsSub.Text = "RTSS live";
        }
        else
        {
            FpsValue.Text = "—";
            FpsRing.Value = 0;
            FpsSub.Text = Services.Settings.RtssFps ? "RTSS not running" : "Turned off";
        }

        GameTitle.Text = string.IsNullOrWhiteSpace(s.FocusTitle) ? "Desktop" : s.FocusTitle;
        GameExe.Text = string.IsNullOrWhiteSpace(s.FocusExe) ? "nothing in focus" : s.FocusExe;
        SessionText.Text = $"Session {Ui.Compact(s.Session)}";
        UptimeText.Text = $"Up {(int)s.SystemUptime.TotalHours}h";
        BigSession.Text = Ui.Compact(s.Session);

        DownValue.Text = $"{s.NetDownMbps:0.0}";
        UpValue.Text = $"{s.NetUpMbps:0.0}";
        NetSpark.Values = s.DownHistory;

        if (s.HasBattery && s.BatteryPercent >= 0)
        {
            BatteryText.Text = $"{s.BatteryPercent}% · {(s.BatteryCharging ? "charging" : "on battery")}";
            BatteryFill.Width = Math.Max(1, 15.0 * s.BatteryPercent / 100.0);
        }
        else
        {
            BatteryText.Text = "No battery";
            BatteryFill.Width = 0;
        }

        GpuMemoryText.Text = s.GpuMemoryGb > 0 ? $"VRAM {s.GpuMemoryGb:0.0} GB" : "VRAM —";
    }
}
