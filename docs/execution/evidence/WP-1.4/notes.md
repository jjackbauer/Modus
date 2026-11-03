# WP-1.4 — Resource limits and kill

**Status:** done for resource limits (Windows Job Object + Linux prlimit/timeouts); C1 OS containment remains out of scope.
**Date:** 2026-11-03

## Delivered

- Client startup/compile/move timeouts; timeout kills the process tree.
- Windows Job Object: kill-on-close, 256 MiB process memory cap, 16 active-process cap.
- Linux `prlimit` launcher where available: CPU, process, virtual-memory bounds; absolute dotnet.
- Bounded 1 MiB inbound frame; 256 KiB outbound cap; stdout/stderr suppressed during strategy execution.
- Tests: infinite loop timeout/kill, 64 MiB memory bomb capped on Windows, environment allowlist, oversized frame, hostile APIs.

## Verification

- 17/17 sandbox tests; 16-project gate local green.
- No orphan process observed after timeout (client waits for exit).

## Residual

- Linux memory/process/security limits are best-effort via `prlimit`, not cgroup/seccomp.
- No OS file/network/native boundary (C1); Job Object/prlimit are resource caps.
- C1 remains a G2 condition; no untrusted production execution.
