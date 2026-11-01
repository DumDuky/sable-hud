using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using SableHUD.Core;

namespace SableHUD.SystemInfo;

public sealed record SystemSnapshot(
    double CpuPercent,
    double GpuPercent,
    bool GpuAvailable,
    double GpuMemoryGb,
    double RamPercent,
    double RamUsedGb,
    double RamTotalGb,
    double NetDownMbps,
    double NetUpMbps,
    IReadOnlyList<double> DownHistory,
    IReadOnlyList<double> UpHistory,
    double? Fps,
    string FocusTitle,
    string FocusExe,
    TimeSpan Session,
    TimeSpan SystemUptime,
    int BatteryPercent,
    bool BatteryCharging,
    bool HasBattery)
{
    public static readonly SystemSnapshot Empty = new(
        0, 0, false, 0, 0, 0, 0, 0, 0, Array.Empty<double>(), Array.Empty<double>(),
        null, "Desktop", "", TimeSpan.Zero, TimeSpan.Zero, -1, false, false);
}

/// <summary>One-second system sampling on a background thread.</summary>
public sealed class SystemMonitor : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
        public ulong Value => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out PowerStatus status);

    [DllImport("kernel32.dll")]
    private static extern ulong GetTickCount64();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int max);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    private readonly System.Threading.Timer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly GpuCounters _gpu = new();
    private readonly RtssReader _rtss = new();
    private readonly Queue<double> _down = new();
    private readonly Queue<double> _up = new();
    private readonly Queue<double> _fpsHistory = new();

    private ulong _prevIdle;
    private ulong _prevKernel;
    private ulong _prevUser;
    private bool _hasCpuPrev;
    private long _prevNetRx;
    private long _prevNetTx;
    private long _prevNetAt;
    private bool _disposed;

    public bool Demo { get; set; }
    public SystemSnapshot Current { get; private set; } = SystemSnapshot.Empty;
    public event Action<SystemSnapshot>? Updated;

    public SystemMonitor(bool demo = false)
    {
        Demo = demo;
        _timer = new System.Threading.Timer(_ => Tick(), null, 0, 1000);
    }

    private void Tick()
    {
        if (_disposed) return;
        try
        {
            var snapshot = Read();
            Current = snapshot;
            Updated?.Invoke(snapshot);
        }
        catch (Exception ex)
        {
            Log.Warn("system monitor: " + ex.Message);
        }
    }

    private SystemSnapshot Read()
    {
        if (Demo) return ReadDemo();

        // CPU
        double cpu = 0;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            if (_hasCpuPrev)
            {
                var idleDelta = idle.Value - _prevIdle;
                var totalDelta = (kernel.Value - _prevKernel) + (user.Value - _prevUser);
                if (totalDelta > 0) cpu = Math.Clamp((1.0 - (double)idleDelta / totalDelta) * 100.0, 0, 100);
            }
            _prevIdle = idle.Value;
            _prevKernel = kernel.Value;
            _prevUser = user.Value;
            _hasCpuPrev = true;
        }

        // Memory
        var mem = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        var ramPercent = 0.0;
        var ramUsed = 0.0;
        var ramTotal = 0.0;
        if (GlobalMemoryStatusEx(ref mem))
        {
            ramPercent = mem.MemoryLoad;
            ramTotal = mem.TotalPhys / 1024.0 / 1024.0 / 1024.0;
            ramUsed = (mem.TotalPhys - mem.AvailPhys) / 1024.0 / 1024.0 / 1024.0;
        }

        // GPU
        var (gpuUsage, gpuMem) = _gpu.Read();

        // Network
        var (down, up) = ReadNetwork();
        _down.Enqueue(down);
        _up.Enqueue(up);
        while (_down.Count > 72) _down.Dequeue();
        while (_up.Count > 72) _up.Dequeue();

        // Foreground
        var hwnd = GetForegroundWindow();
        var pid = 0u;
        var title = "";
        if (hwnd != IntPtr.Zero)
        {
            GetWindowThreadProcessId(hwnd, out pid);
            var sb = new StringBuilder(320);
            GetWindowTextW(hwnd, sb, sb.Capacity);
            title = sb.ToString();
        }

        var exe = "";
        var description = "";
        string focus = "Desktop";
        if (pid != 0)
        {
            try
            {
                using var process = Process.GetProcessById((int)pid);
                exe = process.ProcessName + ".exe";
                try { description = process.MainModule?.FileVersionInfo.FileDescription ?? ""; } catch { }
                if (exe.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) ||
                    exe.Equals("SableHUD.exe", StringComparison.OrdinalIgnoreCase) ||
                    exe.Equals("SearchHost.exe", StringComparison.OrdinalIgnoreCase) ||
                    exe.Equals("ShellExperienceHost.exe", StringComparison.OrdinalIgnoreCase))
                {
                    focus = "Desktop";
                    exe = "";
                }
                else
                {
                    focus = string.IsNullOrWhiteSpace(title) ? process.ProcessName : title;
                    if (focus.Contains(":\\", StringComparison.Ordinal) || focus.Length > 64)
                        focus = string.IsNullOrWhiteSpace(description) ? process.ProcessName : description;
                }
            }
            catch
            {
                focus = string.IsNullOrWhiteSpace(title) ? "Desktop" : title;
            }
        }
        if (focus.Length > 54) focus = focus[..53] + "…";

        // FPS (optional, RTSS)
        double? fps = exe.Length > 0 ? _rtss.ReadFps(pid) : null;
        if (fps is not null)
        {
            _fpsHistory.Enqueue(fps.Value);
            while (_fpsHistory.Count > 72) _fpsHistory.Dequeue();
        }
        else
        {
            _fpsHistory.Clear();
        }

        // Battery
        var hasBattery = false;
        var charging = false;
        var battery = -1;
        if (GetSystemPowerStatus(out var power) && power.BatteryLifePercent != 255)
        {
            hasBattery = true;
            battery = power.BatteryLifePercent;
            charging = (power.BatteryFlag & 0x08) != 0 || power.ACLineStatus == 1;
        }

        return new SystemSnapshot(
            cpu,
            gpuUsage,
            _gpu.Available,
            gpuMem,
            ramPercent,
            ramUsed,
            ramTotal,
            down,
            up,
            _down.ToArray(),
            _up.ToArray(),
            fps,
            focus,
            exe,
            _clock.Elapsed,
            TimeSpan.FromMilliseconds(GetTickCount64()),
            battery,
            charging,
            hasBattery);
    }

    private (double Down, double Up) ReadNetwork()
    {
        long rx = 0, tx = 0;
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var stats = ni.GetIPStatistics();
                rx += stats.BytesReceived;
                tx += stats.BytesSent;
            }
        }
        catch
        {
            return (0, 0);
        }

        var now = Environment.TickCount64;
        if (_prevNetAt == 0)
        {
            _prevNetAt = now;
            _prevNetRx = rx;
            _prevNetTx = tx;
            return (0, 0);
        }

        var seconds = Math.Max(0.001, (now - _prevNetAt) / 1000.0);
        var down = Math.Max(0, rx - _prevNetRx) / seconds * 8 / 1_000_000.0;
        var up = Math.Max(0, tx - _prevNetTx) / seconds * 8 / 1_000_000.0;
        _prevNetAt = now;
        _prevNetRx = rx;
        _prevNetTx = tx;
        return (down, up);
    }

    private SystemSnapshot ReadDemo()
    {
        var t = _clock.Elapsed.TotalSeconds;
        var cpu = 34 + Math.Sin(t / 5.1) * 9 + Math.Sin(t / 1.7) * 3;
        var gpu = 71 + Math.Sin(t / 3.3) * 12;
        var down = 2.4 + Math.Sin(t / 2.0) * 2.0;
        var up = 0.42 + Math.Sin(t / 1.3) * 0.2;

        _down.Enqueue(Math.Max(0, down));
        _up.Enqueue(Math.Max(0, up));
        while (_down.Count > 72) _down.Dequeue();
        while (_up.Count > 72) _up.Dequeue();

        return new SystemSnapshot(
            Math.Clamp(cpu, 0, 100),
            Math.Clamp(gpu, 0, 100),
            true,
            6.4,
            46,
            14.9,
            32,
            Math.Max(0, down),
            Math.Max(0, up),
            _down.ToArray(),
            _up.ToArray(),
            116.8,
            "ELDEN RING",
            "eldenring.exe",
            TimeSpan.FromMinutes(83),
            TimeSpan.FromHours(9.4),
            84,
            false,
            true);
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
        _gpu.Dispose();
        _rtss.Dispose();
    }
}
