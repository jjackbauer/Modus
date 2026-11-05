# Compatibility and upgrade notes (WIP 1.0 draft)

**Status:** pre-release. No earlier WIP release exists; the baseline is `wip-baseline-2026-11-02`
(`99c0b08`, tag `wip-baseline-2026-11-02`). No claim of compatibility beyond what is tested here.

## Runtime and platform support

| Item | Supported |
|---|---|
| .NET | SDK 10.0.x (both builds use `10.0.x` in CI) |
| Windows | builds and the offline/legacy suites run on `windows-latest` |
| Linux | builds and the architecture/security/modern suites run on `ubuntu-latest`; the sandbox worker is verified on Linux |
| Windows OS containment | **not supported/claimed** (C1 unclaimed) |

## Contract and configuration changes since the baseline

| Area | Change | Upgrade action |
|---|---|---|
| Workflow execution | declarative `WorkflowDefinition` + `WorkflowPlanMapRuntime` replaced the process-global map registry and default-linear heuristics | register explicit maps/definitions; legacy registration factories were added where required |
| Host composition | `RunWorkflowAsync` requires the host `IServiceProvider`; no nested provider | pass the host provider; do not build a per-run container |
| Script host | `ScriptHost:Mode` (`InProcess` default, `Worker` trusted-only) | keep `InProcess` for trusted profiles; `Worker` needs a valid worker path and trusted-only configuration |
| Permissions | catalog + `PermissionMode` (`Enforce` default / `Audit`) + grants for plugin-origin capabilities | provide grants for plugin capabilities; `Audit` is diagnostic only |
| Plugins | collectible per-plugin `PluginAssemblyLoadContext` | plugins load into their own context; conflicting dependency versions are isolated |
| Agent runtime | new `IChatModel` contract; DeepSeek adapter; `RunAgentAsync`/`RunAgentStreamingAsync`; shell `agent` command | register `IChatModel`; the shell `agent` command requires a session |
| Privacy | agent-memory redaction + `retention` command (`30` days default) | run `retention purge` under the deployment policy |
| CI | 16-project offline gate on Windows; Modus legacy suite blocking again | no operator action; see `docs/testing/test-status.md` |

## Manifest and plugin compatibility

Plugin manifests carry an API version and declared capabilities. Unknown or unregistered manifests
are rejected deterministically; the `Wip.Modus` conflict fixtures prove dependency isolation. The
1.0 contract is documented in `harness/requirements/` and the architecture tests enforce the
boundaries.

## Rollback compatibility

There are no database migrations. Session state and artifacts remain readable by the previous
version; a release-blocking incompatibility is a G5 no-go. See `docs/release/rollback.md`.
