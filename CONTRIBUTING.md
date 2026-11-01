# Contributing to Sable HUD

Sable is a small Windows app with a narrow job: stay out of the way until the Guide
button is pressed. Changes that make the HUD louder, slower or busier are usually the
wrong direction.

## Before you start

- Open an issue for anything larger than a bug fix or a wording change, so the
  direction can be agreed before the code.
- Keep the scope to the HUD. Sable integrates with LiMusic as a separate app; do not
  vendor or modify LiMusic source in this repository.
- New dependencies need a reason. The current set is NAudio, TagLib#, WinAudioRoute
  and the bundled HidHide installer.

## Build

Requires Windows 10/11 x64 and the .NET 8 SDK.

```powershell
git clone https://github.com/DumDuky/sable-hud.git
cd sable-hud
.\build.ps1 -SkipFrameworkDependent
```

`build.ps1` uses the repository-local SDK when present and falls back to `dotnet` on
PATH, so CI and a normal developer machine take the same path.

## Test

Run the built-in diagnostics before opening a pull request:

```powershell
.\dist\SableHUD.exe --selftest $env:TEMP\sable-selftest.json
```

The JSON report covers themes, settings, Guide/toggle logic, the controller map,
XInput binding, audio, GPU, system sampling, SMTC, the LiMusic bridge, local playback,
RTSS and the icon.

For anything involving navigation, test it with a real pad:

- D-pad and left stick, all four directions
- A, B, X, Y, LB, RB, LT, RT, Menu and View
- open, move between tabs, open a browser, back out, close
- Settings: move through a long section, change a value, close

A pointer-only test is not enough. If you cannot test a controller path, say so in
the pull request.

## Code

- Follow the shape of the file you are editing. WPF widgets live in `Widgets/`,
  windows in `Windows/`, platform code in `SystemInfo/`.
- Keep the refresh loop cheap. Pages render from the shared snapshot; do not start
  private timers.
- Keep the palette monochrome. Color comes from the four themes, never from a widget
  or an icon.
- New text goes through the existing styles. No emoji in the interface.
- Short strings end without a period.

## Pull requests

Include what changed, why, and how it was tested. Screenshots help for anything
visible. Keep unrelated cleanup out of the same pull request.

By contributing you agree that your work is licensed under GPL-3.0, the license of
this repository.
