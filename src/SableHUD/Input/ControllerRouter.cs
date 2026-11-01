using System.Diagnostics;
using SableHUD.Core;

namespace SableHUD.Input;

public enum NavDir
{
    Up,
    Down,
    Left,
    Right,
}

/// <summary>
/// Turns the raw pad state into intent: the trigger gesture, tab paging, and
/// D-pad navigation with a repeat curve that feels right on a couch.
/// </summary>
public sealed class ControllerRouter : IDisposable
{
    private const ushort GuideMask = (ushort)PadButton.Guide;
    private readonly XInputService _pad;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<PadButton, long> _repeatAt = new();
    private readonly Dictionary<PadButton, long> _nextRepeat = new();

    private ushort _held;
    private long _lastToggle = -10_000;
    private long _lastGuidePress = -10_000;
    private long _guideDownAt = -1;
    private bool _guideHoldFired;
    private bool _chordLatch;
    private bool _customLatch;
    private NavDir? _stickDir;
    private long _stickNext;
    private bool _leftTriggerHeld;
    private bool _rightTriggerHeld;
    private long _leftTriggerNext;
    private long _rightTriggerNext;

    public Func<bool> IsHudVisible { get; set; } = () => false;
    public AppSettings Settings { get; set; }

    public event Action? ToggleHud;
    public event Action<NavDir>? Navigate;
    public event Action? Confirm;
    public event Action? Back;
    public event Action? Secondary;
    public event Action? MiniPlayer;
    public event Action? Menu;
    public event Action? PagePrev;
    public event Action? PageNext;
    public event Action<int>? Adjust;
    public event Action<PadInfo>? PadChanged;

    public ControllerRouter(XInputService pad, AppSettings settings)
    {
        _pad = pad;
        Settings = settings;
        _pad.ButtonsChanged += OnButtonsChanged;
        _pad.Tick += OnTick;
        _pad.PadChanged += info => PadChanged?.Invoke(info);
    }

    /// <summary>Injected only by the self-test so the trigger state machine can be verified without hardware.</summary>
    internal void InjectForTest(ushort pressed, ushort released) => OnButtonsChanged(pressed, released);
    internal void InjectTickForTest(PadState state) => OnTick(state);

    private void OnButtonsChanged(ushort pressed, ushort released)
    {
        if (!Settings.ControllerEnabled) return;
        _held = (ushort)((_held | pressed) & ~released);

        var p = (PadButton)pressed;
        var now = _clock.ElapsedMilliseconds;

        // ---- trigger gestures -------------------------------------------------
        if ((p & PadButton.Guide) != 0)
        {
            var previousGuide = _lastGuidePress;
            _lastGuidePress = now;
            _guideDownAt = now;
            _guideHoldFired = false;

            switch (Settings.Trigger)
            {
                case "Guide":
                    FireToggle(now);
                    break;
                case "GuideDouble":
                    if (now - previousGuide < 420) FireToggle(now);
                    break;
                case "GuideHold":
                    // handled in the tick loop
                    break;
            }
        }

        var chordNow = (_held & (ushort)(PadButton.LeftBumper | PadButton.RightBumper)) ==
                       (ushort)(PadButton.LeftBumper | PadButton.RightBumper);
        if (Settings.Trigger == "LbRb")
        {
            if (chordNow && !_chordLatch) FireToggle(now);
            _chordLatch = chordNow;
        }

        if (Settings.Trigger == "Custom")
        {
            var mask = ParseMask(Settings.CustomTrigger);
            var all = mask != 0 && (_held & mask) == mask;
            if (all && !_customLatch) FireToggle(now);
            _customLatch = all;
        }

        // Guide always closes an open HUD, even in chord modes.
        if ((p & PadButton.Guide) != 0 && Settings.Trigger != "Guide" && IsHudVisible())
            FireToggle(now);

        // ---- navigation -------------------------------------------------------
        if (!IsHudVisible()) return;

        if ((p & PadButton.LeftBumper) != 0) PagePrev?.Invoke();
        if ((p & PadButton.RightBumper) != 0) PageNext?.Invoke();

        if ((p & PadButton.A) != 0) Confirm?.Invoke();
        if ((p & PadButton.B) != 0) Back?.Invoke();
        if ((p & PadButton.X) != 0) Secondary?.Invoke();
        if ((p & PadButton.Y) != 0) MiniPlayer?.Invoke();
        if ((p & PadButton.Start) != 0) Menu?.Invoke();
        if ((p & PadButton.Back) != 0) Back?.Invoke();

        if ((p & PadButton.DPadUp) != 0) FireNav(NavDir.Up, now, true);
        if ((p & PadButton.DPadDown) != 0) FireNav(NavDir.Down, now, true);
        if ((p & PadButton.DPadLeft) != 0) FireNav(NavDir.Left, now, true);
        if ((p & PadButton.DPadRight) != 0) FireNav(NavDir.Right, now, true);
    }

