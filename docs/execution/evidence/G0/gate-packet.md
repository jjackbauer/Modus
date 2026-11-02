# GATE: G0 — Phase 0 exit

**Plan:** WIP-IMPL-001
**Phase:** 0 — Truth and baseline
**Packet date:** 2026-11-02T13:24-03:00
**Prepared by:** WIP Program Executor
**Evidence root:** `docs/execution/evidence/`

## Status

**NOT READY — CI run in progress.**

Phase 0 implementation is complete. The product owner authorized the push and accepted ADR-IMPL-01..07 on 2026-11-02. The post-split WIP run is in progress; G0 cannot pass until its offline waiver gate is green. This packet does not self-approve G0.

## Criteria (plan §3.2 / §5.1)

| # | Criterion | Status | Evidence / blocker |
|---|---|---|---|
| 1 | Baseline tag and clean working tree | **PASS** | Tag `wip-baseline-2026-11-02` → `99c0b08`; working tree clean. |
| 2 | `Wip.slnx` includes all 37 WIP projects; coverage check fails on orphans | **PASS** | 37/37 WIP + architecture test project = 38 solution projects; [coverage-after](../WP-0.3/coverage-after.txt); orphan case [coverage-orphan-red](../WP-0.3/coverage-orphan-red.txt) exits 1. |
| 3 | CI runs all offline suites; known failures waived with owners/expiry | **IN PROGRESS** | Post-split run [41480351635](https://github.com/jjackbauer/Modus/actions/runs/41480351635): Ubuntu build, Windows build, and architecture jobs green; offline waiver gate running. Earlier failures were caused by `WIP.slnx` vs `Wip.slnx` case on Ubuntu, fixed in `6a06e4c`. |
| 4 | Governance guard committed and tested in CI | **PASS** | 3 runtime + 2 Shell E2E guard tests green locally ([WP-0.2](../WP-0.2/notes.md)); guard steps run in the WIP workflow. |
| 5 | Architecture fitness tests exist and run | **PASS** | WIP architecture tests 3/3; injected forbidden edge exits 1 and source restored with 0 diff ([WP-0.5](../WP-0.5/notes.md)); `wip-architecture` job green in run 41480351635. |
| 6 | ADR-IMPL-01..07 reviewed and accepted | **PASS** | Accepted by the product owner on 2026-11-02; [decision record](decision-2026-11-02-g0-adrs.md). ADR-07 expires 2026-11-30. |
| 7 | G0 passed | **NOT MET** | Criterion 3 is still running. |

## Verified state

- Baseline: 148 modified + 160 untracked (308 porcelain / 522 files) frozen into `99c0b08`; tag exists.
- Tests: **1,354 executed / 1,322 passed / 32 failed**, 3 suites aborted after known test-specific hangs/crashes. The 32 failures are recorded with owners/expiry; **no test was skipped or deleted**.
- Offline CI gate: 21 exact-test waivers + 2 known aborts; missing project, unwaived failure, and zero-test cases all fail.
- Solution: `dotnet build Wip.slnx` exit 0, 0 errors, 113 warnings.
- Architecture tests: 3/3; one temporary `Wip.ShellHost → Wip.Bones` waiver under ADR-07.
- NuGet 0.0.2: 18 packages published at the user's explicit direction; the red test baseline is recorded as a risk (DEV-001).

## Decisions

1. **Completed:** the product owner authorized pushing `WIP`; the branch is pushed.
2. **Completed:** ADR-IMPL-01..07 accepted; ADR-07 remains time-bounded (expiry 2026-11-30) and G1–G4 still gate their mechanisms/contracts.
3. **Acknowledged:** DEV-001 (out-of-plan NuGet 0.0.2), DEV-002 (project-level CI categories until WP-4.3), and the red-baseline failures/aborts with owners/expiry.
4. **Remaining:** the offline waiver gate in run 41480351635 must finish green.

## Risks

- The red baseline is real: 32 tests fail and 3 suites abort. Waivers make the gate truthful, not green.
- The first remote CI run may expose Windows/Linux or Playwright differences; a new failure will fail the gate.
- The ShellHost→Bones waiver is a release blocker if it survives WP-2.6.
- Category traits are deferred (DEV-002); CI selects by project group for now.
- NuGet 0.0.2 was published before the red test inventory; triage and a patch/unlist may be needed.

## Recommendation

Wait for run 41480351635. If the offline gate is green and no unwaived failure appears, close WP-0.4, record the run/TRX evidence, and pass G0. Do not start Phase 1 before that.

## If CI is green

Attach the run URL/TRX, update WP-0.4 to done, produce the G0 pass record, and start Phase 1.

## Evidence index

- [WP-0.1 baseline and tests](../WP-0.1/notes.md), [baseline-test-status.md](../../../plan/baseline-test-status.md)
- [WP-0.2 governance guard](../WP-0.2/notes.md)
- [WP-0.3 solution topology](../WP-0.3/notes.md)
- [WP-0.4 CI gate](../WP-0.4/notes.md), [gate green](../WP-0.4/gate-green.txt), [gate negative](../WP-0.4/gate-negative.txt)
- [WP-0.5 architecture tests](../WP-0.5/notes.md)
- [WP-0.6 ADRs](../decisions/README.md), [G0 ADR decision](decision-2026-11-02-g0-adrs.md)
- [WIP run 41480351635](https://github.com/jjackbauer/Modus/actions/runs/41480351635)
- [BLK-CI-REMOTE](../blockers/BLK-CI-REMOTE.md), [DEV-001](../deviations/DEV-001-nuget-release.md), [DEV-002](../deviations/DEV-002-ci-category-traits.md)
