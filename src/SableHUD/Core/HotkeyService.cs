using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace SableHUD.Core;

public sealed class HotkeyService
{
    public const int IdHud = 0x5AB1;
    public const int IdMini = 0x5AB2;

    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HwndSource? _source;
    private bool _registered;

    public event Action? ToggleRequested;
    public event Action? MiniRequested;

    public bool Register()
    {
        if (_registered) return true;
        try
        {
            var parameters = new HwndSourceParameters("SableHUD.Hotkeys")
            {
                Width = 0,
                Height = 0,
                WindowStyle = 0,
                ParentWindow = new IntPtr(-3), // HWND_MESSAGE
            };
            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);

            var ok = RegisterHotKey(_source.Handle, IdHud, ModControl | ModAlt | ModNoRepeat, 0x48); // H
            var okMini = RegisterHotKey(_source.Handle, IdMini, ModControl | ModAlt | ModNoRepeat, 0x4E);   // N
            _registered = ok;
            if (!ok) Log.Warn("RegisterHotKey(Ctrl+Alt+H) failed");
            if (!okMini) Log.Warn("RegisterHotKey(Ctrl+Alt+N) failed");
            return ok;
        }
        catch (Exception ex)
        {
            Log.Warn("hotkey init failed: " + ex.Message);
            return false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey) return IntPtr.Zero;
        var id = wParam.ToInt32();
        if (id == IdHud)
        {
            ToggleRequested?.Invoke();
            handled = true;
        }
        else if (id == IdMini)
        {
            MiniRequested?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Unregister()
    {
        if (_source is null) return;
        try
        {
            UnregisterHotKey(_source.Handle, IdHud);
            UnregisterHotKey(_source.Handle, IdMini);
            _source.RemoveHook(WndProc);
            _source.Dispose();
        }
        catch
        {
            // shutdown path
        }
        _source = null;
        _registered = false;
    }
}
