using System.Runtime.InteropServices;

namespace SableHUD.SystemInfo;

/// <summary>
/// Optional FPS readout. RTSS (RivaTuner Statistics Server) already measures frames
/// for the foreground process; Sable only reads that shared block. No injection,
/// no anti-cheat surface. If RTSS is absent the widget shows a calm dash.
/// </summary>
public sealed class RtssReader : IDisposable
{
    private const string MapName = "RTSSSharedMemoryV2";
    private IntPtr _handle;
    private IntPtr _view;
    private uint _entrySize;
    private uint _arrOffset;
    private uint _arrSize;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenFileMapping(uint access, bool inherit, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr MapViewOfFile(IntPtr handle, uint access, uint offsetHigh, uint offsetLow, UIntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UnmapViewOfFile(IntPtr address);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    public double? ReadFps(uint processId)
    {
        if (processId == 0) return null;
        try
        {
            if (!EnsureOpen()) return null;

            var now = unchecked((uint)Environment.TickCount);
            for (var i = 0u; i < _arrSize; i++)
            {
                var entry = (int)(_view + (int)_arrOffset + i * _entrySize);
                var pid = Marshal.ReadInt32(entry);
                if (pid == 0 || (uint)pid != processId) continue;

                var time0 = unchecked((uint)Marshal.ReadInt32(entry, 268));
                var time1 = unchecked((uint)Marshal.ReadInt32(entry, 272));
                var frames = unchecked((uint)Marshal.ReadInt32(entry, 276));
                if (time0 == 0 || time1 <= time0 || frames == 0) return null;
                if (now - time1 > 2500) return null; // stale entry

                var fps = frames * 1000.0 / (time1 - time0);
                return fps is > 0 and < 2000 ? fps : null;
            }
        }
        catch
        {
            Close();
        }
        return null;
    }

    private bool EnsureOpen()
    {
        if (_view != IntPtr.Zero) return true;

        _handle = OpenFileMapping(0x0004, false, MapName);
        if (_handle == IntPtr.Zero) return false;
        _view = MapViewOfFile(_handle, 0x0004, 0, 0, UIntPtr.Zero);
        if (_view == IntPtr.Zero)
        {
            Close();
            return false;
        }

        if (Marshal.ReadInt32(_view, 0) is not 0x53535452) // 'RTSS'
        {
            Close();
            return false;
        }

        _entrySize = unchecked((uint)Marshal.ReadInt32(_view, 8));
        _arrOffset = unchecked((uint)Marshal.ReadInt32(_view, 12));
        _arrSize = unchecked((uint)Marshal.ReadInt32(_view, 16));
        if (_entrySize < 280 || _arrSize is 0 or > 4096)
        {
            Close();
            return false;
        }
        return true;
    }

    private void Close()
    {
        try { if (_view != IntPtr.Zero) UnmapViewOfFile(_view); } catch { }
        try { if (_handle != IntPtr.Zero) CloseHandle(_handle); } catch { }
        _view = IntPtr.Zero;
        _handle = IntPtr.Zero;
        _arrOffset = _entrySize = _arrSize = 0;
    }

    public void Dispose() => Close();
}
