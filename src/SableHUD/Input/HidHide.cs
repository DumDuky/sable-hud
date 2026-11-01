using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SableHUD.Core;

namespace SableHUD.Input;

/// <summary>
/// Optional controller isolation through HidHide (MIT). HidHide only hides the
/// physical pad while the HUD is open; Sable is allowlisted, games are not.
/// Nothing is installed automatically: the driver installer is bundled and the
/// Settings row launches it only when the user asks.
/// </summary>
public static class HidHide
{
    public const string InstallerName = "HidHide_1.5.230_x64.exe";

    public static bool IsInstalled => File.Exists(CliPath);

    public static string CliPath
    {
        get
        {
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var current = Path.Combine(pf, "Nefarius Software Solutions", "HidHide", "HidHideCLI.exe");
            if (File.Exists(current)) return current;
            var old = Path.Combine(pf, "Nefarius Software Solutions", "HidHide", "x64", "HidHideCLI.exe");
            return old;
        }
    }

    public static string? InstallerPath
    {
        get
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "drivers", InstallerName),
                Path.Combine(AppContext.BaseDirectory, InstallerName),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "vendor", InstallerName),
            };
            return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
        }
    }

    public static bool Install()
    {
        var installer = InstallerPath;
        if (installer is null)
        {
            Log.Warn("HidHide installer not bundled");
            return false;
        }

        try
        {
            var process = Process.Start(new ProcessStartInfo(installer)
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            return process is not null;
        }
        catch (Exception ex)
        {
            Log.Warn("HidHide install: " + ex.Message);
            return false;
        }
    }

    private static string Pnputil => Path.Combine(Environment.SystemDirectory, "pnputil.exe");

    public static async Task<List<string>> BuildDeviceListAsync()
    {
        var devices = new List<string>();
        if (!IsInstalled) return devices;

        var inventory = await RunAsync("--dev-gaming").ConfigureAwait(false);
        if (!inventory.Ok) return devices;

        try
        {
            using var doc = JsonDocument.Parse(inventory.Output);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var group in doc.RootElement.EnumerateArray())
                    CollectGamingDevices(group, devices);
            }
            AddXusbDevices(devices);
        }
        catch (Exception ex)
        {
            Log.Warn("HidHide inventory parse: " + ex.Message);
        }
        return devices;
    }

    public static async Task<(bool Ok, string Message)> PrepareAsync()
    {
        if (!IsInstalled) return (false, "HidHide is not installed");

        try
        {
            var exe = Paths.ExePath;
            if (string.IsNullOrWhiteSpace(exe) || Path.GetFileName(exe).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
                return (false, "Run the packaged SableHUD.exe so the driver can allowlist it");

            var devices = await BuildDeviceListAsync().ConfigureAwait(false);

            // Replace the persisted hide list instead of growing it. A stale
            // path from a previous controller or a virtual pad must not stay
            // hidden.
            var clears = new List<string>();
            var current = await RunAsync("--dev-list").ConfigureAwait(false);
            if (current.Ok)
            {
                foreach (var line in current.Output.Split('\n'))
                {
                    var match = Regex.Match(line, "--dev-hide\\s+\"(.+)\"");
                    if (match.Success) clears.Add(match.Groups[1].Value);
                }
            }

            var args = new List<string> { "--app-reg", exe, "--inv-off", "--cloak-off" };
            foreach (var device in clears)
            {
                args.Add("--dev-unhide");
                args.Add(device);
            }
            foreach (var device in devices)
            {
                args.Add("--dev-hide");
                args.Add(device);
            }

            var result = await RunAsync(args.ToArray()).ConfigureAwait(false);
            return result.Ok
                ? (true, devices.Count == 0 ? "Ready; no gaming HID found yet" : $"Ready; {devices.Count} controller device(s)")
                : (false, Clean(result.Error));
        }
        catch (Exception ex)
        {
            Log.Warn("HidHide prepare: " + ex.Message);
            return (false, ex.Message);
        }
    }

    public static bool PadResetTaskExists()
    {
        if (!IsInstalled) return false;
        try
        {
            using var process = Process.Start(new ProcessStartInfo("schtasks.exe", "/query /tn SableHUD-PadReset")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null) return false;
            if (!process.WaitForExit(2500))
            {
                try { process.Kill(); } catch { }
                return false;
            }
            return process.ExitCode == 0;
        }
        catch { return false; }
    }

    public static async Task<bool> RunPadResetAsync()
    {
        if (!PadResetTaskExists()) return false;
        var result = await RunToolAsync("schtasks.exe", "/run", "/tn", "SableHUD-PadReset").ConfigureAwait(false);
        if (!result.Ok)
        {
            Log.Warn("pad reset: " + Clean(result.Error));
            return false;
        }
        await Task.Delay(1000).ConfigureAwait(false);
        return true;
    }

    public static async Task<(bool Ok, string Message)> InstallPadResetAsync()
    {
        if (!IsInstalled) return (false, "HidHide is not installed");
        var devices = await BuildDeviceListAsync().ConfigureAwait(false);
        if (devices.Count == 0) return (false, "Connect the controller, then run this again");
        if (string.IsNullOrWhiteSpace(Paths.ExePath)) return (false, "Sable path is unavailable");

        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SableHUD");
            Directory.CreateDirectory(dir);
            var script = Path.Combine(dir, "pad-reset.cmd");

            // The USB composite parent goes first: restarting it cycles every
            // child interface, including the XUSB interface XInput reads. The
            // children are kept as a fallback when the parent cannot restart.
            var ordered = devices
                .OrderByDescending(d => d.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase) &&
                                        !d.Contains("&MI_", StringComparison.OrdinalIgnoreCase))
                .ThenBy(d => d)
                .ToList();

            var lines = new List<string>
            {
                "@echo off",
                "set LOG=%TEMP%\\sable-pad-reset.log",
                "echo ==== %DATE% %TIME% ==== > \"%LOG%\"",
            };
            foreach (var device in ordered)
                lines.Add($"\"{Pnputil}\" /restart-device \"{device}\" >> \"%LOG%\" 2>&1");
            lines.Add("echo done >> \"%LOG%\"");
            lines.Add("exit /b 0");
            await File.WriteAllLinesAsync(script, lines).ConfigureAwait(false);

            var acl = await RunToolAsync("icacls.exe", dir, "/inheritance:r",
                "/grant:r", "*S-1-5-32-544:(OI)(CI)F",
                "*S-1-5-18:(OI)(CI)F",
                "*S-1-5-32-545:(OI)(CI)RX").ConfigureAwait(false);
            if (!acl.Ok) return (false, "Could not protect the reset script: " + Clean(acl.Error));

            await RunAsync("--app-reg", Pnputil).ConfigureAwait(false);
            var action = $"cmd.exe /c \"{script}\"";
            var result = await RunToolAsync("schtasks.exe", "/create", "/f",
                "/tn", "SableHUD-PadReset",
                "/tr", action,
                "/sc", "once", "/st", "00:00",
                "/rl", "HIGHEST").ConfigureAwait(false);
            return result.Ok
                ? (true, $"Automatic pad reset ready for {devices.Count} device(s)")
                : (false, Clean(result.Error));
        }
        catch (Exception ex)
        {
            Log.Warn("pad reset setup: " + ex.Message);
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// XInput pads that enumerate as HID gamepads also expose an XUSB
    /// (XnaComposite/XboxComposite) interface that the HidHide JSON does not
    /// list. Without it, cloaking hides the pad from DirectInput but XInput
    /// still sees it, so isolation looks like it does nothing.
    /// </summary>
    private static void AddXusbDevices(List<string> devices)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in devices)
        {
            var match = Regex.Match(path, @"VID_([0-9A-Fa-f]{4})&PID_([0-9A-Fa-f]{4})");
            if (match.Success)
                ids.Add($"VID_{match.Groups[1].Value}&PID_{match.Groups[2].Value}");
        }
        if (ids.Count == 0) return;

        try
        {
            using var usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
            if (usb is null) return;
            foreach (var iface in usb.GetSubKeyNames())
            {
                if (!ids.Any(id => iface.StartsWith(id, StringComparison.OrdinalIgnoreCase))) continue;
                using var ifaceKey = usb.OpenSubKey(iface);
                if (ifaceKey is null) continue;

                foreach (var instance in ifaceKey.GetSubKeyNames())
                {
                    using var instKey = ifaceKey.OpenSubKey(instance);
                    var service = instKey?.GetValue("Service") as string;
                    if (service is null || !service.StartsWith("xusb", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var path = $@"USB\{iface}\{instance}";
                    if (!devices.Contains(path)) devices.Add(path);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("HidHide xusb inventory: " + ex.Message);
        }
    }

    private static void CollectGamingDevices(JsonElement item, List<string> devices)
    {
        if (item.ValueKind != JsonValueKind.Object) return;
        if (item.TryGetProperty("gamingDevice", out var gaming) && gaming.ValueKind == JsonValueKind.False)
            return;
        if (item.TryGetProperty("present", out var present) && present.ValueKind == JsonValueKind.False)
            return;

        foreach (var name in new[] { "deviceInstancePath", "xusbDeviceInstancePath" })
        {
            if (!item.TryGetProperty(name, out var value)) continue;
            var path = value.GetString();
            if (!string.IsNullOrWhiteSpace(path) && !devices.Contains(path))
                devices.Add(path);
        }

        if (item.TryGetProperty("devices", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
                CollectGamingDevices(child, devices);
        }
    }

    public static bool ReleaseCloakBlocking(int timeoutMs = 5000)
    {
        if (!IsInstalled) return false;
        try
        {
            var info = new ProcessStartInfo(CliPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            info.ArgumentList.Add("--cloak-off");
            using var process = Process.Start(info);
            if (process is null) return false;
            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(); } catch { }
                return false;
            }
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> SetCloakAsync(bool on)
    {
        if (!IsInstalled) return false;
        var result = await RunAsync(on ? "--cloak-on" : "--cloak-off").ConfigureAwait(false);
        if (!result.Ok) Log.Warn("HidHide cloak: " + Clean(result.Error));
        return result.Ok;
    }

    public static async Task<bool> IsCloakedAsync()
    {
        if (!IsInstalled) return false;
        var result = await RunAsync("--cloak-state").ConfigureAwait(false);
        return result.Ok && result.Output.Contains("--cloak-on", StringComparison.OrdinalIgnoreCase);
    }

    private static Task<(bool Ok, string Output, string Error)> RunAsync(params string[] args) =>
        RunToolAsync(CliPath, args);

    private static async Task<(bool Ok, string Output, string Error)> RunToolAsync(string file, params string[] args)
    {
        try
        {
            var info = new ProcessStartInfo(file)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in args) info.ArgumentList.Add(arg);

            using var process = Process.Start(info);
            if (process is null) return (false, "", "could not start " + Path.GetFileName(file));

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            var exitTask = process.WaitForExitAsync();
            var finished = await Task.WhenAny(exitTask, Task.Delay(10_000)).ConfigureAwait(false);
            if (finished != exitTask) return (false, "", Path.GetFileName(file) + " timed out");

            var output = await stdout.ConfigureAwait(false);
            var error = await stderr.ConfigureAwait(false);
            return (process.ExitCode == 0, output.Trim(), error.Trim());
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }

    private static string Clean(string text) =>
        string.IsNullOrWhiteSpace(text) ? "HidHideCLI failed" : text.Replace("\r", " ").Replace("\n", " ").Trim();
}
