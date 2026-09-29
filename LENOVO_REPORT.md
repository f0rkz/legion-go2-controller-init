# Legion Go 2 controller initialization and shortcut-routing defects

To the Lenovo Legion Space engineering team:

The integrated Legion Go 2 controllers are affected by two related lifecycle
defects in Legion Space 1.4.4.31:

1. The controller can remain unavailable after login until the Controllers page
   is opened.
2. The right Legion shortcut becomes nondeterministic after Legion Space starts
   or Windows resumes. The same button may open the side drawer, invoke Windows
   Task View, or do nothing.

Both failures occur while the controller USB devices remain enumerated and
healthy. Evidence below indicates stale initialization, button-release, and
subscriber state in Lenovo software rather than a Windows PnP failure.

## Tested environment

- Device: Lenovo Legion Go 2
- Legion Space: `1.4.4.31` (`26.07.10.01`)
- Controller USB identity: `USB\VID_17EF&PID_61EB`
- Controller library: `SapientiaUsb\LEGOKZHandle.dll` version `2.0.1.3`
- Windows 11 build family: `10.0.26100`
- Power model: Modern Standby

## Reproduction A: controller unavailable after login

1. Fully shut down the Legion Go 2.
2. Power it on and log in.
3. Do not visit the Legion Space Controllers page.
4. Test XInput and the Legion shortcut buttons.

Observed:

- The USB composite, HID, and `Xbox 360 Controller for Windows` nodes report
  status `OK`.
- Controller input may be unavailable until the Controllers page is opened.
- Opening that page immediately restores input.

Expected: controller input and shortcuts work after login without activating a
specific UI route.

### Initialization evidence

Calling the installed controller library directly produces:

```text
Init()       = 1
GetLastErr() = 0
getUSBMode() = 1
```

After this call, XInput works in Steam without opening the Controllers page.
PnP snapshots show no corresponding device creation or status transition. A
delayed per-user logon task performing this initialization also restored XInput
after a cold boot while `LSDaemon.exe` was running and the Legion Space UI was
not. This isolates the missing operation to Lenovo's controller initialization
path rather than Windows enumeration.

## Reproduction B: shortcut fails after resume or Legion Space startup

1. Begin with working controller input and side drawer.
2. Start Legion Space, or enter and resume from Modern Standby.
3. Press the right Legion shortcut repeatedly, allowing full releases.

Observed outcomes alternate between:

- side drawer;
- Windows Task View/app switcher;
- no action.

After resume, LB/RB continued to work in Steam but stopped changing tabs in
Legion Space. Opening the Controllers page restored Legion Space's response.
This proves the XInput path was healthy while Legion Space's UI/controller
subscription was stale.

System events confirmed Modern Standby entry and exit (`Kernel-Power` events
506 and 507). The game-controller interface changed from `IG_00` before sleep
to `IG_01` after resume while the underlying device remained healthy.

## Raw button-state evidence

`SetTestButtonBackFunc()` was registered from a compiled helper. The callback
only enqueues the native value; all logging and IPC occur on a managed worker
thread.

A healthy right-Legion press produces:

```text
0x40000000 -> 0x00000000
```

The failing path repeatedly produces:

```text
0x40000800 -> 0x00000800
```

In some failures, `0x00000800` remains present across subsequent callbacks
instead of returning to zero. Based on the bundled API's documented button
ordering and observed behavior:

- `0x00000800` is the logical Legion R button bit.
- `0x40000000` is a synthesized Desktop/action bit.

The identification of `0x00000800` as Legion R is strongly supported; the
meaning of `0x40000000` is inferred from repeated traces. The directly observed
defect is that the logical Legion R state can remain asserted after physical
release.

Changing `GetLegionLRAndMenuViewMode()` was investigated but does not explain
the defect. The mode was later observed as `0` while Task View still occurred.

## Shortcut dispatcher evidence

The installed `LegionSettingMenu.dll` contains a dispatcher with separate
business operations for:

- `0`: `OperatingMenu` (side-drawer operation)
- `12`: `WinTabView` (Windows Task View)

Therefore Task View is not an accidental Windows interpretation of the HID
packet. Lenovo software explicitly dispatches the wrong high-level action for
some right-Legion presses.

The drawer itself is healthy. Calling the installed native IPC function
`LegionSetting.dll!SendMsg2App` with destination `LegionSettingMenu.exe` and
this message reliably toggles it:

```json
{"msgType":0,"languageValue":2,"businessName":0,"businessValue":"0","businessValue2":""}
```

The IPC call returns `false` after `LegionSettingMenu.exe` exits when its drawer
is closed. Relaunching the installed executable, waiting for its IPC server,
and resending the same business command succeeds. An unconditional wake-up
message containing `LegionSettingMenu.exe` only opens the drawer and cannot be
used as a toggle.

These results isolate three lifecycle problems:

1. Controller initialization is coupled to the Controllers page.
2. The raw/logical Legion R release state can become stale, and the shortcut
   dispatcher can select `WinTabView` or no operation instead of `OperatingMenu`.
3. The drawer process and its IPC server can exit without the shortcut path
   reliably relaunching them.

## Requested corrections

- Move controller initialization into the device/session lifecycle rather than
  a page lifecycle. Run it after login, USB arrival/re-enumeration, and resume.
- Make initialization idempotent and serialize access to the vendor HID
  endpoint across Legion Space components.
- Reset button state on device generation changes, suspend/resume, callback
  registration, and reader restart. Never preserve an asserted Legion R bit
  across those boundaries.
- Route the right Legion button from a debounced press/release edge rather than
  a stale level bitmask.
- Keep the side-drawer action distinct from the `WinTabView` action and log the
  input state and selected business command when dispatching either one.
- Ensure the shortcut handler starts or reconnects to `LegionSettingMenu.exe`
  before sending the drawer command, with a bounded retry while its IPC server
  initializes.
- Rebind Legion Space's controller/UI subscriptions after resume rather than
  requiring the Controllers page to recreate them.

Useful diagnostic fields would include device generation/interface path,
VID/PID, selected controller implementation, initialization result,
`GetLastErr()`, USB mode, raw button mask, previous button mask, selected
business command, IPC result, and resume/rebind generation.

## Validation workaround

The accompanying experimental helper loads only Lenovo DLLs already installed
on the device; it does not modify or redistribute them. A one-shot initializer
restores XInput. A resident diagnostic bridge observes the broken Legion R bit,
starts the installed drawer process when necessary, and sends business command
`0` through Lenovo's existing IPC interface.

This workaround is evidence and a temporary compatibility measure. The proper
fix belongs in Legion Space and its daemon/controller lifecycle.
