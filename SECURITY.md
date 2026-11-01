# Security

Sable is a local Windows app. It has no server, no account and no telemetry.

## Reporting

Use GitHub's private vulnerability reporting on this repository:
**Security → Report a vulnerability**. Please do not open a public issue for a
security problem.

## Trust boundaries

- **LiMusic bridge.** While a music widget is linked, LiMusic runs with WebView2's
  DevTools port bound to `127.0.0.1`. Local processes that already run as your user
  can reach that port, exactly as they can reach LiMusic's own UI. The port lives as
  long as the linked LiMusic process and is never bound to a network interface.
- **Audio routing.** Per-app output uses an undocumented Windows API, capability
  probed at runtime. It only changes the audio route of the process you select.
- **HidHide.** The optional driver hides the physical controller from other apps
  while the HUD is open. Sable allowlists itself and releases the cloak on normal
  exit. After a crash, run Sable again or `HidHideCLI.exe --cloak-off`.
- **Updates.** There is no auto-updater. Releases are built from this repository by
  GitHub Actions and published on the Releases page.

Sable is unsigned. Verify a download against the SHA-256 in the release notes if you
need to be certain of its origin.
