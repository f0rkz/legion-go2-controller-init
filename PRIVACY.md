# Privacy policy

Legion Go 2 Controller Bridge does not transfer information to other networked
systems unless the user explicitly chooses to share a diagnostic archive.

The application:

- does not contain telemetry, analytics, update checks, advertising, or network
  clients;
- writes operational events to `tray.log` on the local computer;
- loads Lenovo DLLs already installed by Legion Space; and
- communicates locally with Lenovo's side-drawer process through Lenovo's IPC
  interface.

`Collect-Diagnostics.ps1` runs only when explicitly invoked. It collects the
application log, relevant controller identities and component versions,
scheduled-task/process state, and recent power-transition event metadata. It
does not collect Lenovo binaries, memory dumps, credentials, or unrelated
application logs. User and computer names are redacted from `tray.log`.

Users should inspect diagnostic archives before sharing them. Controller
instance IDs and timestamps remain in the archive because they are relevant to
device re-enumeration and sleep/resume failures.
