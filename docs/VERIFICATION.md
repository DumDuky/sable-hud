# Sable HUD — verification

## Automated self-test

```
SableHUD.exe --selftest %TEMP%\sable-selftest.json
```

Latest published run: exit `0`, `passed=true`.

- themes: Noir, Graphite, Ivory, Porcelain
- resources and packed `app.ico`
- settings round-trip
- trigger parsing: `Guide=0x400`, `LB+RB=0x300`, `View+Menu=0x30`
- trigger state machine: single Guide, double Guide and LB+RB each fired once / twice
  as expected (injected without hardware)
- controller map: `A=1 B=1 X=1 Y=1`; D-pad and left stick moved all four directions;
  LT/RT emitted volume/seek adjustments
- XInput: `XInputGetStateEx` bound (Guide visible)
- audio: master, mic, active sessions and output devices enumerated
- GPU: PDH counters available
- SMTC: real LiMusic session (`Around the World — Daft Punk`, 02:07/07:09, playing)
- **LiMusic bridge: connected; 20 search results, 7 playlists, first result
  “Teachers (Extended Mix)”**
- local engine: generated WAV scanned and played (`position > 0.1 s`)
- RTSS: optional; absent reported as a soft check

## Live checks on Windows 11

- **In-HUD search and playback**: typed “DAFT” on the on-screen keyboard, searched
  through LiMusic, selected “Around the World”; LiMusic began playing it and the HUD
  reflected `playing=true`.
- **Playlist queue**: selecting a track from the “tunes” playlist started
  `play_playlist` with `currentIndex=1`, 34 queue items and source `tunes`; the next
  titles are the playlist's real order, so playback continues onward.
- **Autoplay / radio**: the bridge mirrored `autoplay=true` into LiMusic; the toggle
  is in Settings → Widgets.
- **Playlist cover**: with Artwork = Playlist, playing "God Is" from the "tunes"
  playlist drew the playlist's own cover; switching to Song restores the song art.
  When the playlist ended and autoplay moved to "south to west" (not in the
  playlist), the row kept that track's own cover — the playlist art no longer
  leaks into radio playback. The mini player and toast use the same picker.
- **Instant transport feedback**: the play icon and state changed within 180 ms of the
  click, before SMTC polled back.
- **Playlist browsing**: opened the signed-in library (7 playlists), opened a playlist
  and displayed its tracks with artist/duration.
- **Transport**: the HUD play button paused LiMusic via the bridge; next advanced to
  “Lady (Hear Me Tonight)” and started it.
- **Seek**: a mouse click at 75% on the progress rail moved the live LiMusic session
  to 01:07/01:30.
- **Per-app output**: routed LiMusic to “Speakers (Yeti Classic)” from the output
  picker; readback confirmed the new endpoint, then reset it to System default.
- **Performance**: live CPU/GPU/RAM/network/foreground/session values appear on a
  desktop with no game running.
- **Navigation**: left/right now move focus (verified in the controller map), the
  left stick repeats when held, LT/RT adjust seek/volume.
- **Auto-hide**: the overlay was hidden after the 12 s idle timeout.
- **Screenshot tool**: hides the HUD, writes to `Pictures\Sable HUD`, then restores
  the Tools page.
- **Settings**: opens above the desktop; row focus rings are no longer clipped;
  D-pad down calls BringIntoView so the controller scrolls the list.
- **Volume**: left/right on the master/session row adjusts that volume without moving
  focus; LT/RT still repeats. Per-app levels are read live, so a change appears on the
  row immediately (verified LiMusic 90% → 65% and back).
- **Audio page access**: opened the Audio tab with LiMusic plus three ffplay sessions
  active; the page built in 47 ms, showed all four, and the hint now reads
  `LB/RB Tabs` so the controller route is explicit.
- **Back out of search**: searching from the now-playing page, then backing out of the
  results, returns to the keyboard with the query intact; a second back returns to
  now-playing. Searching from the library returns to the library. The results ↔
  keyboard loop is gone.
- **Pad isolation**: with the pad connected, opening the HUD hid both its HID gamepad
  interface and its XUSB interface. A fresh, non-allowlisted XInput reader reported
  1167 (no pad) while Sable still showed `Pad 1`; hiding the HUD uncloaked it and the
  reader saw the pad again. Devices are re-read every time the HUD opens, so a pad
  connected after the setting was enabled is picked up.
  Already-open apps keep the handle they hold: an open hardwaretester page kept the
  pad, while reloading that page with the HUD open dropped it to “Connect gamepad”.
  This is HidHide filtering new device opens, not Sable. An **automatic pad reset**
  was tried and removed: on this Windows build `pnputil` restarts the HID gamepad but
  returns “not supported on this OS product” for the USB composite parent and XUSB,
  so the tester kept its handle, and the scheduled `cmd` flashed a console on every
  open. The task was deleted. A supported reset needs SetupAPI/devcon and is not in
  this build.
- **UI polish**: micro labels moved from shouting caps to sentence case at 10 px
  medium (`System volume`, `Source`, `Album`, `Processor`), the LiMusic pill and toast
  keep the player's capitalisation, and stat values use tabular figures.
- **Cloak guard**: a previous automatic reset could finish after the HUD had closed
  and re-cloak the pad. The reset is removed and a 3 s guard now uncloaks whenever the
  HUD is hidden. Verified: show, hide 300 ms later, then cloak off and a fresh XInput
  reader sees the pad.
- **Exit release**: exiting Sable while the HUD is open now runs a blocking
  `--cloak-off` (5 s budget) instead of a fire-and-forget 800 ms wait. Verified: show
  (`cloak=--cloak-on`), exit, process gone, `cloak=--cloak-off`, fresh XInput sees the
  pad. The tray and Settings also offer **Release controller** for a stuck state.
- **Output**: the Output button is part of the D-pad focus order (master → Output →
  mic → app rows), so routing no longer needs the mouse or X on a row.
- **Mini player**: Top-right dock preset placed it correctly; Y/hotkey toggles it.
- **Installer**: the zip installs to `%LOCALAPPDATA%\SableHUD`, creates the Start Menu
  shortcut, the installed self-test passes, and uninstall removes it.

## Packaging

The published self-contained exe and portable zip include
`docs/` and `drivers/HidHide_1.5.230_x64.exe` (optional, never launched
automatically).

## Design renders

`SableHUD.exe --render docs\screenshots` renders the real XAML at 2× with demo data.
Live HUD crops in the same folder were captured from the running release.
