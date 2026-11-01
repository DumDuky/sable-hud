using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using SableHUD.Core;
using SableHUD.Input;
using SableHUD.Media;
using SableHUD.Shared;
using SableHUD.SystemInfo;
using SableHUD.Windows;
using SableHUD.Widgets;

namespace SableHUD;

public partial class App : Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private EventWaitHandle? _toggleEvent;
    private EventWaitHandle? _settingsEvent;
    private EventWaitHandle? _exitEvent;
    private Thread? _listener;
    private volatile bool _listening;

    private readonly System.Threading.SemaphoreSlim _isolationGate = new(1, 1);
    private bool _isolationHintShown;
    private bool _virtualStarted;
    private bool _watchdogStarted;
    private DispatcherTimer? _cloakGuard;
    private bool _cloakGuardBusy;
    private OverlayWindow? _overlay;
    private MiniPlayerWindow? _mini;
    private ToastWindow? _toast;
    private SettingsWindow? _settings;
    private TrayService? _tray;
    private IReadOnlyList<IHudPage> _allPages = Array.Empty<IHudPage>();
    private string _pageSignature = "";
    private string _lastTrackKey = "";
    private DateTime _suppressToastsUntil = DateTime.MinValue;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("unhandled: " + (args.ExceptionObject as Exception)?.Message);

        var args = e.Args;

        if (Has(args, "--selftest"))
        {
            var ok = false;
            try { ok = SelfTest.Run(args); }
            catch (Exception ex) { Log.Error("selftest: " + ex); }
            Shutdown(ok ? 0 : 1);
            return;
        }

        if (Has(args, "--render"))
        {
            try { DemoRender.Run(args); }
            catch (Exception ex) { Log.Error("render: " + ex); }
            Shutdown(0);
            return;
        }

        if (!AcquireSingleInstance())
        {
            SignalExisting(args);
            Shutdown(0);
            return;
        }

        if (Has(args, "--exit"))
        {
            Shutdown(0);
            return;
        }

        try
        {
            Initialize(args);
        }
        catch (Exception ex)
        {
            Log.Error("startup: " + ex);
            MessageBox.Show(
                "Sable could not start.\n\n" + ex.Message + "\n\nDetails: " + Paths.LogFile,
                "Sable HUD",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Shutdown(1);
        }
    }

    private bool AcquireSingleInstance()
    {
        _mutex = new Mutex(true, @"Local\SableHUD.Mutex", out var created);
        return created;
    }

    private static void SignalExisting(string[] args)
    {
        var name = Has(args, "--settings") ? "Settings"
            : Has(args, "--exit") ? "Exit"
            : Has(args, "--show") ? "Show"
            : "Toggle";

        for (var i = 0; i < 50; i++)
        {
            try
            {
                using var handle = EventWaitHandle.OpenExisting(@"Local\SableHUD." + name);
                handle.Set();
                return;
            }
            catch
            {
                Thread.Sleep(80);
            }
        }
    }

    private void Initialize(string[] args)
    {
        Services.Settings = SettingsStore.Load();
        ThemeManager.Apply(Services.Settings.Theme);
        Services.SaveSettings = ApplySettings;

        Services.Media = new MediaCoordinator(new LocalPlayer(), Services.Settings.LocalVolume);
        Services.Media.LiMusic.Changed += () =>
        {
            if (Services.Media.LiMusic.IsConnected)
                _ = Services.Media.LiMusic.SetAutoplayAsync(Services.Settings.Autoplay);
        };
        _ = Services.Media.LiMusic.TryConnectAsync();
        Services.Monitor = new SystemMonitor();
        Services.Pad = new XInputService();
        Services.Pad.Start();
        Services.Router = new ControllerRouter(Services.Pad, Services.Settings)
        {
            IsHudVisible = () => _overlay?.IsShown == true || _settings?.IsOpen == true,
        };

        _allPages = new IHudPage[]
        {
            new MusicView(),
            new PerformanceView(),
            new AudioView(),
            new ToolsView(),
        };

        _overlay = new OverlayWindow(ActivePages(), Services.Settings.DefaultTab);
        _overlay.ScaleTo(Services.Settings.ScaleFactor);
        _overlay.Shown += OnOverlayShown;
        _overlay.Hidden += OnOverlayHidden;
        _overlay.Prewarm();

        _settings = new SettingsWindow();
        _settings.Dismissed += OnSettingsDismissed;

        _mini = new MiniPlayerWindow();
        _toast = new ToastWindow();

        // Safety net: never leave the controller cloaked with no HUD open.
        _cloakGuard = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
        _cloakGuard.Tick += async (_, _) =>
        {
            if (_cloakGuardBusy || _overlay?.IsShown == true) return;

            if (Services.VirtualIsolation)
            {
                if (Services.Settings.IsolateController) return; // hidden by design
                Services.VirtualPad.Dispose();
                Services.VirtualIsolation = false;
                Services.Pad.PinnedSlot = -1;
                await HidHide.SetCloakAsync(false).ConfigureAwait(true);
                return;
            }

            if (!Services.Settings.IsolateController || !HidHide.IsInstalled) return;
            _cloakGuardBusy = true;
            try
            {
                if (await HidHide.IsCloakedAsync().ConfigureAwait(true))
                    await HidHide.SetCloakAsync(false).ConfigureAwait(true);
            }
            catch (Exception ex) { Log.Warn("cloak guard: " + ex.Message); }
            finally { _cloakGuardBusy = false; }
        };
        _cloakGuard.Start();

        Services.Cover.Changed += () => OnUi(() =>
        {
            _mini?.Refresh();
            if (_overlay?.IsShown == true) _overlay.RefreshPage();
        });

        _pageSignature = PageSignature();
        WireRouter();
        WireMedia();
        WireTray();
        WireHotkeys();

        if (Services.Settings.IsolateController && HidHide.IsInstalled)
            _ = StartVirtualIsolationAsync();

        if (!string.IsNullOrWhiteSpace(Services.Settings.MusicFolder))
            _ = Services.Media.Local.ScanAsync(Services.Settings.MusicFolder);

        Autostart.Set(Services.Settings.StartWithWindows);
        if (Services.Settings.IsolateController && HidHide.IsInstalled)
            _ = PrepareIsolationAsync();

        Hud.ShowRequested = tab => OnUi(() => ShowHud(tab));
        Hud.HideRequested = () => OnUi(HideHud);
        Hud.HideImmediateRequested = () => OnUi(() => _overlay?.HideImmediate());
        Hud.ToggleRequested = () => OnUi(ToggleHud);
        Hud.MiniRequested = () => OnUi(ToggleMini);
        Hud.SettingsRequested = () => OnUi(() => OpenSettings());
        Hud.SettingsSectionRequested = section => OnUi(() => OpenSettings(section));
        Hud.ToastRequested = OnToastRequested;
        Hud.IsVisible = () => _overlay?.IsShown == true;

        StartCommandListener();
        ApplySettings();

        var firstRun = Services.Settings.FirstRun;
        if (firstRun)
        {
            Services.Settings.FirstRun = false;
            SettingsStore.Save(Services.Settings);
        }

        if (Services.Settings.ShowMiniPlayer && !firstRun)
            _mini.ShowMini();

        var tab = ArgValue(args, "--tab");
        if (Has(args, "--show"))
            Dispatcher.BeginInvoke(() => ShowHud(tab), DispatcherPriority.ApplicationIdle);
        else if (Has(args, "--settings"))
            Dispatcher.BeginInvoke(() => OpenSettings(), DispatcherPriority.ApplicationIdle);
        else if (Has(args, "--toggle"))
            Dispatcher.BeginInvoke(ToggleHud, DispatcherPriority.ApplicationIdle);
        else if (firstRun && !Has(args, "--autostart"))
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                ShowHud("Music");
                _tray?.Balloon("Sable is ready", "Press the Xbox button once, or Ctrl+Alt+H. In Settings, turn off Game Bar's controller shortcut.");
            };
            timer.Start();
        }
    }

    private IReadOnlyList<IHudPage> ActivePages()
    {
        var pages = new List<IHudPage>();
        if (Services.Settings.ShowMusic) pages.Add(_allPages[0]);
        if (Services.Settings.ShowPerformance) pages.Add(_allPages[1]);
        if (Services.Settings.ShowAudio) pages.Add(_allPages[2]);
        if (Services.Settings.ShowTools) pages.Add(_allPages[3]);
        if (pages.Count == 0) pages.Add(_allPages[0]);
        return pages;
    }

    private string PageSignature() =>
        $"{Services.Settings.ShowMusic}|{Services.Settings.ShowPerformance}|{Services.Settings.ShowAudio}|{Services.Settings.ShowTools}";

    private void WireRouter()
    {
        Services.Router.ToggleHud += () => OnUi(ToggleHud);
        Services.Router.Navigate += dir => OnUi(() => RouteNav(dir));
        Services.Router.Adjust += dir => OnUi(() => RouteAdjust(dir));
        Services.Router.Confirm += () => OnUi(RouteConfirm);
        Services.Router.Back += () => OnUi(RouteBack);
        Services.Router.Secondary += () => OnUi(RouteSecondary);
        Services.Router.MiniPlayer += () => OnUi(ToggleMini);
        Services.Router.Menu += () => OnUi(RouteMenu);
        Services.Router.PagePrev += () => OnUi(() => RoutePage(-1));
        Services.Router.PageNext += () => OnUi(() => RoutePage(1));
        Services.Router.PadChanged += info =>
        {
            OnUi(() => _overlay?.SetPad(info));
            if (info.Connected && Services.Settings.IsolateController && !Services.VirtualIsolation)
                OnUi(() => _ = StartVirtualIsolationAsync());
        };
    }

    private void WireMedia()
    {
        _lastTrackKey = Services.Media.Current.Key;
        _suppressToastsUntil = DateTime.UtcNow.AddSeconds(6);
        Services.Media.Changed += snap =>
        {
            if (snap.Key == _lastTrackKey) return;
            _lastTrackKey = snap.Key;
            if (DateTime.UtcNow < _suppressToastsUntil) return;
            if (!snap.HasTrack || !Services.Settings.ToastOnTrackChange) return;
            if (_overlay?.IsShown == true) return;
            if (_mini?.IsVisible == true) return;
            if (_settings?.IsOpen == true) return;
            _toast?.ShowTrack(snap);
        };
    }

    private void WireTray()
    {
        _tray = new TrayService
        {
            MiniChecked = () => Services.Settings.ShowMiniPlayer,
            AutostartChecked = () =>
            {
                Services.Settings.StartWithWindows = !Services.Settings.StartWithWindows;
                Autostart.Set(Services.Settings.StartWithWindows);
                SettingsStore.Save(Services.Settings);
                OnUi(ApplySettings);
                return Services.Settings.StartWithWindows;
            },
        };
        _tray.ShowHud += () => OnUi(() => ShowHud(null));
        _tray.ToggleMini += () => OnUi(ToggleMini);
        _tray.OpenSettings += () => OnUi(() => OpenSettings());
        _tray.ReleaseController += () => OnUi(() =>
        {
            HidHide.ReleaseCloakBlocking();
            _tray?.Balloon("Controller released", "HidHide cloaking is off");
        });
        _tray.Exit += () => OnUi(ExitApp);
    }

    private void WireHotkeys()
    {
        Services.Hotkeys.ToggleRequested += () => OnUi(ToggleHud);
        Services.Hotkeys.MiniRequested += () => OnUi(ToggleMini);
        if (Services.Media.LiMusic.IsConnected)
            _ = Services.Media.LiMusic.SetAutoplayAsync(Services.Settings.Autoplay);

        if (Services.Settings.KeyboardHotkey) Services.Hotkeys.Register();
    }

    private void StartCommandListener()
    {
        if (_mutex is null) return;
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\SableHUD.Show");
        _toggleEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\SableHUD.Toggle");
        _settingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\SableHUD.Settings");
        _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\SableHUD.Exit");

        _listening = true;
        _listener = new Thread(() =>
        {
            var handles = new WaitHandle[] { _showEvent, _toggleEvent, _settingsEvent, _exitEvent };
            while (_listening)
            {
                int index;
                try { index = WaitHandle.WaitAny(handles); }
                catch { break; }

                if (!_listening) break;
                Dispatcher.BeginInvoke(() =>
                {
                    switch (index)
                    {
                        case 0: ShowHud(null); break;
                        case 1: ToggleHud(); break;
                        case 2: OpenSettings(); break;
                        case 3: ExitApp(); break;
                    }
                });
            }
        })
        {
            IsBackground = true,
            Name = "Sable.Commands",
        };
        _listener.Start();
    }

    public void ShowHud(string? tab)
    {
        if (_settings?.IsOpen == true) _settings.Dismiss();
        _overlay?.ShowHud(tab);
    }

    public void HideHud() => _overlay?.HideHud();

    public void ToggleHud()
    {
        if (_settings?.IsOpen == true)
        {
            _settings.Dismiss();
            _overlay?.ShowHud(null);
            return;
        }
        if (_overlay?.IsShown == true) _overlay.HideHud();
        else _overlay?.ShowHud(null);
    }

    public void ToggleMini()
    {
        Services.Settings.ShowMiniPlayer = !Services.Settings.ShowMiniPlayer;
        SettingsStore.Save(Services.Settings);

        if (Services.Settings.ShowMiniPlayer)
        {
            if (_overlay?.IsShown != true && _settings?.IsOpen != true) _mini?.ShowMini();
            _overlay?.SetStatus("Mini player on");
            Hud.Toast("Mini player on");
        }
        else
        {
            _mini?.HideMini();
            _overlay?.SetStatus("Mini player off");
            Hud.Toast("Mini player off");
        }
    }

    public void OpenSettings(string? section = null)
    {
        if (_overlay?.IsShown == true) _overlay.HideImmediate();
        _mini?.HideMini();
        _toast?.HideToast();
        _settings?.Open(section);
    }

    private void OnSettingsDismissed()
    {
        if (Services.Settings.ShowMiniPlayer && _overlay?.IsShown != true)
            _mini?.ShowMini();
    }

    private void OnOverlayShown()
    {
        _mini?.HideMini();
        _toast?.HideToast();

        if (Services.VirtualIsolation)
        {
            // The real pad is already hidden from games; make the virtual pad
            // neutral so a running game stops receiving input immediately.
            Services.VirtualPad.SetForwarding(false);
        }
    }

    private async Task StartVirtualIsolationAsync()
    {
        if (_virtualStarted || Services.VirtualIsolation ||
            !Services.Settings.IsolateController || !HidHide.IsInstalled)
            return;

        _virtualStarted = true;
        try
        {
            // Wait for the real pad so its slot can be pinned before the
            // virtual pad is created. Sable must never read its own virtual pad.
            for (var i = 0; i < 60 && !Services.Pad.Pad.Connected; i++)
                await Task.Delay(50).ConfigureAwait(true);
            if (!Services.Pad.Pad.Connected)
            {
                _virtualStarted = false;
                return;
            }

            Services.Pad.PinnedSlot = Services.Pad.Pad.Slot;

            // Hide the real pad before creating the virtual one. If the virtual
            // pad existed first, PrepareAsync would enumerate and hide it too,
            // leaving games with no controller at all.
            var (ok, message) = await HidHide.PrepareAsync().ConfigureAwait(true);
            if (!ok)
            {
                Log.Warn("virtual isolation: " + message);
                Services.Pad.PinnedSlot = -1;
                _virtualStarted = false;
                return;
            }

            if (!Services.VirtualPad.Start())
            {
                await HidHide.SetCloakAsync(false).ConfigureAwait(true);
                Services.Pad.PinnedSlot = -1;
                _virtualStarted = false;
                return;
            }

            Services.Pad.Tick += state => Services.VirtualPad.Submit(state);
            Services.VirtualPad.SetForwarding(true);

            await HidHide.SetCloakAsync(true).ConfigureAwait(true);
            Services.VirtualIsolation = true;
            StartCloakWatchdog();
            Log.Info("virtual isolation active");
            Hud.Toast("Virtual pad active · restart games once so they use it");
        }
        catch (Exception ex)
        {
            Log.Warn("virtual isolation: " + ex.Message);
            Services.VirtualPad.Dispose();
            Services.VirtualIsolation = false;
            _virtualStarted = false;
        }
    }

    private void StartCloakWatchdog()
    {
        if (_watchdogStarted) return;
        _watchdogStarted = true;
        try
        {
            var cli = HidHide.CliPath;
            var command =
                $"-NoProfile -WindowStyle Hidden -Command \"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 800; if (-not (Get-Process -Name SableHUD -ErrorAction SilentlyContinue)) {{ & '{cli}' --cloak-off }}\"";
            Process.Start(new ProcessStartInfo("powershell.exe", command)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }
        catch (Exception ex)
        {
            Log.Warn("cloak watchdog: " + ex.Message);
        }
    }

    private async Task PrepareIsolationAsync()
    {
        if (Services.VirtualIsolation) return;
        await _isolationGate.WaitAsync().ConfigureAwait(true);
        try
        {
            var (ok, message) = await HidHide.PrepareAsync().ConfigureAwait(true);
            if (!ok)
            {
                Log.Warn("isolation prepare: " + message);
                return;
            }

            if (_overlay?.IsShown == true)
            {
                await HidHide.SetCloakAsync(true).ConfigureAwait(true);

                if (!_isolationHintShown)
                {
                    _isolationHintShown = true;
                    Hud.Toast("Pad hidden · reload an app that was already open");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("isolation prepare: " + ex.Message);
        }
        finally
        {
            _isolationGate.Release();
        }
    }

    private void OnOverlayHidden()
    {
        if (Services.VirtualIsolation)
        {
            Services.VirtualPad.SetForwarding(true);
        }
        else if (Services.Settings.IsolateController && HidHide.IsInstalled)
        {
            _ = HidHide.SetCloakAsync(false);
        }

        if (Services.Settings.ShowMiniPlayer && _settings?.IsOpen != true)
            _mini?.ShowMini();
    }

    private void RouteNav(NavDir dir)
    {
        if (_settings?.IsOpen == true) _settings.Navigate(dir);
        else _overlay?.Navigate(dir);
    }

    private void RouteAdjust(int direction)
    {
        if (_settings?.IsOpen == true)
            _settings.Navigate(direction < 0 ? NavDir.Left : NavDir.Right);
        else
            _overlay?.Adjust(direction);
    }

    private void RouteConfirm()
    {
        if (_settings?.IsOpen == true) _settings.Confirm();
        else _overlay?.Confirm();
    }

    private void RouteBack()
    {
        if (_settings?.IsOpen == true) _settings.Dismiss();
        else if (_overlay?.Back() == true) { }
        else HideHud();
    }

    private void RouteSecondary()
    {
        if (_settings?.IsOpen == true) _settings.Dismiss();
        else _overlay?.Secondary();
    }

    private void RouteMenu()
    {
        if (_settings?.IsOpen == true) _settings.Dismiss();
        else if (_overlay?.IsShown == true) OpenSettings();
    }

    private void RoutePage(int delta)
    {
        if (_settings?.IsOpen == true) _settings.CycleSection(delta);
        else if (_overlay?.IsShown == true) _overlay.CyclePage(delta);
    }

    private void OnToastRequested(string text)
    {
        if (_overlay?.IsShown == true)
        {
            _overlay.SetStatus(text);
            return;
        }
        if (_settings?.IsOpen == true) return;
        _toast?.ShowMessage(text);
    }

    private void ApplySettings()
    {
        ThemeManager.Apply(Services.Settings.Theme);
        _overlay?.ScaleTo(Services.Settings.ScaleFactor);
        _overlay?.SetDefaultTab(Services.Settings.DefaultTab);
        _overlay?.SetHotkeyHint(Services.Settings.KeyboardHotkey);

        var signature = PageSignature();
        if (signature != _pageSignature)
        {
            _pageSignature = signature;
            _overlay?.ConfigurePages(ActivePages(), Services.Settings.DefaultTab);
        }

        if (Services.Media.LiMusic.IsConnected)
            _ = Services.Media.LiMusic.SetAutoplayAsync(Services.Settings.Autoplay);

        if (Services.Settings.KeyboardHotkey) Services.Hotkeys.Register();
        else Services.Hotkeys.Unregister();

        if (Services.Settings.ShowMiniPlayer && _overlay?.IsShown != true && _settings?.IsOpen != true)
        {
            _mini?.ShowMini();
            _mini?.Reposition();
        }
        else if (!Services.Settings.ShowMiniPlayer)
            _mini?.HideMini();

        if (!Services.Settings.ToastOnTrackChange) _toast?.HideToast();
    }

    private void OnUi(Action action)
    {
        if (Dispatcher.CheckAccess()) action();
        else Dispatcher.BeginInvoke(action);
    }

    private static bool Has(string[] args, string flag) =>
        args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

    private static string? ArgValue(string[] args, string flag)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    private void ExitApp()
    {
        Shutdown(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _listening = false;
        try { _exitEvent?.Set(); } catch { }
        try { _listener?.Join(500); } catch { }

        try { _settings?.AllowClose(); _settings?.Close(); } catch { }
        try { _tray?.Dispose(); } catch { }
        try { _overlay?.Close(); } catch { }
        try { _mini?.Close(); } catch { }
        _cloakGuard?.Stop();
        try { _toast?.Close(); } catch { }

        try { Services.VirtualPad?.Dispose(); } catch { }
        Services.VirtualIsolation = false;
        try { if (Services.Pad is not null) Services.Pad.PinnedSlot = -1; } catch { }

        if (Services.Settings.IsolateController && HidHide.IsInstalled)
        {
            try { HidHide.ReleaseCloakBlocking(5000); } catch { }
        }

        try { Services.Media?.Dispose(); } catch { }
        try { Services.Audio?.Dispose(); } catch { }
        try { Services.Monitor?.Dispose(); } catch { }
        try { Services.Pad?.Dispose(); } catch { }
        try { Services.Router?.Dispose(); } catch { }
        try { Services.Hotkeys?.Unregister(); } catch { }

        try
        {
            if (_mutex is not null)
            {
                _mutex.ReleaseMutex();
                _mutex.Dispose();
            }
        }
        catch { }

        base.OnExit(e);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("dispatcher: " + e.Exception);
        try { Services.VirtualPad?.Dispose(); } catch { }
        Services.VirtualIsolation = false;
        try { HidHide.ReleaseCloakBlocking(1500); } catch { }
        e.Handled = true;
    }
}
