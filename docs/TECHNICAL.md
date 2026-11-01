# Sable HUD — technical notes

## Shape of the app

Sable is a single WPF process targeting `net8.0-windows10.0.19041.0`. It has no
background service, no driver, no injected DLL, and no network access. Everything is
local Windows API surface:

- XInput for the pad and Guide button
- Global hotkeys for the keyboard fallback
- Win32 window styles for a non-activating always-on-top overlay
- Windows Media Transport Controls (SMTC) for the music session
- Core Audio for volume and app sessions
- PDH for GPU counters
- RTSS shared memory for optional FPS
- `GetSystemTimes` / `GlobalMemoryStatusEx` / `GetSystemPowerStatus` for the rest

`App.xaml.cs` owns lifetime, single-instance signaling, settings, and the small amount
of routing between the controller and whichever window is active.

## Window behaviour

`OverlayWindow` is `AllowsTransparency`, `WindowStyle=None`, `Topmost`, with the extended
styles `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST`. `WM_MOUSEACTIVATE` returns
`MA_NOACTIVATE`, so clicking a button in the HUD does not pull focus away from the game.
The window is hidden when idle and shown with a 320 ms top-drop and 170 ms fade.

The overlay is positioned with `SystemParameters.WorkArea` (DIPs) at the top centre of
the primary monitor. `HUD size` applies a `LayoutTransform` and resizes the window
around it. Auto-hide is a dispatcher timer; every controller/mouse interaction resets it.

Mini player and toast use the same no-activate style. The toast is additionally
`WS_EX_TRANSPARENT` and `IsHitTestVisible=false`, so it can never eat a click. The mini
player is draggable with manual screen-space math (DPI-aware), and persists its
position and a `MiniPlaced` flag.

## Controller pipeline

`XInputService` polls all four XInput slots at ~125 Hz on a dedicated thread. The Guide
button is only present in the undocumented `XInputGetStateEx` export (ordinal 100), so
the service binds it with `LoadLibrary` + `GetProcAddress(ordinal)` and falls back to
the documented `XInputGetState` if unavailable.

`ControllerRouter` turns edges into intent:

- trigger gestures: `Guide`, `GuideDouble`, `GuideHold`, `LbRb`, `Custom`
- tab paging: LB/RB
- navigation: **D-pad and left stick, all four directions**, with a 350 ms stick
  initial delay and 115 ms repeat
- analog triggers: `Adjust(-1/+1)` for seek or volume, with hold repeat; they never
  compete with focus movement
- actions: A, B, X, Y, Menu, View

While the HUD is hidden, navigation is not emitted. While Settings is open, the same
router drives Settings rows and left/right changes values. Rumble is a short 70 ms
pulse on open/close (configurable).

The overlay refreshes the active page every 250 ms. This is what keeps Performance,
playback progress and the audio mixer live; pages do not own their own polling clocks.

The router runs on the XInput thread and posts to the WPF dispatcher. It never blocks the
game's thread.

## Music

`SmtcService` runs a background async loop:

1. `GlobalSystemMediaTransportControlsSessionManager.RequestAsync()`
2. choose the current/playing session
3. `TryGetMediaPropertiesAsync()` for title, artist, album and thumbnail
4. `GetPlaybackInfo()` and `GetTimelineProperties()` for state, position and duration
5. keep the last known duration per track because some players drop `EndTime` while paused
6. expose `TryTogglePlayPause`, `TrySkipNext`, `TrySkipPrevious`,
   `TryChangePlaybackPosition`

Artwork is decoded from the SMTC thumbnail stream to a frozen `BitmapImage`.

### In-HUD LiMusic browser

`LiMusicBridge` launches (or reuses) LiMusic with
`WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9222`. It reads
`http://127.0.0.1:9222/json`, deliberately **skips the `limusicharness.localhost`
BotGuard webview**, and attaches to `tauri.localhost` over a local WebSocket.

Each call evaluates a tiny wrapper in LiMusic's page:

```js
(async () => {
  const r = await window.__TAURI_INTERNALS__.invoke(command, args);
  return JSON.stringify({ ok: true, data: r });
})()
```

