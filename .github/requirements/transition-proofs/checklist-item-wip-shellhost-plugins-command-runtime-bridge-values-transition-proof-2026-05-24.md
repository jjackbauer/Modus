# Checklist Transition Proof - Wip.ShellHost Plugins Command Runtime Bridge Values

## Baseline Unchecked Source Text Evidence
- [ ] Surface plugin diagnostics and plugin catalog through interactive host command flow (`plugins`) with runtime values from bridge, not static docs [depends on manifest and diagnostics capture]

## Checked Completion Evidence
- [x] Surface plugin diagnostics and plugin catalog through interactive host command flow (`plugins`) with runtime values from bridge, not static docs [depends on manifest and diagnostics capture] [transition-proof: .github/requirements/transition-proofs/checklist-item-wip-shellhost-plugins-command-runtime-bridge-values-transition-proof-2026-05-24.md]

## Implementation Evidence
- `plugins` interactive command output is sourced from bridge runtime state via `GetRunManifest()` and `GetLoadDiagnostics()` in `src/Wip.Shell/Interactive/WipShellCommandLoop.cs`.
- Runtime bridge captures deterministic plugin manifest values and stage-scoped diagnostics during plugin load in `src/Wip.Modus/Hosting/ModusWipBridge.cs`.
- Behavioral test `PluginsCommand_GivenCapturedRuntimeFailures_RendersBridgeDiagnosticsInCommandOutput` verifies one shell run renders both loaded plugin catalog entries and runtime diagnostics in command output in `tests/Wip.ShellHost.Tests/Hosting/WipShellHostPluginDiagnosticsTests.cs`.
