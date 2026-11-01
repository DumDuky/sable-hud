using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SableHUD.Core;

public static class WindowFx
{
    private const int GwlExStyle = -20;
    private const long WsExTopmost = 0x00000008;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExTransparent = 0x00000020;
    private const long WsExNoActivate = 0x08000000;
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    public static void MakeOverlay(Window window, bool clickThrough = false)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var ex = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        ex |= WsExTopmost | WsExToolWindow | WsExNoActivate;
        if (clickThrough) ex |= WsExTransparent;
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(ex));

        // Keep it above borderless games without stealing activation.
        SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040);

        HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg == WmMouseActivate)
            {
                handled = true;
                return new IntPtr(MaNoActivate);
            }
            return IntPtr.Zero;
        });
    }

    public static void MakeToolWindow(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var ex = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        ex |= WsExToolWindow;
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(ex));
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public static uint ForegroundProcessId()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    public static Rect PrimaryWorkArea()
    {
        var wa = SystemParameters.WorkArea;
        return new Rect(wa.Left, wa.Top, wa.Width, wa.Height);
    }
}
