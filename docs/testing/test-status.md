# WIP test status (truthful status page)

**Status:** pre-release. WIP 1.0 is **not release-ready**; the G5 release decision has not been
requested. This page describes what actually runs where. A green claim without a run id is not
evidence.

## Authoritative gates

| Gate | Where it runs | What it proves |
|---|---|---|
| `wip-offline-tests` (16 projects) | Tests CI, **windows-latest** | Every registered suite runs, expected project totals are met, and every failure is on the 27-entry waiver register. The PowerShell verifier fails on missing/unexpected/zero-test projects and unwaived failures. |
| `wip-architecture` | Tests CI, ubuntu-latest | Modus and WIP architecture rules (4/4 at 43 WIP projects) |
| `wip-build (ubuntu)` / `wip-build (windows)` | Tests CI | Both platforms restore and build `Wip.slnx` in Release |
| `wip-security` / `wip-security (ubuntu)` | Tests CI | Sandbox hostile/protocol suite (18 tests) **and** the agent safety suite (3 tests: prompt injection, self-approval, exfiltration) |
| Modus CI `tests` | ubuntu-latest | Restore/build, harness sync, migration-regression hard gate, plugin curl probe, **blocking** full legacy Modus integration suite |

The Windows-local version of the 16-project gate cannot be executed in this session (VSTest
testhost aborts with `Win32Exception (5)` opening its parent process), and WSL/Linux is not
gate-equivalent: Windows-specific tests exercise `dotnet.cmd`, backslash path guards and line-ending
normalization. CI is therefore the authoritative gate; focused WSL runs are supplementary evidence.

## Suite inventory (expected totals)

| Project | Expected | Notes |
|---|---|---|
| Wip.Abstractions.Tests | 55 | chat/tool contracts, streaming contracts |
| Wip.Artifacts.Local.Tests | 4 | local artifact store |
| Wip.Builder.Tests | 32 | builder/workflow registrations |
| Wip.Modus.Tests | 27 | plugin hosting incl. collectible ALC and conflicting dependency versions |
| Wip.Policy.LocalSafe.Tests | 40 | policy engine |
| Wip.Validation.DotNet.Tests | 54 | dotnet validation evidence (Windows-specific tests) |
| Wip.Workspaces.Git.Tests | 12 | git worktrees/diff hashing |
| Wip.Tools.Shell.Tests | 25 | shell tool |
| Wip.Runtime.Tests | 137 | agent runtime: contracts, invoker, executor, memory, streaming, DeepSeek adapter, real-model HTTP-boundary E2E, safety suite |
| Wip.Shell.Tests | 33 | shell commands incl. `agent` streaming progress |
| Wip.ShellHost.Tests | 47 | host composition/configuration |
| Wip.Bones.Tests | 396 | Bones engine/workflows |
| Wip.Bones.ModelProviders.DeepSeek.Tests | 48 | Bones DeepSeek adapters |
| Wip.Bones.Host.Tests | 251 | Bones host, status API, loop reliability |
| Wip.Bones.E2E.Tests | 10 | shell E2E harness |
| Wip.Sandbox.Worker.Tests | 18 | worker protocol/hostile inputs |

27 registered failures are carried on the waiver register (pre-existing, documented in
`docs/plan/test-waivers.json`); the register and the expected totals are the gate's source of truth.
No new waiver is added without a pre-existing failure, reproduction evidence, an owner and an
expiry.

## Test categories

- **Offline/deterministic** (everything above): no network, no credentials.
- **Live (opt-in):** `DeepSeekChatModelLiveTests` calls the real DeepSeek API only when
  `DEEPSEEK_API_KEY` is set; it returns immediately otherwise and is safe in the offline gate.
- **Environment-bound:** a few legacy tests need `pwsh`, `make` or `zip`; a bare WSL image may lack
  them, while the GitHub ubuntu image provides them. They are not waived.

## How to reproduce

```powershell
pwsh -NoProfile -File tools/run-wip-tests.ps1 -Configuration Release -ResultsDirectory artifacts/ci-test-results
pwsh -NoProfile -File tools/verify-test-results.ps1 -ResultsDirectory artifacts/ci-test-results -WaiverPath docs/plan/test-waivers.json
```

## Last verified CI (see `docs/execution/state.json` for the current record)

| HEAD | Tests CI | Modus CI |
|---|---|---|
| `89308d1` | `42048062612` — all six jobs success (Runtime 137; agent safety steps green) | `42048062647` — success |
| `cda224a` | `42045866606` — all six jobs success | `42045866711` — success |
| `4e9c11d` | pending | `42054558417` — failure only in the legacy failure-reporting step; suite itself passed; fixed in `6453c38` |

## Known limitations

- Windows OS containment (C1) is unclaimed; the sandbox is fail-closed but not OS-contained.
- G2 remains conditional/trusted-only until the 2026-12-03 re-review and human sign-off.
- Modus CI's legacy suite is blocking again; its reporting step now treats a green TRX as the normal
  case.
