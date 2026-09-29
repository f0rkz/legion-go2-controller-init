# Known issues

## Task View may open with the side drawer

Lenovo's dispatcher can execute its `WinTabView` business operation for the
same broken right-Legion press that the bridge repairs. The installed native
implementation synthesizes Left-Windows + Tab. The current bridge restores the
drawer but does not yet suppress that parallel injected shortcut.

Foreground-window detection is not sufficient: Task View is a shell overlay
and did not replace the foreground HWND during 50, 150, 300, or 600 ms samples.
The planned mitigation is a narrowly armed low-level keyboard hook that blocks
only injected Tab events while a Windows key is held immediately after the
observed `0x40000800` failure signature.

## Controller support is intentionally narrow

Only the Legion Go 2 Sapientia implementation and the tested USB identity are
known to use the observed ABI and button masks. Huaqin, original Legion Go, and
Legion Go S support must be validated independently.

## Left Legion shortcut not yet validated

The physical left Legion shortcut was not re-tested after the tray bridge was
installed. The bridge only reacts to callback mask `0x40000800` and does not
intentionally remap Legion L, but this alpha does not claim left-button support.

## Legion Space updates may change private interfaces

The application dynamically locates Lenovo's installed DLLs but depends on
undocumented exports and IPC behavior. A Legion Space update can change those
interfaces. The application must fail closed and report an unsupported version
rather than guessing at an ABI.

## Unsigned prereleases may trigger SmartScreen

Until release signing is configured, Windows may warn about downloaded builds.
Verify the published SHA-256 checksum. Do not download binaries from unofficial
mirrors.