    private void OnTick(PadState state)
    {
        if (!Settings.ControllerEnabled) return;
        var held = state.Buttons;
        _held = held;
        var now = _clock.ElapsedMilliseconds;

        // Guide-hold trigger.
        if (Settings.Trigger == "GuideHold" && _guideDownAt >= 0 &&
            (held & GuideMask) != 0 && !_guideHoldFired && now - _guideDownAt >= 560)
        {
            _guideHoldFired = true;
            FireToggle(now);
        }
        if ((held & GuideMask) == 0 && _guideDownAt >= 0)
        {
            _guideDownAt = -1;
            _guideHoldFired = false;
        }

        if (!IsHudVisible())
        {
            _stickDir = null;
            _leftTriggerHeld = _rightTriggerHeld = false;
            return;
        }

        // D-pad auto-repeat.
        foreach (var (button, dir) in new[]
                 {
                     (PadButton.DPadUp, NavDir.Up),
                     (PadButton.DPadDown, NavDir.Down),
                     (PadButton.DPadLeft, NavDir.Left),
                     (PadButton.DPadRight, NavDir.Right),
                 })
        {
            if ((held & (ushort)button) == 0)
            {
                _repeatAt.Remove(button);
                _nextRepeat.Remove(button);
                continue;
            }

            if (!_repeatAt.ContainsKey(button)) continue;
            if (now < _nextRepeat.GetValueOrDefault(button)) continue;

            var first = _repeatAt[button];
            if (now - first < 330) continue;

            Navigate?.Invoke(dir);
            _nextRepeat[button] = now + 105;
        }

        // Left stick walks the focus like a D-pad.
        var stick = StickDirection(state);
        if (stick is null)
        {
            _stickDir = null;
        }
        else if (stick != _stickDir)
        {
            _stickDir = stick;
            _stickNext = now + 350;
            Navigate?.Invoke(stick.Value);
        }
        else if (now >= _stickNext)
        {
            _stickNext = now + 115;
            Navigate?.Invoke(stick.Value);
        }

        // Analog triggers handle continuous values (seek, volume) so the D-pad
        // and stick never have to double as sliders.
        HandleTrigger(state.LeftTrigger > 140, ref _leftTriggerHeld, ref _leftTriggerNext, -1, now);
        HandleTrigger(state.RightTrigger > 140, ref _rightTriggerHeld, ref _rightTriggerNext, 1, now);
    }

    private static NavDir? StickDirection(PadState state)
    {
        const int dead = 17000;
        var ax = Math.Abs((int)state.LeftX);
        var ay = Math.Abs((int)state.LeftY);
        if (ax < dead && ay < dead) return null;
        if (ax >= ay)
            return state.LeftX > 0 ? NavDir.Right : NavDir.Left;
        return state.LeftY > 0 ? NavDir.Up : NavDir.Down;
    }

    private void HandleTrigger(bool down, ref bool held, ref long next, int direction, long now)
    {
        if (!down)
        {
            held = false;
            return;
        }

        if (!held)
        {
            held = true;
            next = now + 380;
            Adjust?.Invoke(direction);
            return;
        }

        if (now < next) return;
        next = now + 110;
        Adjust?.Invoke(direction);
    }

    private void FireNav(NavDir dir, long now, bool initial)
    {
        if (!initial) return;
        Navigate?.Invoke(dir);
        var key = dir switch
        {
            NavDir.Up => PadButton.DPadUp,
            NavDir.Down => PadButton.DPadDown,
            NavDir.Left => PadButton.DPadLeft,
            _ => PadButton.DPadRight,
        };
        if (!_repeatAt.ContainsKey(key)) _repeatAt[key] = now;
        _nextRepeat[key] = now + 340;
    }

    private void FireToggle(long now)
    {
        if (now - _lastToggle < 130) return;
        _lastToggle = now;
        if (Settings.Rumble) _pad.Rumble();
        ToggleHud?.Invoke();
    }

    public static ushort ParseMask(string? text)
    {
        ushort mask = 0;
        if (string.IsNullOrWhiteSpace(text)) return mask;
        foreach (var raw in text.Split(new[] { '+', ',', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.Trim();
            switch (token.ToLowerInvariant())
            {
                case "lb": mask |= (ushort)PadButton.LeftBumper; continue;
                case "rb": mask |= (ushort)PadButton.RightBumper; continue;
                case "view": mask |= (ushort)PadButton.Back; continue;
                case "menu": mask |= (ushort)PadButton.Start; continue;
                case "xbox":
                case "guide": mask |= (ushort)PadButton.Guide; continue;
            }

            if (Enum.TryParse<PadButton>(token, ignoreCase: true, out var b))
                mask |= (ushort)b;
        }
        return mask;
    }

    public void Dispose()
    {
        _pad.ButtonsChanged -= OnButtonsChanged;
        _pad.Tick -= OnTick;
    }
}
