# GATE: G0 — Phase 0 exit

**Plan:** WIP-IMPL-001
**Phase:** 0 — Truth and baseline
**Packet date:** 2026-11-02T14:31-03:00
**Prepared by:** WIP Program Executor
**Evidence root:** `docs/execution/evidence/`

## Status

**READY FOR G0 DECISION.**

Phase 0 criteria are met and CI is green. The product owner authorized the push and accepted ADR-IMPL-01..07 on 2026-11-02. This packet does **not** self-approve G0; it requests the explicit G0 decision, including the six CI-exposed waivers under DEV-003.

## Criteria (plan §3.2 / §5.1)

| # | Criterion | Status | Evidence / blocker |
|---|---|---|---|
| 1 | Baseline tag and clean working tree | **PASS** | Tag `wip-baseline-2026-11-02` → `99c0b08`; working tree clean. |
| 2 | `Wip.slnx` includes all 37 WIP projects; coverage check fails on orphans | **PASS** | 37/37 WIP + architecture test project = 38 solution projects; [coverage-after](../WP-0.3/coverage-after.txt); orphan case [coverage-orphan-red](../WP-0.3/coverage-orphan-red.txt) exits 1. |
| 3 | CI runs all offline suites; known failures waived with owners/expiry | **PASS** | Run [41489673359](https://github.com/jjackbauer/Modus/actions/runs/41489673359) is green on all four jobs; 15 offline projects, 27 exact waivers, 0 unwaived failures. Six CI-exposed Bones failures are waived under DEV-003; two Modus documentation failures were fixed by committing the required proof artifacts. |
| 4 | Governance guard committed and tested in CI | **PASS** | 3 runtime + 2 Shell E2E guard tests green locally ([WP-0.2](../WP-0.2/notes.md)); guard steps run in the WIP workflow. |
| 5 | Architecture fitness tests exist and run | **PASS** | WIP architecture tests 3/3; injected forbidden edge exits 1 and source restored with 0 diff ([WP-0.5](../WP-0.5/notes.md)); `wip-architecture` job green in run 41489673359. |
| 6 | ADR-IMPL-01..07 reviewed and accepted | **PASS** | Accepted by the product owner on 2026-11-02; [decision record](decision-2026-11-02-g0-adrs.md). ADR-07 expires 2026-11-30. |
| 7 | G0 passed | **AWAITING HUMAN APPROVAL** | All technical criteria pass; this packet does not self-approve. |

## Verified state

- Baseline: 148 modified + 160 untracked (308 porcelain / 522 files) frozen into `99c0b08`; tag exists.
- Tests: **1,354 executed / 1,322 passed / 32 baseline failures**, 3 suites aborted after known test-specific hangs/crashes. Six more failures were exposed by CI (38 known failing total); all are recorded with owners/expiry; **no test was skipped or deleted**.
- Offline CI gate: 27 exact-test waivers (21 baseline + 6 CI-exposed under DEV-003) + 2 known aborts; missing project, unwaived failure, and zero-test cases all fail.
- Solution: `dotnet build Wip.slnx` exit 0, 0 errors, 113 warnings.
- Architecture tests: 3/3; one temporary `Wip.ShellHost → Wip.Bones` waiver under ADR-07.
- NuGet 0.0.2: 18 packages published at the user's explicit direction; the red test baseline is recorded as a risk (DEV-001).

## Decisions

1. **Completed:** the product owner authorized pushing `WIP`; the branch is pushed.
2. **Completed:** ADR-IMPL-01..07 accepted; ADR-07 remains time-bounded (expiry 2026-11-30) and G1–G4 still gate their mechanisms/contracts.
3. **Acknowledged:** DEV-001 (out-of-plan NuGet 0.0.2), DEV-002 (project-level CI categories until WP-4.3), and DEV-003 (six CI-exposed Bones failures, including two compliance meta-tests, waived until 2026-11-16).
4. **Remaining:** explicit G0 approval. All technical criteria pass; approval starts Phase 1.

## Risks

- The red baseline is real: 32 baseline failures + 6 CI-exposed failures = 38 known, and 3 suites abort. Waivers make the gate truthful, not green.
- DEV-003 waives two compliance meta-tests (Bones board and status API) until 2026-11-16; this is a release risk, not a fix.
- The ShellHost→Bones waiver is a release blocker if it survives WP-2.6.
- Category traits are deferred (DEV-002); CI selects by project group for now.
- NuGet 0.0.2 was published before the red test inventory; triage and a patch/unlist may be needed.

## Recommendation

Approve G0 with DEV-003 disclosed, or reject and require the six Bones waivers to be fixed first. CI is green; the remaining decision is human.

## If approved

Record the G0 approval, mark Phase 0 complete, and start Phase 1. If rejected, fix the DEV-003 tests and rerun before G0.

## Evidence index

- [WP-0.1 baseline and tests](../WP-0.1/notes.md), [baseline-test-status.md](../../../plan/baseline-test-status.md)
- [WP-0.2 governance guard](../WP-0.2/notes.md)
- [WP-0.3 solution topology](../WP-0.3/notes.md)
- [WP-0.4 CI gate](../WP-0.4/notes.md), [gate green](../WP-0.4/gate-green.txt), [gate negative](../WP-0.4/gate-negative.txt)
- [WP-0.5 architecture tests](../WP-0.5/notes.md)
- [WP-0.6 ADRs](../decisions/README.md), [G0 ADR decision](decision-2026-11-02-g0-adrs.md)
- [Green WIP run 41489673359](https://github.com/jjackbauer/Modus/actions/runs/41489673359)
- [DEV-003](../deviations/DEV-003-ci-exposed-bones-waivers.md)
- [BLK-CI-REMOTE](../blockers/BLK-CI-REMOTE.md), [DEV-001](../deviations/DEV-001-nuget-release.md), [DEV-002](../deviations/DEV-002-ci-category-traits.md)
