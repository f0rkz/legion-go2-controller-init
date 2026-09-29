# Support status

## Tested configuration

| Component | Tested value | Status |
| --- | --- | --- |
| Device | Lenovo Legion Go 2 | Active testing |
| Controller USB ID | `USB\VID_17EF&PID_61EB` | Supported |
| Legion Space | `1.4.4.31` / `26.07.10.01` | Supported |
| Controller DLL | `LEGOKZHandle.dll` `2.0.1.3` | Supported |
| Windows | Windows 11, build family `26100` | Supported |
| Architecture | x64 | Required |

Other Legion Space versions may work because the application discovers the
installed version dynamically. They are unverified until someone supplies a
diagnostic bundle and confirms cold boot and resume behavior.

The original Legion Go and Legion Go S are not currently supported. Do not
assume that they expose the same USB identity, DLL ABI, or button bitmask.

## Test checklist for a new version

- Cold boot and sign in without opening Legion Space.
- Verify XInput in a game or Steam.
- Open and close the side drawer at least ten times.
- Launch and close Legion Space, then repeat the shortcut test.
- Enter and resume from Modern Standby, then verify XInput and shortcuts.
- Confirm LB/RB behavior in both Steam and Legion Space.
- Exit and restart the tray application.
- Run the uninstaller and confirm the scheduled task and process are removed.

## Reporting a problem

Run `Collect-Diagnostics.ps1`, inspect the resulting ZIP, and attach it to the
issue if its contents are acceptable. Include the exact Legion Space version,
whether the failure followed boot/resume/UI startup, and what the right Legion
button did: drawer, Task View, both, or nothing.
