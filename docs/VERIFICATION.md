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
- **Virtual pad isolation**: Sable hides the real pad with HidHide and presents a
  ViGEmBus Xbox 360 pad to games. Verified with the physical pad (`VID_3537&PID_1098`,
  at XInput slot 0) and with a ViGEm pad standing in for it:
  - HUD closed: the hidden real pad is at slot 0 (`XInputGetState` → 1167 for any app
    Sable does not allowlist) and the Sable virtual pad at slot 1 forwards the live
    left stick (measured `12604`, `23269` while the stick rested).
  - HUD open: the virtual pad submitted a neutral report and read `0` on every axis,
    with the real pad still hidden. Closing the HUD restored forwarding.
  So a game using the virtual pad stops receiving input the moment the HUD opens,
  without a device restart, reload or console window. The browser gamepad tester
  showed the same result once reloaded.
- **One-time switch**: a game already running before the virtual pad appeared holds the
  real pad handle and must be restarted once. Games launched after Sable see only the
  virtual pad. The same applies to an open browser tester once.
- **HUD focus**: opening the overlay always presents the Music tab with the cursor on
  play/pause. `MusicView.Activate` previously restored the last used button, so a
  search left the cursor on **Browse LiMusic**. It now resets to `PlayButton`; with no
  track loaded it still falls back to **Browse LiMusic**, which is the only useful
  action there.
- **Audio tab cursor (1.1)**: the page registered its three top controls only inside
  `BuildSessions`, which was skipped while the session signature stayed empty. With no
  app rendering audio the page had an empty nav list: no cursor ring, and no direction
  moved. Reported from use as "you just can't navigate the audio tab", with no ring at
  all. The structural build now runs once unconditionally, so System volume, Output and
  Mic are always registered. Sessions also include apps that are not currently playing,
  so a paused app keeps its mixer row.
- **Muted speaker (1.1)**: the old 21-unit glyph was squeezed into a 13 px box and the
  slash merged with the cone. It is now a purpose-built speaker-plus-slash designed at
  24 units and drawn at 16 px, checked next to the unmuted icon in the design render.
- **UI polish**: micro labels moved from shouting caps to sentence case at 11 px
  medium (`System volume`, `Source`, `Album`, `Processor`), the LiMusic pill and toast
  keep the player's capitalisation, and stat values use tabular figures.
- **Chrome (1.1)**: the active tab and the Settings sidebar use a raised surface plus a
  hairline instead of a solid white fill, the cursor is one 1 px ring with the item's
  own border hidden, the hint strip uses one keycap size on an 18 px rhythm, Tools drops
  its six chevrons, and Performance keeps a single ring on Processor with thin value
  bars for the rest. Verified in the rendered pages: no clipped subtitles in Tools or
  the Audio mixer, both themes legible.
- **Type scale (1.1)**: 20+ hard-coded sizes collapsed to 11, 13, 15, 20, 28 and 30.
  Nothing in the interface is smaller than 11 px.
- **Cloak guard**: the guard releases HidHide when isolation is disabled; while virtual
  isolation runs the real pad stays hidden on purpose. Verified show/hide in the
  stand-in test: the virtual pad went neutral/open and forwarded/closed.
- **Exit release**: exiting Sable while the HUD is open runs a blocking `--cloak-off`
  (5 s budget), disconnects the virtual pad and leaves a watchdog that releases HidHide
  if Sable disappears unexpectedly. Verified: show (`cloak=--cloak-on`), exit, process
  gone, `cloak=--cloak-off`. The tray and Settings also offer **Release controller**.
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
