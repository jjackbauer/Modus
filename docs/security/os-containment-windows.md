# Windows OS containment — production path

**Status:** installer + AppContainer launcher implemented; CI validation pending.

## One-time elevated install

Run from an **Administrator PowerShell**:

~~~powershell
cd C:\Users\ricar\dev\Modus
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\sandbox\Install-WipAppContainer.ps1
~~~

The script:

1. creates/derives the `WipSandboxWorker` AppContainer profile;
2. grants that AppContainer SID read/execute on:
   - `WIP\Wip.Sandbox.Worker\bin\Release\net10.0`;
   - `dotnet.exe`, `host`, and `shared` under the .NET root;
3. grants the SID full control on `.tmp\wip-sandbox`;
4. writes `%ProgramData%\WIP\appcontainer.sid` (readiness marker) and
   `docs/execution/evidence/WP-1.4/windows-appcontainer-setup.txt`.

The main app stays **non-elevated**; it derives the profile SID and launches the worker with `PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES`. If the marker is absent, Windows `RequireOsContainment=true` fails closed.

## CI

The Windows `wip-security` job runs the installer before the sandbox suite (the GitHub runner is administrator). The containment test runs only when the marker and profile are present.

## Residual

- AppContainer creation is an install-time operation; an uninstalled machine is fail-closed.
- `CreateProcessAsUser`/security-capability launch must be verified in CI and on a clean machine.
- If AppContainer launch still returns error 5 after the installer, the next step is an elevated helper/service that owns the launch over a local named pipe; the main app must remain non-elevated.

## Elevated helper

The installer also builds and starts a SYSTEM scheduled task WipSandboxHelper. The helper listens on the WipSandboxHelper named pipe, validates a per-install token plus worker/temp paths, launches the AppContainer worker with inherited pipe handles, and returns the PID. The app never runs elevated.
