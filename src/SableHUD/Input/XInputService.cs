using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SableHUD.Input;

[Flags]
public enum PadButton : ushort
{
    None = 0,
    DPadUp = 0x0001,
    DPadDown = 0x0002,
    DPadLeft = 0x0004,
    DPadRight = 0x0008,
    Start = 0x0010,
    Back = 0x0020,
    LeftStick = 0x0040,
    RightStick = 0x0080,
    LeftBumper = 0x0100,
    RightBumper = 0x0200,
    Guide = 0x0400,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000,
}

public enum BatteryLevel
{
    Empty,
    Low,
    Medium,
    Full,
    Wired,
    Unknown,
}

public readonly record struct PadInfo(bool Connected, int Slot, BatteryLevel Battery, bool Wireless);

public readonly record struct PadState(ushort Buttons, short LeftX, short LeftY, short RightX, short RightY, byte LeftTrigger, byte RightTrigger);

/// <summary>
/// Polls XInput. The Guide/Xbox button is only visible through the undocumented
/// ordinal 100 export (XInputGetStateEx); the documented entry point hides it.
/// </summary>
public sealed class XInputService : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint dwPacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputVibration
    {
        public ushort wLeftMotorSpeed;
        public ushort wRightMotorSpeed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputBatteryInformation
    {
        public byte BatteryType;
        public byte BatteryLevel;
    }

    private delegate uint GetStateDelegate(int index, out XInputState state);

    private static readonly GetStateDelegate? GetStateEx =
        TryBind("#100", "xinput1_4.dll") ?? TryBind("#100", "xinput1_3.dll");

    private static readonly GetStateDelegate? GetStateDoc =
        TryBind("XInputGetState", "xinput1_4.dll") ?? TryBind("XInputGetState", "xinput9_1_0.dll");

    public static bool GuideButtonSupported => GetStateEx is not null;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryW(string path);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr module, IntPtr ordinal);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    private static GetStateDelegate? TryBind(string entry, string dll)
    {
        try
        {
            var module = LoadLibraryW(dll);
            if (module == IntPtr.Zero) return null;

            IntPtr fn;
            if (entry.StartsWith('#'))
            {
                // XInputGetStateEx is exported by ordinal only. GetProcAddress by
                // name cannot see it, which is why the Guide button "disappears".
                if (!int.TryParse(entry.AsSpan(1), out var ordinal)) return null;
                fn = GetProcAddress(module, new IntPtr(ordinal));
            }
            else
            {
                fn = GetProcAddress(module, entry);
            }

            return fn == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<GetStateDelegate>(fn);
        }
        catch
        {
            return null;
        }
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
    private static extern uint XInputSetState(int index, ref XInputVibration vibration);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetBatteryInformation")]
    private static extern uint XInputGetBatteryInformation(int index, byte deviceType, out XInputBatteryInformation info);

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _gate = new();
    private Thread? _thread;
    private volatile bool _running;
    private ushort _lastButtons;
    private int _slot = -1;
    private long _lastBatteryCheck;
    private PadInfo _info;

    public event Action<ushort, ushort>? ButtonsChanged;
    public event Action<PadState>? Tick;
    public event Action<PadInfo>? PadChanged;

    public PadInfo Pad => _info;
    public int PinnedSlot { get; set; } = -1;

    public XInputService()
    {
        _info = new PadInfo(false, -1, BatteryLevel.Unknown, false);
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "Sable.XInput",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
    }

    private static uint GetState(int slot, out XInputState state)
    {
        if (GetStateEx is not null) return GetStateEx(slot, out state);
        if (GetStateDoc is not null) return GetStateDoc(slot, out state);
        state = default;
        return 1167; // ERROR_DEVICE_NOT_CONNECTED
    }

    private void Loop()
    {
        while (_running)
        {
            var found = -1;
            XInputState state = default;

            // With a virtual pad enabled the real pad is pinned so Sable never
            // reads its own virtual controller by mistake.
            if (PinnedSlot >= 0 && GetState(PinnedSlot, out state) == 0)
            {
                found = PinnedSlot;
            }
            else if (PinnedSlot < 0 && _slot >= 0 && GetState(_slot, out state) == 0)
            {
                found = _slot;
            }
            else
            {
                for (var i = 0; i < 4; i++)
                {
                    if (GetState(i, out state) == 0)
                    {
                        found = i;
                        break;
                    }
                }
            }

            if (found >= 0)
            {
                var buttons = state.Gamepad.wButtons;
                if (_slot != found)
                {
                    _slot = found;
                    _lastButtons = 0;
                    RefreshBattery(force: true);
                }

                var pressed = (ushort)(buttons & ~_lastButtons);
                var released = (ushort)(~buttons & _lastButtons);
                _lastButtons = buttons;

                if (pressed != 0 || released != 0)
                    ButtonsChanged?.Invoke(pressed, released);
                Tick?.Invoke(new PadState(buttons, state.Gamepad.sThumbLX, state.Gamepad.sThumbLY, state.Gamepad.sThumbRX, state.Gamepad.sThumbRY, state.Gamepad.bLeftTrigger, state.Gamepad.bRightTrigger));

                if (_clock.ElapsedMilliseconds - _lastBatteryCheck > 10_000)
                    RefreshBattery(force: false);
            }
            else if (_slot != -1)
            {
                _slot = -1;
                _lastButtons = 0;
                SetPad(new PadInfo(false, -1, BatteryLevel.Unknown, false));
            }

            Thread.Sleep(8);
        }
    }

    private void RefreshBattery(bool force)
    {
        _lastBatteryCheck = _clock.ElapsedMilliseconds;
        if (_slot < 0) return;
        try
        {
            if (XInputGetBatteryInformation(_slot, 0, out var b) != 0) return;
            var wireless = b.BatteryType is 2 or 3;
            var level = b.BatteryType switch
            {
                1 => BatteryLevel.Wired,
                2 or 3 => (BatteryLevel)Math.Clamp((int)b.BatteryLevel, 0, 3),
                0 => BatteryLevel.Unknown,
                _ => BatteryLevel.Unknown,
            };
            SetPad(new PadInfo(true, _slot, level, wireless));
        }
        catch
        {
            if (force) SetPad(new PadInfo(true, _slot, BatteryLevel.Unknown, false));
        }
    }

    private void SetPad(PadInfo info)
    {
        if (_info == info) return;
        _info = info;
        PadChanged?.Invoke(info);
    }

    public void Rumble(ushort left = 0x2E00, ushort right = 0x1E00, int ms = 70)
    {
        var slot = _slot;
        if (slot < 0) return;
        Task.Run(async () =>
        {
            try
            {
                var v = new XInputVibration { wLeftMotorSpeed = left, wRightMotorSpeed = right };
                XInputSetState(slot, ref v);
                await Task.Delay(ms).ConfigureAwait(false);
                v = default;
                XInputSetState(slot, ref v);
            }
            catch
            {
                // a disconnected pad is not an error
            }
        });
    }

    public void Dispose()
    {
        _running = false;
        try { _thread?.Join(250); } catch { }
    }
}
