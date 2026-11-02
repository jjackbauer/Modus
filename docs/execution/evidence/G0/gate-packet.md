# GATE: G0 — Phase 0 exit

**Plan:** WIP-IMPL-001
**Phase:** 0 — Truth and baseline
**Packet date:** 2026-11-02T12:58-03:00
**Prepared by:** WIP Program Executor
**Evidence root:** `docs/execution/evidence/`

## Status

**NOT READY — two decisions required.**

Phase 0 implementation is complete except for the first GitHub Actions run and ADR acceptance. This packet asks for those two decisions. It does not self-approve G0.

## Criteria (plan `3.2 / `5.1)

| # | Criterion | Status | Evidence / blocker |
|---|---|---|---|
| 1 | Baseline tag and clean working tree | **PASS** | Tag `wip-baseline-2026-11-02` → `99c0b08`; working tree clean. |
| 2 | `Wip.slnx` includes all 37 WIP projects; coverage check fails on orphans | **PASS** | 37/37 WIP + architecture test project = 38 solution projects; [coverage-after](../WP-0.3/coverage-after.txt); orphan case [coverage-orphan-red](../WP-0.3/coverage-orphan-red.txt) exits 1. |
| 3 | CI runs all offline suites; known failures waived with owners/expiry | **BLOCKED** | Workflows/scripts/waivers are committed; local gate green (15 projects / 21 waivers) and negative cases exit 1. GitHub Actions has not run: [BLK-CI-REMOTE](../blockers/BLK-CI-REMOTE.md). |
| 4 | Governance guard committed and tested in CI | **PASS local / CI pending** | 3 runtime + 2 Shell E2E guard tests green ([WP-0.2](../WP-0.2/notes.md)); CI steps added; first remote run pending. |
| 5 | Architecture fitness tests exist and run | **PASS local / CI pending** | WIP architecture tests 3/3; injected forbidden edge exits 1 and source restored with 0 diff ([WP-0.5](../WP-0.5/notes.md)); CI step added; remote run pending. |
| 6 | ADR-IMPL-01..07 reviewed and accepted | **NOT MET** | Seven **Proposed** ADRs; no ADR is self-accepted. [decisions](../decisions/README.md). |
| 7 | G0 passed | **NOT MET** | Criteria 3 and 6. |

## Verified state

- Baseline: 148 modified + 160 untracked (308 porcelain / 522 files) frozen into `99c0b08`; tag exists.
- Tests: **1,354 executed / 1,322 passed / 32 failed**, 3 suites aborted after known test-specific hangs/crashes. The 32 failures are recorded with owners/expiry; **no test was skipped or deleted**.
- Offline CI gate: 21 exact-test waivers + 2 known aborts; missing project, unwaived failure, and zero-test cases all fail.
- Solution: `dotnet build Wip.slnx` exit 0, 0 errors, 113 warnings.
- Architecture tests: 3/3; one temporary `Wip.ShellHost → Wip.Bones` waiver under ADR-07.
- NuGet 0.0.2: 18 packages published at the user's explicit direction; the red test baseline is recorded as a risk (DEV-001).

## Decisions required

1. **Authorize a push** of `WIP` or a short-lived branch to run `Tests CI`. This is the only way to close WP-0.4. No push has been made. After the run, attach the run URL/artifacts; the gate must pass against `docs/plan/test-waivers.json`.
2. **Accept or amend ADR-IMPL-01..07.** ADR-07 is a time-bounded waiver (expiry 2026-11-30) for the known ShellHost→Bones coupling; WP-2.6 removes it. G0 cannot pass with them still `Proposed`.
3. **Acknowledge the deviations/risks:** DEV-001 (out-of-plan NuGet 0.0.2), DEV-002 (project-level CI categories instead of Category traits until WP-4.3), and the 32 red-baseline failures/3 aborts with their owners/expiry.

## Risks

- The red baseline is real: 32 tests fail and 3 suites abort. Waivers make the gate truthful, not green.
- The first remote CI run may expose Windows/Linux or Playwright differences; a new failure will fail the gate.
- The ShellHost→Bones waiver is a release blocker if it survives WP-2.6.
- Category traits are deferred (DEV-002); CI selects by project group for now.
- NuGet 0.0.2 was published before the red test inventory; triage and a patch/unlist may be needed.

## Recommendation

Authorize the short-lived-branch push and accept ADR-IMPL-01..07 with ADR-07's expiry and DEV-002 noted. Then run CI, attach the evidence, and pass G0 if the waiver gate is green and no unwaived failure appears. Do not start Phase 1 before that.

## If approved, next action

Push a short-lived branch, run `Tests CI`, attach the run URL/TRX, update WP-0.4 and WP-0.6, and produce the G0 pass record. Phase 1 starts only after G0.

## Evidence index

- [WP-0.1 baseline and tests](../WP-0.1/notes.md), [baseline-test-status.md](../../../plan/baseline-test-status.md)
- [WP-0.2 governance guard](../WP-0.2/notes.md)
- [WP-0.3 solution topology](../WP-0.3/notes.md)
- [WP-0.4 CI gate](../WP-0.4/notes.md), [gate green](../WP-0.4/gate-green.txt), [gate negative](../WP-0.4/gate-negative.txt)
- [WP-0.5 architecture tests](../WP-0.5/notes.md)
- [WP-0.6 ADRs](../decisions/README.md)
- [BLK-CI-REMOTE](../blockers/BLK-CI-REMOTE.md), [DEV-001](../deviations/DEV-001-nuget-release.md), [DEV-002](../deviations/DEV-002-ci-category-traits.md)
