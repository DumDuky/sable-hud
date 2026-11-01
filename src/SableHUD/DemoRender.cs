using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SableHUD.Core;
using SableHUD.Input;
using SableHUD.Media;
using SableHUD.Shared;
using SableHUD.SystemInfo;
using SableHUD.Windows;
using SableHUD.Widgets;

namespace SableHUD;

/// <summary>
/// Renders the real XAML offscreen with representative data so the design can be
/// reviewed (and regression-checked) without a live game or media session.
/// </summary>
public static class DemoRender
{
    public static void Run(string[] args)
    {
        var outDir = ArgValue(args, "--render") ?? Path.Combine(Environment.CurrentDirectory, "docs", "screenshots");
        outDir = Path.GetFullPath(outDir);
        Directory.CreateDirectory(outDir);

        Services.Settings = new AppSettings
        {
            Theme = "Noir",
            Scale = "Comfortable",
            DefaultTab = "Music",
            ShowMusic = true,
            ShowPerformance = true,
            ShowAudio = true,
            ShowTools = true,
            ShowMiniPlayer = true,
            FirstRun = false,
        };
        ThemeManager.Apply("Noir");

        Services.Media = new MediaCoordinator(new LocalPlayer(), 0.8f);
        Services.Monitor = new SystemMonitor(demo: true);
        Services.Pad = new XInputService();
        Services.Router = new ControllerRouter(Services.Pad, Services.Settings);
        Services.Demo = true;
        Services.DemoMedia = BuildMedia();
        Services.DemoSystem = BuildSystem();
        Services.DemoAudio = BuildAudio();

        var pages = new IHudPage[]
        {
            new MusicView(),
            new PerformanceView(),
            new AudioView(),
            new ToolsView(),
        };

        var overlay = new OverlayWindow(pages, "Music")
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
        };
        overlay.Show();

