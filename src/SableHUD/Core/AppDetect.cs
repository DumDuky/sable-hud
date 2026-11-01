using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.Win32;

namespace SableHUD.Core;

public static class AppDetect
{
    public static string? FindLimusic(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

        var candidates = new List<string>();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        foreach (var root in new[] { local, roaming, pf, pf86 })
        {
            candidates.Add(Path.Combine(root, "limusic", "limusic-app.exe"));
            candidates.Add(Path.Combine(root, "limusic", "limusic.exe"));
            candidates.Add(Path.Combine(root, "Limusic", "limusic-app.exe"));
            candidates.Add(Path.Combine(root, "Limusic", "limusic.exe"));
            candidates.Add(Path.Combine(root, "Programs", "limusic", "limusic-app.exe"));
            candidates.Add(Path.Combine(root, "Programs", "limusic", "limusic.exe"));
            candidates.Add(Path.Combine(root, "Programs", "Limusic", "limusic-app.exe"));
            candidates.Add(Path.Combine(root, "LiMusic", "limusic-app.exe"));
            candidates.Add(Path.Combine(root, "LiMusic", "limusic.exe"));
        }

        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        // Uninstall entries.
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var uninstall = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (var sub in uninstall.GetSubKeyNames())
                {
                    using var k = uninstall.OpenSubKey(sub);
                    var name = k?.GetValue("DisplayName") as string;
                    if (name is null || !name.Contains("limusic", StringComparison.OrdinalIgnoreCase)) continue;
                    var icon = k?.GetValue("DisplayIcon") as string;
                    if (!string.IsNullOrWhiteSpace(icon))
                    {
                        var exe = icon.Trim('"').Split(',')[0];
                        if (File.Exists(exe)) return exe;
                    }
                    var loc = k?.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrWhiteSpace(loc))
                    {
                        var exe = Path.Combine(loc, "limusic-app.exe");
                        if (File.Exists(exe)) return exe;
                    }
                }
            }
            catch
            {
                // registry is best-effort
            }
        }
        return null;
    }

    public static bool Open(string target, string? args = null)
    {
        try
        {
            var psi = new ProcessStartInfo(target)
            {
                UseShellExecute = true,
                Arguments = args ?? string.Empty,
            };
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"open '{target}' failed: {ex.Message}");
            return false;
        }
    }

    public static bool OpenLimusic(string? configured)
    {
        var path = FindLimusic(configured);
        return path is not null && Open(path);
    }

    public static bool OpenDiscord()
    {
        if (Open("discord://")) return true;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var update = Path.Combine(local, "Discord", "Update.exe");
        return File.Exists(update) && Open(update, "--processStart Discord.exe");
    }

    public static bool OpenSpotify() => Open("spotify:");

    public static bool OpenSteam() => Open("steam://open/main");

    public static string? GameBarSettingsUri => "ms-settings:gaming-gamebar";

    /// <summary>Turn off the Windows Game Bar controller shortcut (the Guide button) if allowed.</summary>
    public static bool DisableGameBarControllerButton()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\GameBar");
            key?.SetValue("UseNexusForGameBarEnabled", 0, RegistryValueKind.DWord);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("disable game bar failed: " + ex.Message);
            return false;
        }
    }
}

public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "SableHUD";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Name) is string s && s.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null) return;
            if (enabled)
            {
                var exe = Paths.ExePath;
                if (!string.IsNullOrWhiteSpace(exe))
                    key.SetValue(Name, $"\"{exe}\" --autostart", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(Name, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("autostart failed: " + ex.Message);
        }
    }
}

public static class ScreenshotService
{
    public static string? CaptureAllScreens()
    {
        try
        {
            var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
            using var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bmp.Size, CopyPixelOperation.SourceCopy);
            }
            var path = Path.Combine(Paths.ScreenshotDir, $"Sable {DateTime.Now:yyyy-MM-dd HH-mm-ss}.png");
            bmp.Save(path, ImageFormat.Png);
            return path;
        }
        catch (Exception ex)
        {
            Log.Warn("screenshot failed: " + ex.Message);
            return null;
        }
    }
}
