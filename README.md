# Legion Go 2 Controller Init

Experimental workaround and diagnostic tooling for controller initialization
and right-Legion shortcut bugs in Lenovo Legion Space.

Tested with:

- Legion Go 2 controller `USB\VID_17EF&PID_61EB`
- Legion Space `1.4.4.31` (`26.07.10.01`)
- `SapientiaUsb\LEGOKZHandle.dll` `2.0.1.3`
- Windows 11 build family `10.0.26100`

## Confirmed behavior

There are at least two distinct failures:

1. After login, XInput may remain unavailable until Legion Space's Controllers
   page is opened. Calling the installed controller DLL's `Init()` restores it
   without opening Legion Space.
2. After Legion Space startup or Modern Standby resume, the right Legion button
   may open the side drawer, invoke Task View, or do nothing. XInput can remain
   fully functional in Steam while Legion Space's own LB/RB navigation is stale.

The shortcut failure has a repeatable raw signature:

```text
Healthy: 0x40000000 -> 0x00000000
Broken:  0x40000800 -> 0x00000800
```

`0x00000800` is the logical Legion R bit and can remain asserted after physical
release. Lenovo's installed menu dispatcher separately implements business
command `0` (`OperatingMenu`) and command `12` (`WinTabView`), explaining why
the broken software path explicitly opens Task View.

The drawer can be toggled deterministically through Lenovo's existing IPC:

- API: `LegionSetting.dll!SendMsg2App`
- Destination: `LegionSettingMenu.exe`
- Message:

```json
{"msgType":0,"languageValue":2,"businessName":0,"businessValue":"0","businessValue2":""}
```

`LegionSettingMenu.exe` may exit when the drawer closes. A robust caller must
start it if absent and retry while its IPC server initializes.

See [LENOVO_REPORT.md](LENOVO_REPORT.md) for the reproduction, evidence, and
specific engineering recommendations.

See [SUPPORT.md](SUPPORT.md) for the compatibility matrix,
[KNOWN_ISSUES.md](KNOWN_ISSUES.md) for current limitations, and
[SIGNING.md](SIGNING.md) for release provenance and Authenticode plans.

The application contains no telemetry or network client. See
[PRIVACY.md](PRIVACY.md) and [CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md).

## Repository contents

- `Initialize-LegionGo2Controller.ps1` performs one-shot controller
  initialization using Lenovo's currently installed DLL.
- `ControllerDrawerBridge.cs` is the experimental resident shortcut recovery
  helper. It detects the broken Legion R state, uses Lenovo's toggle command,
  and resurrects the installed drawer process when necessary.
- `LegionGo2ControllerTray.cs` packages the same recovery behavior as a
  single-instance Windows tray application. Its menu exposes current status,
  a manual drawer toggle, the diagnostic log, and a clean exit action.
- `assets/controller-bridge.png` and `assets/controller-bridge.ico` provide the
  unbranded application artwork and multi-resolution Windows tray icon.
- `diagnostics/ControllerEventMonitor.cs` records the native button bitmask for
  short diagnostic sessions.

## Tray application

The tray application is the intended long-running form of the workaround. It
runs in the interactive user session and keeps Lenovo's callback minimal; a
worker thread performs logging, process startup, and IPC.

The current test installation is compiled on the target system against the
Windows .NET Framework assemblies and launched by an interactive per-user
logon task. Only one instance can run per user session. Runtime logs are written
beside the executable as `tray.log`.

The recovery state machine only acts on `0x40000800`, the observed broken press
signature. It deliberately ignores the trailing `0x00000800` stale-release
state, preventing a long press from causing a second toggle.

## Installing a release

Release archives are self-contained for 64-bit Windows and do not include any
Lenovo binaries. Extract the complete archive, then run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\Install.ps1
```

The installer:

- verifies the required Lenovo controller and menu DLLs are already installed;
- copies the application to
  `%LOCALAPPDATA%\Programs\LegionGo2ControllerBridge`;
- creates a delayed, interactive, per-user logon task without elevation; and
- starts the application and verifies that its process remains running.

To remove it:

```powershell
.\Uninstall.ps1
```

Use `Uninstall.ps1 -KeepLogs` to retain `tray.log` while removing the
application. Neither script modifies Legion Space or its installed binaries.

## Building a release

Install the .NET 8 SDK, then run on Windows:

```powershell
.\build-release.ps1
```

This publishes a self-contained single-file `win-x64` application and creates a
ZIP plus SHA-256 checksum under `artifacts`. The GitHub Actions workflow performs
the same build for pushes, pull requests, tags, and manual runs.

## Status and limitations

This is reverse-engineered, device-specific, experimental software. The
resident bridge is still undergoing repeated open/close, Legion Space startup,
sleep/resume, and controller reconnect testing. It is not yet a packaged
release.

The drawer currently opens and closes consistently in testing, but Lenovo may
also dispatch Windows Task View for the same broken press. Foreground-window
tracing is present to identify and narrowly suppress that parallel action; this
is not yet resolved and should be called out in pre-release notes.

PowerShell must not be invoked from Lenovo's native button callback thread.
Doing so caused `powershell.exe` to terminate with `0xc0000409` in
`LEGOKZHandle.dll`. The compiled helpers keep the callback minimal and perform
logging and IPC on a worker thread.

The helpers do not redistribute Lenovo binaries or source. They locate and load
the user's existing Legion Space installation.
