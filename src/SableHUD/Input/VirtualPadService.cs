using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using SableHUD.Core;

namespace SableHUD.Input;

/// <summary>
/// Presents a virtual Xbox 360 pad to games while the real pad is hidden from
/// them (HidHide). Input is forwarded while the HUD is closed; the moment the
/// HUD opens the virtual pad goes neutral, so a running game stops receiving
/// input without needing a restart.
/// </summary>
public sealed class VirtualPadService : IDisposable
{
    private readonly object _gate = new();
    private ViGEmClient? _client;
    private IXbox360Controller? _pad;
    private long _lastSubmit;
    private System.Threading.Timer? _stale;

    public bool IsReady { get; private set; }
    public bool Forwarding { get; private set; }

    public bool Start()
    {
        try
        {
            _client = new ViGEmClient();
            _pad = _client.CreateXbox360Controller();
            _pad.AutoSubmitReport = false;
            _pad.Connect();
            IsReady = true;
            Neutral();
            _stale = new System.Threading.Timer(_ => CheckStale(), null, 100, 150);
            Log.Info("virtual pad connected");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("virtual pad: " + ex.Message);
            Dispose();
            return false;
        }
    }

    public void SetForwarding(bool forward)
    {
        lock (_gate)
        {
            Forwarding = forward;
            if (!forward) NeutralLocked();
        }

        Log.Info("virtual pad forwarding=" + forward);
    }

    public void Submit(PadState state)
    {
        if (!IsReady || _pad is null) return;
        lock (_gate)
        {
            // Check inside the lock: a tick that raced the HUD opening must not
            // overwrite the neutral report with the last real-pad state.
            if (!Forwarding) return;
            try
            {
                _pad.SetButtonsFull(state.Buttons);
                _pad.SetAxisValue(Xbox360Axis.LeftThumbX, state.LeftX);
                _pad.SetAxisValue(Xbox360Axis.LeftThumbY, state.LeftY);
                _pad.SetAxisValue(Xbox360Axis.RightThumbX, state.RightX);
                _pad.SetAxisValue(Xbox360Axis.RightThumbY, state.RightY);
                _pad.SetSliderValue(Xbox360Slider.LeftTrigger, state.LeftTrigger);
                _pad.SetSliderValue(Xbox360Slider.RightTrigger, state.RightTrigger);
                _pad.SubmitReport();
                _lastSubmit = Environment.TickCount64;
            }
            catch (Exception ex)
            {
                Log.Warn("virtual pad submit: " + ex.Message);
            }
        }
    }

    public void Neutral()
    {
        lock (_gate) NeutralLocked();
    }

    private void NeutralLocked()
    {
        if (_pad is null) return;
        try
        {
            _pad.SetButtonsFull(0);
            _pad.SetAxisValue(Xbox360Axis.LeftThumbX, 0);
            _pad.SetAxisValue(Xbox360Axis.LeftThumbY, 0);
            _pad.SetAxisValue(Xbox360Axis.RightThumbX, 0);
            _pad.SetAxisValue(Xbox360Axis.RightThumbY, 0);
            _pad.SetSliderValue(Xbox360Slider.LeftTrigger, 0);
            _pad.SetSliderValue(Xbox360Slider.RightTrigger, 0);
            _pad.SubmitReport();
            _lastSubmit = Environment.TickCount64;
        }
        catch (Exception ex)
        {
            Log.Warn("virtual pad neutral: " + ex.Message);
        }
    }

    private void CheckStale()
    {
        if (!IsReady || !Forwarding) return;
        if (Environment.TickCount64 - _lastSubmit > 350) Neutral();
    }

    public void Dispose()
    {
        IsReady = false;
        Forwarding = false;
        try { _stale?.Dispose(); } catch { }
        _stale = null;
        try { _pad?.Disconnect(); } catch { }
        try { _client?.Dispose(); } catch { }
        _pad = null;
        _client = null;
    }
}