`Runtime.evaluate` runs with `awaitPromise` and `returnByValue`, so Sable receives the
typed response from LiMusic's own commands: `search`, `get_library`, `get_playlist`,
`play`, `play_playlist`, `toggle_pause`, `next_track`, `prev_track`, `seek`,
`toggle_shuffle`, `set_repeat` and `set_setting`. Playlist playback sends the whole
track list plus its continuation and the clicked index, so LiMusic owns the queue and
continues onward. `set_setting` mirrors the HUD Autoplay toggle into LiMusic's own
`autoplay` key. This is why search, queue continuation and playlist playback are real
LiMusic operations, not a lookalike index.

If LiMusic is already running without the port, `EnsureAsync(true)` restarts it once
and minimizes its window. `CoverService` remembers the playlist title, cover and its track list. A Settings choice
(`ArtworkSource = Song | Playlist`) decides whether the now-playing surfaces draw the
song thumbnail from SMTC or the downloaded playlist cover. The track list matters:
when autoplay/radio continues past the playlist, those tracks are not in it, so they
keep their own cover instead of inheriting the playlist art. Every surface asks
`Pick()` (HUD, mini player, toast), so a setting change or a late cover download
updates them without waiting for the next track.

If the bridge is unavailable, transport falls back to SMTC. The port is bound to
loopback and is only opened while LiMusic is launched through Sable's bridge.

Per-app mixer rows read their level and mute with `GetApplicationVolumePercent` /
`GetApplicationMute`. `GetSessions()` caches the volume a session had when it was
first seen, so using it for display left rows frozen at their starting value; the
live getters keep the mixer honest. Rows carry their PID, so changes stay on the
right app even if the sorted order shifts between refreshes.

`LocalPlayer` still exists (NAudio `MediaFoundationReader` + `WaveOutEvent`, TagLib#,
background scan) and is exercised by the self-test, but the local-folder UI was
removed after the first review.

`MediaCoordinator` chooses the active source and pauses the other when one starts
playing, so two tracks never overlap.

## System sampling

`SystemMonitor` samples once a second on a thread-pool timer:

- CPU from `GetSystemTimes` deltas
- RAM from `GlobalMemoryStatusEx`
- GPU from two PDH wildcard queries (`GPU Engine` 3D sum, `GPU Adapter Memory`)
- network from `NetworkInterface` byte counters, kept as a 72-sample history
- foreground window title and process, with shell/own-window exclusions
- battery and AC state
- FPS from `RTSSSharedMemoryV2` for the foreground PID when RTSS is alive; entries
  older than 2.5 s are ignored so a closed game does not leave a frozen number

`AudioService` uses **WinAudioRoute** (MIT), which wraps Core Audio and the internal
`Windows.Media.Internal.AudioPolicyConfig` route store. It exposes master volume, mic
mute, grouped per-process sessions with their current device, and
`SetApplicationOutput(pid, device)` / `ResetApplicationOutput(pid)`. The capability is
probed at runtime; if routing is unsupported the UI opens the Windows per-app mixer
instead. Discord is ordered first.

## Settings

`%APPDATA%\SableHUD\settings.json`, camelCase JSON, validated on load by
`SettingsStore.Sanitize`. Changes save immediately from the Settings window.

Key fields: theme, scale, default tab, trigger gesture, custom gesture, auto-hide,
keyboard/controller/rumble toggles, widget visibility, mini player, toast, RTSS,
music folder, LiMusic path, mini dock/position, autostart, first-run, controller
isolation.

`MiniDock` accepts `TopLeft` … `BottomRight`, `Center`, or `Custom`. Dragging sets
`Custom` and stores pixel coordinates.

Logs: `%APPDATA%\SableHUD\sable.log`. Settings are never written to the program folder.

## Optional pad isolation

`HidHide` (MIT) is bundled at `drivers\HidHide_1.5.230_x64.exe`. Settings can launch
the installer and, after a reboot, prepare the configuration:

1. `HidHideCLI --app-reg <SableHUD.exe>` allowlists the HUD
2. `--dev-gaming` returns gaming HID groups; the present `gamingDevice` entries are
   collected from each group's `devices` array with `--dev-hide`
