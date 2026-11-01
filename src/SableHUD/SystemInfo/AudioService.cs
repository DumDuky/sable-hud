using System.Diagnostics;
using SableHUD.Core;
using WinAudioRoute;

namespace SableHUD.SystemInfo;

public sealed record AudioSessionInfo(
    int Pid,
    string Name,
    float Volume,
    bool Muted,
    string DeviceId,
    string DeviceName);

public sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault);

public sealed record AudioSnapshot(
    float Master,
    bool MasterMuted,
    bool MicAvailable,
    bool MicMuted,
    string DefaultDeviceName,
    bool RoutingSupported,
    IReadOnlyList<AudioDeviceInfo> Devices,
    IReadOnlyList<AudioSessionInfo> Sessions)
{
    public static readonly AudioSnapshot Empty = new(
        0, false, false, false, "No output", false, Array.Empty<AudioDeviceInfo>(), Array.Empty<AudioSessionInfo>());
}

/// <summary>
/// Master volume, microphone mute and a per-app mixer. Per-app output routing uses
/// the same Windows route store as Settings → System → Sound → Volume mixer;
/// WinAudioRoute wraps that internal API with a capability check.
/// </summary>
public sealed class AudioService : IDisposable
{
    private readonly WindowsAudioManager _audio = new();
    private readonly Dictionary<int, string> _names = new();

    public AudioSnapshot Current { get; private set; } = AudioSnapshot.Empty;
    public bool RoutingSupported => _audio.IsPerAppRoutingSupported;

