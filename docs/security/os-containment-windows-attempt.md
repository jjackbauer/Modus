# Windows AppContainer attempt (2026-11-03)

**Status:** failed; not wired. Windows C1 remains open.

## What was tried

- `CreateAppContainerProfile("WipSandboxWorker", ...)` succeeds from an elevated PowerShell probe but returns `0x80070005` (access denied) from the testhost.
- Pre-creating the profile and deriving the SID succeeds; `icacls` grants the AppContainer SID on the worker directory and temp succeed.
- `CreateProcessAsUser` with `PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES` fails with error 5 (access denied); the `CreateProcess` fallback also fails with error 5.
- The experimental launcher was removed; Windows `RequireOsContainment=true` fails closed rather than silently using the Job Object.

## Why it failed

Likely missing AppContainer token creation/installation privileges and the exact ACL/security-capability setup required for the .NET host/runtime. AppContainer profile creation itself requires elevation; a testhost does not have it. A product installer/helper must create the profile and grant runtime access before launch.

## Next

1. Installer/helper creates the profile with the required privileges.
2. Launch via an AppContainer token (not the current user token) with explicit capabilities and a granted runtime/worker/temp ACL.
3. Verify the child token is AppContainer (not a normal process) and file/network denial with an OS probe.
4. Only then remove the Windows `NotSupportedException`.

No Windows containment claim.