3. HID gamepads also expose an XUSB (XnaComposite) interface that `--dev-gaming`
   does not list. Sable finds it in
   `HKLM\SYSTEM\CurrentControlSet\Enum\USB` by vendor/product and `Service=xusb*`,
   and hides that instance too, otherwise XInput games still see the pad
4. `--inv-off` and `--cloak-off`

The device list is rebuilt every time the HUD opens, so a pad connected after the
setting was enabled is picked up. When the HUD is shown, `--cloak-on` hides those
devices from every process except Sable; when it hides, `--cloak-off` restores them.
Startup and normal exit always uncloak. A crash can leave cloaking on until Sable is
run again or `HidHideCLI --cloak-off` is called.

HidHide filters **new device opens**, not handles that already exist. An app or
browser page that opened the controller before the HUD keeps receiving input until
it restarts or reloads; apps that open the pad after the HUD is up are isolated.
This is why a game should be launched with the HUD already open, and why reloading a
browser gamepad tester is enough to see the pad disappear.

For apps that were already running, **Settings → General → Automatic pad reset** does
a one-time elevated setup. It writes a `pad-reset.cmd` in
`%ProgramData%\SableHUD` with an admins-only write ACL and registers a manual,
highest-privilege task (`SableHUD-PadReset`) whose only action is `cmd /c` that
protected script. On show, after cloaking, Sable runs the task; `pnputil` is on the
HidHide allowlist, so it can restart the HID and XUSB devices while they are cloaked.
The restart closes the old handles; the owning apps cannot reopen the device because
the cloak is active. Sable reopens on its allowlisted path, and the overlay keeps
working. `schtasks` and `pnputil` are the only privileged pieces; no elevated
service or user-writable executable is used.

## Animations

The overlay enters from above over 460 ms with a quintic ease, fades over 280 ms and
scales the shell from 98.2% to 100%. It leaves over 280 ms. Page changes slide 9 px
and fade over 320 ms. The mini player fades in over 220 ms.

## Single instance

A named mutex (`Local\SableHUD.Mutex`) owns the app. Four auto-reset events
(`Local\SableHUD.Show|Toggle|Settings|Exit`) let a second process signal the first.
The listener thread waits and posts to the dispatcher. `--settings`, `--show`,
`--toggle`, `--exit` therefore work as launcher/stream-deck commands.

`--tab <music|performance|audio|tools>` is read on a fresh launch together with
`--show`.

## Diagnostics

- `--selftest <path>`: theme/resource checks, settings round-trip, trigger parsing,
  path write, XInput Guide binding, audio endpoints, GPU PDH, system sample, SMTC
  session, local file playback (a generated 1 s tone), RTSS and icon. Writes JSON and
  returns 0 when the critical set passes.
- `--render <dir>`: builds the pages with demo data, renders the real XAML at 2× onto
  a neutral backdrop, and writes the README images plus `manifest.json`. No game or
  media session is required.

## Packaging

`build.ps1` publishes:

- `dist\SableHUD.exe` — self-contained, single file, compressed
- `dist\framework-dependent\SableHUD.exe` — needs .NET 8 Desktop Runtime
- `dist\drivers\HidHide_1.5.230_x64.exe` — optional, never run automatically
- `dist\SableHUD-win-x64.zip` — includes docs and the optional driver

The app manifest requests `asInvoker`, PerMonitorV2 DPI and Windows 10/11
compatibility. No elevation. The HidHide installer asks for elevation itself through
the standard UAC prompt.

## Known technical trade-offs

- WPF is used instead of WinUI 3 because the overlay needs dependable layered-window
  transparency, no-activate behaviour and animation without a composition swap chain.
- XInput cannot be consumed exclusively without a filter driver. The HUD does not
  install one.
- SMTC has no duration field in media properties; duration comes from the timeline,
  which some players fill late.
- RTSS is the only no-injection FPS source used. PresentMon/ETW would need elevation
  and a different privacy posture.
- Discord voice details require the Discord Social SDK and an app registration, so the
  product provides a mixer quick-mute instead of a fake voice list.
