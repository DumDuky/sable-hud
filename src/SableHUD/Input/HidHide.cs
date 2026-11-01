using System.Diagnostics;
using System.Text.Json;
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

    public static async Task<(bool Ok, string Message)> PrepareAsync()
    {
        if (!IsInstalled) return (false, "HidHide is not installed");

        try
        {
            var exe = Paths.ExePath;
            if (string.IsNullOrWhiteSpace(exe) || Path.GetFileName(exe).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
                return (false, "Run the packaged SableHUD.exe so the driver can allowlist it");

            var inventory = await RunAsync("--dev-gaming").ConfigureAwait(false);
            var devices = new List<string>();
            if (inventory.Ok)
            {
                try
                {
                    using var doc = JsonDocument.Parse(inventory.Output);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in doc.RootElement.EnumerateArray())
                        {
                            if (item.TryGetProperty("gamingDevice", out var gaming) && gaming.ValueKind == JsonValueKind.False)
                                continue;

                            foreach (var name in new[] { "deviceInstancePath", "xusbDeviceInstancePath" })
                            {
                                if (!item.TryGetProperty(name, out var value)) continue;
                                var path = value.GetString();
                                if (!string.IsNullOrWhiteSpace(path) && !devices.Contains(path))
                                    devices.Add(path);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("HidHide inventory parse: " + ex.Message);
                }
            }

            var args = new List<string> { "--app-reg", exe, "--inv-off", "--cloak-off" };
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

    private static async Task<(bool Ok, string Output, string Error)> RunAsync(params string[] args)
    {
        if (!IsInstalled) return (false, "", "HidHide is not installed");
        try
        {
            var info = new ProcessStartInfo(CliPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in args) info.ArgumentList.Add(arg);

            using var process = Process.Start(info);
            if (process is null) return (false, "", "could not start HidHideCLI");

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            var exitTask = process.WaitForExitAsync();
            var finished = await Task.WhenAny(exitTask, Task.Delay(10_000)).ConfigureAwait(false);
            if (finished != exitTask) return (false, "", "HidHideCLI timed out");

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
