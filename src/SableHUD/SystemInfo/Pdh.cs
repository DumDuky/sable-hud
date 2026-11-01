using System.Runtime.InteropServices;

namespace SableHUD.SystemInfo;

/// <summary>Minimal PDH wrapper for the Windows GPU Engine counters.</summary>
public sealed class PdhQuery : IDisposable
{
    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhMoreData = 0x800007D2;

    [StructLayout(LayoutKind.Sequential)]
    private struct FormatItem
    {
        public IntPtr Name;
        public uint Status;
        public double Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArrayW(
        IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr buffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);

    private IntPtr _query;
    private IntPtr _counter;

    public bool Available { get; }

    public PdhQuery(string path)
    {
        try
        {
            if (PdhOpenQueryW(null, IntPtr.Zero, out _query) != 0) return;
            if (PdhAddEnglishCounterW(_query, path, IntPtr.Zero, out _counter) != 0) return;
            PdhCollectQueryData(_query);
            Available = true;
        }
        catch
        {
            Available = false;
        }
    }

    /// <summary>Sums every instance whose name matches, or -1 when the read fails.</summary>
    public double Sum(Func<string, bool>? filter = null)
    {
        if (!Available) return -1;
        try
        {
            PdhCollectQueryData(_query);
            uint size = 0;
            var status = PdhGetFormattedCounterArrayW(_counter, PdhFmtDouble, ref size, out var count, IntPtr.Zero);
            if (status != PdhMoreData || size == 0 || count == 0) return 0;

            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                status = PdhGetFormattedCounterArrayW(_counter, PdhFmtDouble, ref size, out count, buffer);
                if (status != 0) return -1;

                var stride = Marshal.SizeOf<FormatItem>();
                var sum = 0.0;
                for (var i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<FormatItem>(buffer + i * stride);
                    if (item.Status != 0) continue;
                    var name = item.Name == IntPtr.Zero ? "" : Marshal.PtrToStringUni(item.Name) ?? "";
                    if (filter is not null && !filter(name)) continue;
                    sum += item.Value;
                }
                return sum;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return -1;
        }
    }

    public void Dispose()
    {
        try { if (_query != IntPtr.Zero) PdhCloseQuery(_query); } catch { }
        _query = _counter = IntPtr.Zero;
    }
}

public sealed class GpuCounters : IDisposable
{
    private readonly PdhQuery _engine = new(@"\GPU Engine(*)\Utilization Percentage");
    private readonly PdhQuery _memory = new(@"\GPU Adapter Memory(*)\Dedicated Usage");

    public bool Available => _engine.Available;

    /// <summary>3D engine utilisation in percent and dedicated VRAM in GB.</summary>
    public (double Usage, double MemoryGb) Read()
    {
        var usage = _engine.Sum(name => name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase));
        var mem = _memory.Sum();
        return (usage < 0 ? 0 : Math.Clamp(usage, 0, 100), mem < 0 ? 0 : mem / 1024.0 / 1024.0 / 1024.0);
    }

    public void Dispose()
    {
        _engine.Dispose();
        _memory.Dispose();
    }
}
