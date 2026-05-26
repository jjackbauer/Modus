# Checklist Transition Proof - Wip.ShellHost Startup and Shutdown

## Baseline Unchecked Source Text Evidence
- [ ] Implement `Wip.ShellHost` startup/shutdown with long-lived host container, one-time plugin load, lifecycle hooks, and graceful exit [depends on shell and Modus bridge]

## Checked Completion Evidence
- [x] Implement `Wip.ShellHost` startup/shutdown with long-lived host container, one-time plugin load, lifecycle hooks, and graceful exit [depends on shell and Modus bridge] [transition-proof: .github/requirements/transition-proofs/checklist-item-wip-shellhost-startup-shutdown-transition-proof-2026-05-24.md]

## Implementation Evidence
- Host startup, long-lived run loop, and graceful cancellation/exit behavior are implemented in `src/Wip.ShellHost/Hosting/WipShellHost.cs`.
- One-time plugin discovery/loading and start/stop/unload lifecycle orchestration are implemented in `src/Wip.ShellHost/Hosting/ModusWipBridge.cs`.
- Behavioral coverage for startup/shutdown and lifecycle hook sequencing is implemented in `tests/Wip.ShellHost.Tests`.
