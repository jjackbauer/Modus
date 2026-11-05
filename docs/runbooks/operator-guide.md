# WIP operator guide (pre-release)

**Status:** pre-release operator documentation for WIP 1.0. No release is claimed; the release
decision is the human G5 gate. Security posture is conditional/trusted-only until the G2 re-review
and Windows OS containment (C1) is unclaimed.

This guide is grounded in the checked-in commands and configuration; every command below is run by
CI or by the test suites named in `docs/testing/test-status.md`.

## 1. Prerequisites

- .NET SDK 10.0.x (`dotnet --version`).
- Supported operator platforms: Windows 10/11 and Ubuntu (the CI matrix builds both).
- Optional: a DeepSeek API key for real-model runs (`DEEPSEEK_API_KEY`). Stub mode needs no key.
- Optional tooling used by some legacy tests: `pwsh`, `make`, `zip` (present on the GitHub ubuntu
  image; a bare WSL image may not have them).

## 2. Build and test

```powershell
dotnet restore Wip.slnx
dotnet build Wip.slnx --configuration Release --no-restore

# Full offline gate (16 projects, waiver-registered)
pwsh -NoProfile -File tools/run-wip-tests.ps1 -Configuration Release -ResultsDirectory artifacts/ci-test-results
pwsh -NoProfile -File tools/verify-test-results.ps1 -ResultsDirectory artifacts/ci-test-results -WaiverPath docs/plan/test-waivers.json

# Focused suites
dotnet test tests/Wip.Architecture.Tests/Wip.Architecture.Tests.csproj --configuration Release --no-build
dotnet test tests/Wip.Sandbox.Worker.Tests/Wip.Sandbox.Worker.Tests.csproj --configuration Release --no-build
```

**Authoritative gate:** Tests CI `wip-offline-tests` runs on `windows-latest`; the WIP gate is not
equivalent on Linux (Windows-specific tests exercise `dotnet.cmd`, backslash path guards and
line-ending normalization). See `docs/testing/test-status.md`.

## 3. Running the shell

```powershell
# Interactive shell
dotnet run --project WIP/Wip.ShellHost/Wip.ShellHost.csproj

# With an explicit plugin folder / autoload
dotnet run --project WIP/Wip.ShellHost/Wip.ShellHost.csproj -- .\plugins
dotnet run --project WIP/Wip.ShellHost/Wip.ShellHost.csproj -- --startup-mode=autoload
```

Session lifecycle (the authoritative semantics are rendered by `help` and defined in
`Wip.Shell/Interactive/WipShellCommandSemantics.cs`):

```
init
session start "<task>"
plan
run                     # executes registered workflow stages; does not perform governance
agent "<task>"          # runs the streaming agent loop and prints deltas/tool progress
validate / review / approve / merge
status / artifacts / diff / checkpoint
```

- `run` is execution-only and cannot substitute for `validate`, `review`, `approve` or `merge`.
- `agent` requires a registered `IChatModel` (DeepSeek configuration below); it writes only inside
  the session worktree and never auto-merges or self-approves.
- `merge` remains a separate, explicit governance step.

## 4. Model configuration

Shell host configuration keys (see `WIP/Wip.ShellHost/Hosting/WipShellHostOptions.cs`):

```json
{
  "ModelProvider": "deepseek",
  "DeepSeek": {
    "BaseUrl": "https://api.deepseek.com/",
    "Model": "deepseek-chat",
    "TimeoutSeconds": 30,
    "ApiKeySource": "env:DEEPSEEK_API_KEY"
  }
}
```

- Supported models: `deepseek-chat`, `deepseek-reasoner`.
- The key is read from the environment at request time and is never logged.
- No key is required for the offline suites; the opt-in live test
  (`DeepSeekChatModelLiveTests`) performs a network call only when `DEEPSEEK_API_KEY` is set.

## 5. Isolation, permissions and sandbox modes

- **Worktree confinement:** every repository write/delete is resolved by `WorktreePathGuard`;
  absolute paths, `..` traversal and `.git` internals are rejected.
- **Script host mode:** `ScriptHost:Mode` = `InProcess` (default) or `Worker`. Worker mode is
  trusted-only, validates the worker path, fails closed with no in-process fallback, and the host
  never loads the model assembly in worker mode.
- **Permissions:** `AddWipPermissions(mode, grants, auditSink)` with `Enforce` (default) or `Audit`.
  Built-in capabilities are trusted; plugin-origin capabilities need grants (`*`, bare permission,
  or `pluginId:permission`). Enforcement covers the stage dispatcher, tool gateway, plugin load,
  and model invocation.
- **Approval/merge boundary:** the agent cannot invoke approval or merge; the shell requires explicit
  governance commands and current diff/validation/review evidence.

## 6. Troubleshooting

| Symptom | Cause | Action |
|---|---|---|
| Host fails at startup with a sandbox error | Worker mode misconfigured; worker missing or not trusted | Check `ScriptHost:Mode` and `WorkerPath`; the host fails closed by design — fix configuration, do not bypass |
| `DeepSeek provider configuration is invalid: Environment variable ...` | `ApiKeySource` points at an unset env var | Export `DEEPSEEK_API_KEY` or switch to stub mode |
| HTTP 401/403 from the model provider | Invalid/expired key | Rotate the key; keys are never logged |
| HTTP 503/timeout | Provider incident | Transient retries are bounded; retry later or switch to stub mode |
| Windows: `Win32Exception (5)` from testhost | Session/environment restriction opening the parent process | Run the gate in CI or a normal interactive terminal; this is an environment limitation, not a repository defect |
| `pwsh`/`make` not found in WSL | Bare WSL image | Use the GitHub ubuntu image or install them; these tests are environment-specific |
| Tests fail after a Windows Event Log denial | Non-admin Windows user and the .NET runtime event source | `tools/run-wip-tests.ps1` sets `Logging__EventLog__LogLevel__Default=None`; use it |

## 7. Known limits (must be stated in any operations review)

- Windows OS containment (C1) is **not implemented/claimed**; the helper is absent and the posture is
  fail-closed. Do not describe the sandbox as OS-level containment on Windows.
- G2 remains conditional/trusted-only until the 2026-12-03 re-review and human sign-off.
- The opt-in live model test is the only component that needs network access; everything else is
  offline and deterministic.
- Legacy Modus full-suite meta-tests are blocking again (Phase 4); see
  `docs/execution/blockers/BLK-MODUS-LEGACY-SUITE.md`.

## 8. Where the records live

- Execution state: `docs/execution/state.json`.
- Journal: `docs/execution/journal.md` and `docs/execution/harness/`.
- Test gate and waivers: `docs/plan/test-waivers.json`, `docs/testing/test-status.md`.
- Gates and decisions: `docs/execution/evidence/G*/`.
- Security: `docs/security/` (threat model and sandbox notes).
