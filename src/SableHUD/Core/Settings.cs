using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SableHUD.Core;

public sealed class AppSettings
{
    // appearance
    public string Theme { get; set; } = "Noir";
    public string Scale { get; set; } = "Comfortable"; // Compact | Comfortable | Large
    public string DefaultTab { get; set; } = "Music";
    public string ArtworkSource { get; set; } = "Song"; // Song | Playlist

    // trigger
    public string Trigger { get; set; } = "Guide"; // Guide | GuideDouble | GuideHold | LbRb | Custom
    public string CustomTrigger { get; set; } = "Guide";
    public bool ControllerEnabled { get; set; } = true;
    public bool KeyboardHotkey { get; set; } = true;
    public int AutoHideSeconds { get; set; } = 12;
    public bool Rumble { get; set; } = true;
    public bool IsolateController { get; set; } = false;

    // widgets
    public bool ShowMusic { get; set; } = true;
    public bool ShowPerformance { get; set; } = true;
    public bool ShowAudio { get; set; } = true;
    public bool ShowTools { get; set; } = true;
    public bool ShowMiniPlayer { get; set; } = false;
    public bool ToastOnTrackChange { get; set; } = true;
    public bool RtssFps { get; set; } = true;
    public bool Autoplay { get; set; } = true;
    public double MiniLeft { get; set; } = -1;
    public double MiniTop { get; set; } = -1;
    public bool MiniPlaced { get; set; } = false;
    public string MiniDock { get; set; } = "BottomRight";

    // music
    public string MusicFolder { get; set; } = "";
    public string LimusicPath { get; set; } = "";
    public float LocalVolume { get; set; } = 0.8f;

    // system
    public bool StartWithWindows { get; set; } = false;
    public bool FirstRun { get; set; } = true;
    public int Schema { get; set; } = 1;

    [JsonIgnore]
    public double ScaleFactor => Scale switch
    {
        "Compact" => 0.88,
        "Large" => 1.14,
        _ => 1.0,
    };

    [JsonIgnore]
    public int AutoHideMs => AutoHideSeconds <= 0 ? 0 : AutoHideSeconds * 1000;
}

public static class Paths
{
    public static string DataDir
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SableHUD");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string LogFile => Path.Combine(DataDir, "sable.log");
    public static string ArtCacheDir
    {
        get
        {
            var d = Path.Combine(Path.GetTempPath(), "SableHUD", "art");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public static string ScreenshotDir
    {
        get
        {
            var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Sable HUD");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public static string? ExePath => Environment.ProcessPath;
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static readonly object Gate = new();

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(Paths.SettingsFile))
            {
                var json = File.ReadAllText(Paths.SettingsFile);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded is not null)
                {
                    Sanitize(loaded);
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("settings load failed: " + ex.Message);
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            lock (Gate)
            {
                Sanitize(settings);
                var json = JsonSerializer.Serialize(settings, JsonOptions);
                var tmp = Paths.SettingsFile + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, Paths.SettingsFile, true);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("settings save failed: " + ex.Message);
        }
    }

    private static void Sanitize(AppSettings s)
    {
        if (Themes.All.All(t => t.Id != s.Theme)) s.Theme = "Noir";
        if (s.Scale is not ("Compact" or "Comfortable" or "Large")) s.Scale = "Comfortable";
        if (s.Trigger is not ("Guide" or "GuideDouble" or "GuideHold" or "LbRb" or "Custom")) s.Trigger = "Guide";
        if (s.AutoHideSeconds < 0) s.AutoHideSeconds = 0;
        if (s.AutoHideSeconds > 120) s.AutoHideSeconds = 120;
        if (string.IsNullOrWhiteSpace(s.CustomTrigger)) s.CustomTrigger = "Guide";
        var docks = new[] { "Custom", "TopLeft", "TopCenter", "TopRight", "MiddleLeft", "Center", "MiddleRight", "BottomLeft", "BottomCenter", "BottomRight" };
        if (!docks.Contains(s.MiniDock)) s.MiniDock = "BottomRight";
        s.LocalVolume = Math.Clamp(s.LocalVolume, 0f, 1f);
        if (s.DefaultTab is not ("Music" or "Performance" or "Audio" or "Tools")) s.DefaultTab = "Music";
        if (s.ArtworkSource is not ("Song" or "Playlist")) s.ArtworkSource = "Song";
    }
}

public static class Log
{
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(Paths.LogFile, line);
            }
        }
        catch
        {
            // logging must never take the app down
        }
    }
}