        var settings = new SettingsWindow
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
        };

        var mini = new MiniPlayerWindow
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
        };
        mini.Show();
        mini.PrepareRender();

        var toast = new ToastWindow
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
        };
        toast.Show();

        var files = new List<string>();

        foreach (var tab in new[] { "Music", "Performance", "Audio", "Tools" })
        {
            overlay.SelectPage(tab, activate: false);
            overlay.CurrentPage?.Activate();
            overlay.CurrentPage?.Refresh();
            overlay.PrepareRender();
            Pump();
            var file = Path.Combine(outDir, $"hud-{tab.ToLowerInvariant()}.png");
            Save(overlay.Root, file, light: false);
            files.Add(Path.GetFileName(file));
        }

        // mini + toast use the demo track
        mini.Refresh();
        mini.PrepareRender();
        Pump();
        var miniFile = Path.Combine(outDir, "mini-player.png");
        Save(mini.Root, miniFile, light: false);
        files.Add(Path.GetFileName(miniFile));

        toast.ShowTrack(Services.DemoMedia!);
        toast.Left = -20000;
        toast.Top = -20000;
        toast.PrepareRender();
        Pump();
        var toastFile = Path.Combine(outDir, "toast.png");
        Save(toast.ToastRoot, toastFile, light: false);
        files.Add(Path.GetFileName(toastFile));

        foreach (var section in new[] { "General", "Widgets", "Appearance", "About" })
        {
            settings.Open(section);
            Pump();
            var file = Path.Combine(outDir, $"settings-{section.ToLowerInvariant()}.png");
            Save(settings.SettingsRoot, file, light: false);
            files.Add(Path.GetFileName(file));
        }

        // Light theme pass
        ThemeManager.Apply("Ivory");
        settings.Open("Appearance");
        Pump();
        var ivorySettings = Path.Combine(outDir, "settings-appearance-ivory.png");
        Save(settings.SettingsRoot, ivorySettings, light: true);
        files.Add(Path.GetFileName(ivorySettings));

        overlay.SelectPage("Music", activate: false);
        overlay.CurrentPage?.Activate();
        overlay.CurrentPage?.Refresh();
        overlay.PrepareRender();
        Pump();
        var ivoryMusic = Path.Combine(outDir, "hud-music-ivory.png");
        Save(overlay.Root, ivoryMusic, light: true);
        files.Add(Path.GetFileName(ivoryMusic));

        ThemeManager.Apply("Noir");

        var manifest = new
        {
            app = "SableHUD",
            generated = DateTimeOffset.Now,
            note = "Design render with representative data (demo track / synthetic telemetry).",
            width = 940,
            height = 404,
            files,
        };
        File.WriteAllText(Path.Combine(outDir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        Log.Info($"render: wrote {files.Count} images to {outDir}");

        settings.AllowClose();
        settings.Close();
        overlay.Close();
        mini.Close();
        toast.Close();
    }

    private static MediaSnapshot BuildMedia() => new(
        MediaKind.System,
        "LiMusic",
        "Midnight City",
        "M83",
        "Hurry Up, We're Dreaming",
        Ui.DemoArt(),
        TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(37),
        TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(3),
        true,
        true,
        true,
        true,
        DateTimeOffset.Now);

    private static SystemSnapshot BuildSystem()
    {
        var down = Enumerable.Range(0, 60)
            .Select(i => 1.8 + Math.Sin(i / 3.4) * 1.4 + Math.Sin(i / 1.1) * 0.5)
            .Select(v => Math.Max(0.05, v))
            .ToArray();
        var up = Enumerable.Range(0, 60)
            .Select(i => 0.35 + Math.Sin(i / 2.6) * 0.18)
            .Select(v => Math.Max(0.01, v))
            .ToArray();

        return new SystemSnapshot(
            38,
            71,
            true,
            6.4,
            46,
            14.9,
            32,
            down[^1],
            up[^1],
            down,
            up,
            116.8,
            "ELDEN RING",
            "eldenring.exe",
            TimeSpan.FromMinutes(83),
            TimeSpan.FromHours(9),
            84,
            false,
            true);
    }

    private static AudioSnapshot BuildAudio() => new(
        0.62f,
        false,
        true,
        false,
        "Roku TV",
        true,
        new[]
        {
            new AudioDeviceInfo("dev-tv", "Roku TV", true),
            new AudioDeviceInfo("dev-headset", "G435 Wireless Headset", false),
            new AudioDeviceInfo("dev-yeti", "Speakers (Yeti Classic)", false),
        },
        new[]
        {
            new AudioSessionInfo(1001, "Discord", 0.72f, false, "dev-headset", "G435 Wireless Headset"),
            new AudioSessionInfo(1002, "ELDEN RING", 0.55f, false, "dev-tv", "Roku TV"),
            new AudioSessionInfo(1003, "Spotify", 0.8f, true, "dev-yeti", "Speakers (Yeti Classic)"),
        });

    private static void Save(FrameworkElement element, string path, bool light)
    {
        element.UpdateLayout();
        var w = element.ActualWidth > 0 ? element.ActualWidth : 940;
        var h = element.ActualHeight > 0 ? element.ActualHeight : 404;
        const double pad = 30;
        const int scale = 2;

        var content = new RenderTargetBitmap(
            (int)Math.Ceiling(w * scale),
            (int)Math.Ceiling(h * scale),
            96 * scale,
            96 * scale,
            PixelFormats.Pbgra32);
        content.Render(element);

        var canvasW = w + pad * 2;
        var canvasH = h + pad * 2;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var top = light ? Color.FromRgb(0xEF, 0xEA, 0xE2) : Color.FromRgb(0x20, 0x20, 0x25);
            var bottom = light ? Color.FromRgb(0xD8, 0xD2, 0xC7) : Color.FromRgb(0x08, 0x08, 0x0A);
            var bg = new LinearGradientBrush(top, bottom, 90);
            dc.DrawRectangle(bg, null, new Rect(0, 0, canvasW, canvasH));
            dc.DrawImage(content, new Rect(pad, pad, w, h));
        }

        var final = new RenderTargetBitmap((int)Math.Ceiling(canvasW * scale), (int)Math.Ceiling(canvasH * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        final.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(final));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static string? ArgValue(string[] args, string flag)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase) &&
                !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                return args[i + 1];
        return null;
    }
}
