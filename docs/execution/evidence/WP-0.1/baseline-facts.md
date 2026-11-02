# WP-0.1 — Verified baseline facts

**Captured:** 2026-11-02T10:42:26-03:00
**Method:** read-only file/git inspection; commands and exit codes in [command-exit-codes.txt](command-exit-codes.txt). No build or test was executed (see "Not yet verified").

## 1. Baseline identity

| Fact | Value | Source |
|---|---|---|
| Branch | `WIP` (tracks `origin/WIP`) | `git branch -vv` |
| HEAD | `d6b895f56af6f633dc3c009cbf93a862a0e920c1` | `git rev-parse HEAD` |
| HEAD subject | `feat(bones): implement learning-loop enhancements (T1.1-T3.5, M1)` | `git log -1 --oneline` |
| Working tree | 308 porcelain entries: 148 modified, 160 untracked | `git status --porcelain` |
| Tracked diff | 148 files, +6,758 / −1,427 | `git diff --stat` |
| .NET SDK | 10.0.103 (8.0.419 also installed) | `dotnet --version`; `dotnet --list-sdks` |
| Plan/report/prompt | present (17,395 / 109,952 / 85,383 bytes) | `Test-Path` |

This matches the plan's documented baseline exactly (branch `WIP`, commit `d6b895f`, 308 paths) and the report's §"Working tree" line ("308 changed paths: 148 modified, 160 untracked").

## 2. Solution topology (plan P0-02 / WP-0.3)

| Fact | Value | Evidence |
|---|---|---|
| WIP projects on disk | **37** | [solution-coverage.txt](solution-coverage.txt) |
| WIP test projects | **17** | same |
| Projects in `WIP.slnx` | **24 of 37** (12 source + 12 test) | same |
| WIP projects in `Modus.slnx` (working tree) | **34 of 37** | same |
| Projects in **neither** solution | **3**: `Wip.Bones.Tests`, `Wip.Bones.Benchmark`, `BonesLibraryRecovery` | same |
| Projects missing from `WIP.slnx` | **13** (all Bones projects + `Wip.Shell.E2E`) | same |

The report says `HEAD` had 24 WIP projects in both solutions; that is still true for `WIP.slnx`. The modified `Modus.slnx` in the working tree has already added 10 WIP projects (Bones family + `Wip.Shell.E2E`), but `Wip.Bones.Tests` remains outside both solutions. This is exactly the WP-0.3 gap, not a WP-0.1 regression.

## 3. Test surface (static inventory)

| Fact | Value |
|---|---|
| WIP test projects | 17 |
| Test source files under `*.Tests` | 239 |
| `[Fact]` / `[Theory]` declarations | **1,300** (1,267 Fact, 33 Theory) |
| Largest suites (source files) | `Wip.Bones.Web.Tests` 59, `Wip.Bones.Tests` 58, `Wip.Bones.Host.Tests` 45 |
| Test projects executed by CI | **1 of 17** (`Wip.ShellHost.Tests` only) |

The counts are declaration counts, not executed cases; theories can expand. Static counts were taken with a read-only scan of `WIP/**/*.Tests/**/*.cs`.

## 4. CI truth (plan P0-03 / WP-0.4)

The only workflow file is `.github/workflows/tests-ci.yml`. Verified:

- Trigger: `push` and `pull_request` on **`main` only**, plus `workflow_dispatch`. The `WIP` branch is not covered.
- One job `tests` on `ubuntu-latest`; .NET 10.0.x; restore/build `Modus.slnx`.
- WIP test coverage: only `WIP/Wip.ShellHost.Tests/Wip.ShellHost.Tests.csproj`. The other 16 WIP test projects (including `Wip.Shell.E2E.Tests`, `Wip.Runtime.Tests`, `Wip.Builder.Tests`, all Bones suites) are not run.
- It also runs `HarnessSync.Tests`, `Modus.Core.Tests`, `Modus.Architecture.Tests`, and `Modus.Host.IntegrationTests`; this is the Modus gate, not the WIP gate.
- No zero-discovered-test guard; no TRX required for the 16 missing WIP projects; no WIP architecture job.

## 5. Phase 0 exit-criteria status (initial)

| Criterion (plan §3.2) | Status | Evidence |
|---|---|---|
| Baseline tag and clean working tree | **Not met** | No `wip-baseline*` tag; 308 dirty entries; decision pending |
| `Wip.slnx` includes all 37 projects | **Not met** | 24/37; 13 missing; 3 in neither solution |
| Coverage check fails on an orphan project | **Not met** | No coverage script exists |
| CI runs all offline suites; waivers with owner/expiry | **Not met** | 1/17 WIP suites; no waiver file |
| Governance guard committed and tested in CI | **Not met** | Guard exists in working tree only; no CI test |
| Architecture tests exist and run | **Not met** | No `Wip.Architecture.Tests`; `Modus.Architecture.Tests` covers Modus only |
| ADRs `ADR-IMPL-01..06` accepted | **Not met** | No ADR file in the repository; `docs/execution/decisions/` is empty |
| `docs/plan/baseline-test-status.md` | **Not met** | Does not exist |

WP-0.1 itself is **not complete**: the baseline is inventoried but not frozen; test inventory/known failures/waivers are not yet executed or recorded.

## 6. Governance guard (WP-0.2 precondition)

- Working tree: `WIP/Wip.Runtime/Runtime/WipRuntimeOrchestrator.cs:299` calls `WorkflowStageStateMapper.ResolveRunAdvancementTarget`.
- HEAD: `git grep ResolveRunAdvancementTarget HEAD -- WIP` returns no match.
- Therefore the guard is in-flight, uncommitted work and cannot be counted as a Phase 0 pass.

## 7. Tooling and repository hygiene

| Fact | Status |
|---|---|
| `.editorconfig` | Untracked, sets `charset = utf-8` |
| `tools/encoding/WriteUtf8.ps1`, `Test-StagedTextFileEncoding.ps1`, `Repair-TextFileUtf8.ps1` | Untracked; encoding helper exists |
| `.githooks/pre-commit` | Untracked; `core.hooksPath` is **unset**, so it is not active |
| `.gitignore` | `.tmp/` is **not ignored**; `*.log` ignores evidence logs such as the contract's `red.log`/`verify.log` (force-add or a negation rule will be needed) |
| `core.autocrlf` | `true`; no `.gitattributes`; Git warns LF→CRLF on changed files |
| Secret scan | 3 files match patterns; 0 added diff lines; see [secret-scan.md](secret-scan.md) |

## 8. Not yet verified (explicit)

The following were **not** run because the baseline is not frozen and the contract requires stopping for the baseline decision:

- `dotnet build Modus.slnx` / `dotnet build Wip.slnx` — still not run at the time of this file; see the build log after the freeze.
- any WIP test project (including `Wip.ShellHost.Tests`) — a `dotnet test` attempt aborted at testhost startup with `Win32Exception (5)`; no test executed. Recorded as F-ENV in `fail-1-testhost.md` and `BLK-TESTHOST-ENV`.
- `dotnet run --project tools/HarnessSync -- --check` — **now run**: initially failed on UTF-16 scratch files under `.tmp`; after excluding `.tmp` from the validator, exit 0. See `harnesssync-check.txt` and `harnesssync-check-after.txt`.
- shell or Bones host smoke tests — not run.

No build, test, or green claim is made here. These become WP-0.1 completion evidence after the baseline freeze, with the test-environment blocker disclosed.

## 9. Commands used

See [command-exit-codes.txt](command-exit-codes.txt) and [preflight.txt](preflight.txt). All captured git/dotnet inspection commands exited 0.
