using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SableHUD.Core;

namespace SableHUD.Shared;

public static class Hud
{
    public static Action<string?>? ShowRequested;
    public static Action? HideRequested;
    public static Action? HideImmediateRequested;
    public static Action? ToggleRequested;
    public static Action? MiniRequested;
    public static Action? SettingsRequested;
    public static Action<string?>? SettingsSectionRequested;
    public static Action<string>? ToastRequested;
    public static Func<bool>? IsVisible;

    public static bool Visible => IsVisible?.Invoke() ?? false;
    public static void Show(string? tab = null) => ShowRequested?.Invoke(tab);
    public static void Hide() => HideRequested?.Invoke();
    public static void HideImmediate() => HideImmediateRequested?.Invoke();
    public static void Toggle() => ToggleRequested?.Invoke();
    public static void ToggleMini() => MiniRequested?.Invoke();
    public static void OpenSettings(string? section = null)
    {
        if (section is null) SettingsRequested?.Invoke();
        else SettingsSectionRequested?.Invoke(section);
    }
    public static void Toast(string text) => ToastRequested?.Invoke(text);
}

public static class Ui
{
    public static Geometry Geo(string key)
    {
        if (Application.Current?.Resources[key] is Geometry g) return g;
        return Geometry.Empty;
    }

    public static string Time(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
    }

    public static string Compact(TimeSpan t)
    {
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes:00}m";
        return $"{t.Minutes}m {t.Seconds:00}s";
    }

    public static string Percent(double value) => $"{Math.Round(value):0}";

    public static string Gb(double value) => value >= 9.95 ? $"{value:0.0}" : $"{value:0.0}";

    private static ImageSource? _demoArt;

    public static ImageSource DemoArt()
    {
        if (_demoArt is not null) return _demoArt;
        const int size = 480;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var bg = new RadialGradientBrush(Color.FromRgb(58, 58, 66), Color.FromRgb(9, 9, 11))
            {
                Center = new Point(0.35, 0.28),
                GradientOrigin = new Point(0.28, 0.2),
                RadiusX = 1.05,
                RadiusY = 1.05,
            };
            dc.DrawRectangle(bg, null, new Rect(0, 0, size, size));

            var pen = new Pen(new SolidColorBrush(Color.FromArgb(215, 245, 245, 248)), 2.4);
            dc.DrawEllipse(null, pen, new Point(size * 0.62, size * 0.44), size * 0.27, size * 0.27);
            var thin = new Pen(new SolidColorBrush(Color.FromArgb(90, 245, 245, 248)), 1.1);
            dc.DrawEllipse(null, thin, new Point(size * 0.62, size * 0.44), size * 0.36, size * 0.36);
            dc.DrawEllipse(null, thin, new Point(size * 0.62, size * 0.44), size * 0.43, size * 0.43);

            var dot = new SolidColorBrush(Color.FromArgb(235, 245, 245, 248));
            dc.DrawEllipse(dot, null, new Point(size * 0.62 + size * 0.27, size * 0.44), 5, 5);

            var bar = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255));
            dc.DrawRoundedRectangle(bar, null, new Rect(size * 0.1, size * 0.78, size * 0.34, 8), 4, 4);
            dc.DrawRoundedRectangle(bar, null, new Rect(size * 0.1, size * 0.84, size * 0.22, 8), 4, 4);
        }
        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        _demoArt = bmp;
        return bmp;
    }
}

public static class Services
{
    public static AppSettings Settings { get; set; } = new();
    public static Media.MediaCoordinator Media { get; set; } = null!;
    public static SystemInfo.SystemMonitor Monitor { get; set; } = null!;
    public static SystemInfo.AudioService Audio { get; set; } = new();
    public static Media.CoverService Cover { get; set; } = new();
    public static Input.XInputService Pad { get; set; } = null!;
    public static Input.ControllerRouter Router { get; set; } = null!;
    public static Core.HotkeyService Hotkeys { get; set; } = new();
    public static bool Demo { get; set; }
    public static Media.MediaSnapshot? DemoMedia { get; set; }
    public static SystemInfo.SystemSnapshot? DemoSystem { get; set; }
    public static SystemInfo.AudioSnapshot? DemoAudio { get; set; }
    public static Action? SaveSettings { get; set; }

    public static void Save()
    {
        Core.SettingsStore.Save(Settings);
        SaveSettings?.Invoke();
    }
}
