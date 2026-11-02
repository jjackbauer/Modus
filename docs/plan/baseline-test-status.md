# Baseline test status — WIP-IMPL-001 / WP-0.1

**Status:** COMPLETE — **red baseline recorded** (1,305 passed / 49 failed / 3 aborted suites).
**Baseline commit:** `99c0b087e8ad5b9993baef807bab92b4cd37aa0e` (tag `wip-baseline-2026-11-02`)
**Date:** 2026-11-02
**Testhost workaround:** [testhost-workaround.md](../execution/evidence/WP-0.1/testhost-workaround.md)

## Build

| Item | Result |
|---|---|
| Command | `dotnet build Modus.slnx -c Release --nologo` |
| Exit | **0** |
| Errors | **0** |
| Warnings | **119** (Test SDK 17.14.1 build) |
| Elapsed | 12.76 s |
| Evidence | [build-testsdk-17.14.1.txt](../execution/evidence/WP-0.1/build-testsdk-17.14.1.txt) |

`Modus.slnx` contains 34 of 37 WIP projects. `Wip.Bones.Tests`, `Wip.Bones.Benchmark`, and `BonesLibraryRecovery` are outside both solutions (WP-0.3).

## Test surface

| Item | Value |
|---|---|
| WIP test projects | 17 |
| Test source files | 239 |
| Declarations | 1,300 |
| Executed (theories expanded) | **1,354** |
| Passed | **1,305** |
| Failed | **49** |
| Skipped | 0 |
| Fully green projects | **8 of 17** |
| Aborted suites | **3** — `Wip.Modus.Tests`, `Wip.Bones.Host.Tests`, `Wip.Shell.E2E.Tests` |
| Projects in CI | 1 of 17 (`Wip.ShellHost.Tests`) |

## Project results

| Project | Total | Passed | Failed | Exit | Note |
|---|---:|---:|---:|---:|---|
| `Wip.Abstractions.Tests` | 44 | 44 | 0 | 0 | |
| `Wip.Artifacts.Local.Tests` | 4 | 4 | 0 | 0 | |
| `Wip.Builder.Tests` | 32 | 32 | 0 | 0 | |
| `Wip.Modus.Tests` | 18 | 17 | 1 | 1 | 5-min hang in `RepositoryLayout_GivenExecutionAffectingDocsAndScripts`; README map missing `Wip.Bones` |
| `Wip.Policy.LocalSafe.Tests` | 40 | 40 | 0 | 0 | |
| `Wip.Runtime.Tests` | 72 | 54 | 18 | 1 | Hardcoded `C:/repo` writes outside the sandbox |
| `Wip.Shell.Tests` | 32 | 30 | 2 | 1 | Missing merge-completion output |
| `Wip.Tools.Shell.Tests` | 25 | 25 | 0 | 0 | |
| `Wip.Validation.DotNet.Tests` | 54 | 54 | 0 | 0 | |
| `Wip.Workspaces.Git.Tests` | 12 | 12 | 0 | 0 | |
| `Wip.Bones.Tests` | 383 | 381 | 2 | 1 | Compliance gates |
| `Wip.Bones.ModelProviders.DeepSeek.Tests` | 48 | 47 | 1 | 1 | Compliance gate |
| `Wip.Bones.Host.Tests` | 153 | 146 | 7 | 1 | Status API timeouts, null player, compliance; testhost crash |
| `Wip.ShellHost.Tests` | 47 | 47 | 0 | 0 | |
| `Wip.Bones.E2E.Tests` | 10 | 3 | 7 | 1 | Six shell-harness assertions + compliance |
| `Wip.Shell.E2E.Tests` | 104 | 102 | 2 | 1 | Registry mismatch; meta-test recursion/crash |
| `Wip.Bones.Web.Tests` | 276 | 267 | 9 | 1 | Five compliance gates + four DOM/Playwright assertions |

TRX evidence: [test-results](../execution/evidence/WP-0.1/test-results/). Three aborted suites still produced counters; aborts are recorded separately.

## Baseline failure register

Every failure is tracked; **no test was deleted, skipped, or loosened**. A waiver here means WP-0.1 may close with the failure recorded; it does not make the suite green.

| Group | Failed | Class | Owner | Expiry |
|---|---:|---|---|---|
| Runtime hardcoded `C:/repo` writes | 18 | F-ENV / portability | runtime engineer | 2026-11-09 |
| Shell merge completion output | 2 | F-TEST / F-SPEC | runtime lead | 2026-11-09 |
| Modus README missing `Wip.Bones` | 1 | F-DOC | tech lead / docs | 2026-11-09 |
| Modus layout scan hang | 1 abort | F-TEST / F-ENV | QA | 2026-11-09 |
| Bones.Tests compliance gates | 2 | F-SPEC | tech lead + QA | 2026-11-16 |
| DeepSeek compliance gate | 1 | F-SPEC | tech lead + QA | 2026-11-16 |
| Bones.Host status/API + compliance | 7 | F-TEST / F-FLAKE / F-SPEC | Bones engineer + QA | 2026-11-09 |
| Bones.E2E shell harness + compliance | 7 | F-TEST / F-ENV | Bones engineer | 2026-11-09 |
| Bones.Web compliance + DOM/Playwright | 9 | F-SPEC / F-TEST | Web engineer + QA | 2026-11-09 |
| Shell.E2E registry + meta-test | 2 | F-TEST / F-ENV | QA | 2026-11-09 |

Compliance-gate subtotal: **11** (Bones 2, DeepSeek 1, Host 1, E2E 1, Web 5, Shell E2E 1).

## Root causes and next action

- **Runtime 18:** tests pass literal `C:/repo` paths and the orchestrator creates `.wip/sessions/...` there; the testhost sandbox denies writes outside the workspace. Use a workspace-backed test path or junction.
- **Shell 2:** the shell loop no longer emits the expected merge-completion text; possible in-flight governance regression or stale assertion.
- **Modus:** README project map is missing `Wip.Bones`; `RepositoryLayoutTests` scanning `WIP` exceeded the 5-minute blame-hang limit.
- **Compliance gates:** planned integration tests lack behavior-proof assumptions or checklist rows lack Trait-bound tests (known report debt).
- **Bones.Host:** multiple `BonesStatusApiTests` wait 2 minutes for host status and time out; testhost crashed after abort.
- **Shell.E2E meta-test:** `WipTestBaseline_GivenFullWipTestMatrix` recursively runs the matrix and spawned an orphan `dotnet test Wip.Builder.Tests` process; it was stopped and the suite aborted.

**Waiver policy:** the 49 failures are recorded baseline red, not skipped. WP-0.1 can close because the baseline is truthfully inventoried; fixes belong to WP-0.2/0.4/0.5 and the owners above. G0 is still **not ready** until WP-0.2–WP-0.6 complete.

**Next:** commit the Test SDK upgrade and this inventory; start WP-0.2 with the Shell merge failures as a priority signal; run the same commands in CI once WP-0.4 lands (the sandbox hook must not be required there).