    public AudioSnapshot Refresh()
    {
        try
        {
            var devices = _audio.GetPlaybackDevices(AudioDeviceState.Active, true);
            var defaultDevice = _audio.GetDefaultDevice(AudioDataFlow.Render, AudioRole.Multimedia, true);
            var deviceList = devices.Select(d => new AudioDeviceInfo(d.Id, d.FriendlyName, d.IsDefault)).ToList();

            float master = 0;
            var masterMuted = false;
            if (defaultDevice is not null)
            {
                try
                {
                    master = _audio.GetDeviceVolume(defaultDevice);
                    masterMuted = _audio.GetDeviceMute(defaultDevice);
                }
                catch { }
            }

            var micAvailable = false;
            var micMuted = false;
            try
            {
                var mic = _audio.GetDefaultDevice(AudioDataFlow.Capture, AudioRole.Communications, true);
                if (mic is not null)
                {
                    micAvailable = true;
                    micMuted = _audio.GetDeviceMute(mic);
                }
            }
            catch { }

            var sessions = new List<AudioSessionInfo>();
            try
            {
                var raw = _audio.GetSessions(AudioDataFlow.Render, true);
                var grouped = new Dictionary<int, AudioSessionInfo>();
                foreach (var s in raw)
                {
                    try
                    {
                        if (!s.IsActive) continue;
                        var pid = s.ProcessId;
                        if (pid <= 4 || pid == Environment.ProcessId) continue;

                        var name = ResolveName(pid, s.DisplayName ?? "");
                        if (string.IsNullOrWhiteSpace(name) ||
                            name.Contains("System Sounds", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("nvcontainer", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("NVIDIA Overlay", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var device = defaultDevice;
                        try { device = _audio.GetApplicationOutput(pid) ?? defaultDevice; } catch { }
                        var deviceId = device?.Id ?? "";
                        var deviceName = device?.FriendlyName ?? "System default";
                        var volume = s.Volume ?? 1f;
                        var muted = s.IsMuted ?? false;

                        if (grouped.TryGetValue(pid, out var existing))
                        {
                            grouped[pid] = existing with
                            {
                                Volume = Math.Max(existing.Volume, volume),
                                Muted = existing.Muted && muted,
                            };
                        }
                        else
                        {
                            grouped[pid] = new AudioSessionInfo(pid, name, volume, muted, deviceId, deviceName);
                        }
                    }
                    catch { }
                }

                // GetSessions() caches each session's volume from the moment it was
                // first seen, so a per-app change (ours or Windows') never shows up
                // there. Read the live level and mute for every PID instead.
                foreach (var pid in grouped.Keys.ToList())
                {
                    try
                    {
                        var live = _audio.GetApplicationVolumePercent(pid);
                        grouped[pid] = grouped[pid] with
                        {
                            Volume = Math.Clamp(live / 100f, 0f, 1f),
                            Muted = _audio.GetApplicationMute(pid),
                        };
                    }
                    catch { }
                }

                sessions = grouped.Values
                    .OrderByDescending(s => s.Name.Equals("Discord", StringComparison.OrdinalIgnoreCase))
                    .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            catch { }

            Current = new AudioSnapshot(
                master,
                masterMuted,
                micAvailable,
                micMuted,
                defaultDevice?.FriendlyName ?? "No output",
                _audio.IsPerAppRoutingSupported,
                deviceList,
                sessions);
        }
        catch (Exception ex)
        {
            Log.Warn("audio refresh: " + ex.Message);
        }
        return Current;
    }

    private string ResolveName(int pid, string display)
    {
        if (_names.TryGetValue(pid, out var cached)) return cached;
        string name = "";
        try
        {
            using var p = Process.GetProcessById(pid);
            name = p.ProcessName;
            try
            {
                var desc = p.MainModule?.FileVersionInfo.FileDescription;
                if (!string.IsNullOrWhiteSpace(desc)) name = desc;
            }
            catch { }
        }
        catch
        {
            name = display;
        }
        name = name.Replace(".exe", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (name.Length > 22) name = name[..21] + "…";
        if (string.IsNullOrWhiteSpace(name)) name = "App";
        _names[pid] = name;
        return name;
    }

    public void SetMasterVolume(float value)
    {
        try
        {
            var device = _audio.GetDefaultDevice(AudioDataFlow.Render, AudioRole.Multimedia, true);
            if (device is not null) _audio.SetDeviceVolume(device, Math.Clamp(value, 0, 1));
        }
        catch (Exception ex) { Log.Warn("master volume: " + ex.Message); }
    }

    public void ToggleMasterMute()
    {
        try
        {
            var device = _audio.GetDefaultDevice(AudioDataFlow.Render, AudioRole.Multimedia, true);
            if (device is not null) _audio.SetDeviceMute(device, !_audio.GetDeviceMute(device));
        }
        catch (Exception ex) { Log.Warn("master mute: " + ex.Message); }
    }

    public void ToggleMicMute()
    {
        try
        {
            var mic = _audio.GetDefaultDevice(AudioDataFlow.Capture, AudioRole.Communications, true);
            if (mic is not null) _audio.SetDeviceMute(mic, !_audio.GetDeviceMute(mic));
        }
        catch (Exception ex) { Log.Warn("mic mute: " + ex.Message); }
    }

    public void ToggleSessionMute(int pid)
    {
        try
        {
            _audio.SetApplicationMute(pid, !_audio.GetApplicationMute(pid));
        }
        catch (Exception ex) { Log.Warn("session mute: " + ex.Message); }
    }

    public void AdjustSessionVolume(int pid, float delta)
    {
        try
        {
            var percent = _audio.GetApplicationVolumePercent(pid);
            _audio.SetApplicationVolumePercent(pid, Math.Clamp(percent + (int)Math.Round(delta * 100), 0, 100));
        }
        catch (Exception ex) { Log.Warn("session volume: " + ex.Message); }
    }

    public (bool Ok, string Message) SetSessionOutput(int pid, string deviceId)
    {
        try
        {
            if (!_audio.IsPerAppRoutingSupported)
                return (false, "This Windows build does not expose per-app routing");

            var device = _audio.ResolvePlaybackDevice(deviceId);
            var result = _audio.SetApplicationOutput(pid, device);
            if (result.IsSuccess)
            {
                Refresh();
                return (true, device.FriendlyName);
            }

            var message = result.Failures.Count > 0 ? result.Failures[0].ToString() : "Windows rejected the route";
            return (false, message);
        }
        catch (Exception ex)
        {
            Log.Warn("session output: " + ex.Message);
            return (false, ex.Message);
        }
    }

    public void ResetSessionOutput(int pid)
    {
        try
        {
            _audio.ResetApplicationOutput(pid);
            Refresh();
        }
        catch (Exception ex) { Log.Warn("session output reset: " + ex.Message); }
    }

    public void Dispose()
    {
        try { _audio.Dispose(); } catch { }
    }
}
