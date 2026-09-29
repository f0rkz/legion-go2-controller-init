# v0.1.0-alpha.1

First public alpha of the Legion Go 2 Controller Bridge.

## What it does

- Initializes the installed Legion Go 2 controller library at interactive logon.
- Restores the side drawer when the observed broken callback signature arrives.
- Starts Lenovo's installed drawer process and retries its local IPC if needed.
- Provides a tray status icon, manual drawer toggle, log access, and clean exit.

## Tested

- Legion Go 2, `USB\VID_17EF&PID_61EB`
- Legion Space `1.4.4.31` (`26.07.10.01`)
- Windows 11 build family `26100`, x64
- Normal power-off/sign-in using Windows Fast Startup
- Repeated drawer toggles on the Windows desktop and Steam Big Picture

## Known issues

- Lenovo may also dispatch Windows Task View for the same physical shortcut
  press. The bridge restores the drawer, but does not yet suppress Lenovo's
  injected Win+Tab action.
- The left Legion shortcut has not been revalidated with the bridge active.
- A full Windows Restart and Modern Standby resume test are still pending.
- Only the Legion Go 2 Sapientia controller implementation has been tested.
- The executable is unsigned. Verify its SHA-256 checksum and GitHub provenance
  before running it.

## Installation

Extract the ZIP and follow `README.md`. The per-user installer requires no
administrator privileges. `Uninstall.ps1` removes the scheduled task and
application.

## Downloads

- `LegionGo2ControllerBridge-win-x64.zip`: self-contained x64 application,
  installer, uninstaller, documentation, and diagnostic collector.
- `LegionGo2ControllerBridge-win-x64.zip.sha256`: archive checksum.
- GitHub Actions provenance attestation is attached to the archive. Verify with
  `gh attestation verify LegionGo2ControllerBridge-win-x64.zip -R f0rkz/legion-go2-controller-init`.

Report issues with `Collect-Diagnostics.ps1`; inspect its ZIP before sharing.
