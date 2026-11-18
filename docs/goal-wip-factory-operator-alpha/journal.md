# Journal — Wip.Factory Operator Alpha v0.3

Append-only. Verified facts only; agent claims enter after the orchestrator re-checks them.

## OBS-001 — Baseline branch and spec state

- **Source**: session state; `harness/requirements/Wip.Factory.Operator.Alpha.v0.3.md`
- **Fact**: Implementation target is branch `feature/wip-factory-alpha` at `4a9793a`. The v0.3 spec is
  committed locally (not yet pushed). v0.1 is marked SUPERSEDED, v0.2 was never accepted.
- **Confidence**: High

## OBS-002 — Prior green counts are historical, not current

- **Source**: this session, earlier rounds (v0.3 N-01 explicitly discounts them)
- **Fact**: Earlier in this session the four factory suites reported 86 passing tests including the
  82/82 compliance gate, and the ten core WIP suites reported 460 passed / 3 failed. Under N-01 these
  must be **re-run and recorded fresh** before any delivery claim; they are not carried forward.
- **Confidence**: High

## DEC-001 — D-08 A and D-09 adopted as ratified

- **Source**: user request (v0.3 header and §2)
- **Decision**: Add exactly one adjacency edge `DeliveryValidation → NeedsRework`, legal **only before**
  the merge operation is applied. Policy approval permitted only for explicitly preapproved immutable
  templates with validated parameters; human fallback otherwise; template approval never authorises merge.
- **Owner**: user
- **Status**: approved

## OQ-001 — Who supplies the blind PA-01/02 requirements?

- **Question**: §1.1 requires an independent evaluator to select and disclose the two final requirements
  only after the implementation is frozen. In this session no external evaluator exists.
- **Impact**: PA-01/PA-02 cannot be honestly claimed without a withheld pair. Default plan: implementer
  authors two *development* fixtures for CI, and PA-01/02 remain **unclaimed** until the user supplies
  blind requirements after freeze.
- **Status**: open — needs user action at freeze time, not now.

## RISK-001 — Live model budget for blind runs

- **Risk**: PA-01/02 require the real configured model. Earlier this session a real DeepSeek round trip was
  measured (17 in / 3 out tokens), so the provider works, but a full app build is many steps and may
  exhaust budget or rate limits mid-run.
- **Mitigation**: bounded steps/timeouts; report `inconclusive` rather than substituting a fake model.
## OBS-003 — Independent intent audit FAILED the v1 goal contract

- **Source**: `audit-intent.md` (subagent `7aa8fb02`, read-only session; content persisted by me because
  the agent had no write tool), plus my own check of `deliverables.md`
- **Fact**: 18 mechanisms audited: **16 KEPT (3 full, 10 partial, 3 nominal), 0 REPLACED, 2 REMOVED**.
  REMOVED were **D-06 control root** and **D-04 three-role default** — neither appeared anywhere in the
  goal folder. Additionally the audit found ten partial/nominal rows (six tools named but not enumerated,
  "effectively-once" as a slogan with no operation identity, missing attribution chain in PA-03, PA-05
  missing "no false release / no lost failure record", PA-07 missing release artifact and process tree,
  PA-08 missing three refusal classes, absent CLI contract and manifest fields) and **one clause that
  contradicted the spec**: v1 stop criterion 1 allowed a goal to be *stopped* on an honestly-recorded
  failing PA gate, while v0.3:75 says a failed gate means the delivery is not accepted.
- **Implications**: **G0 FAILED**. Implementation could not start. This is the second time in this
  project that a completeness claim of mine ("all KEPT — no replacement, no exclusion") was false; the
  first was the 82/82 test count in the previous goal.
- **Confidence**: High
- **Related**: VAL-001, EVD-009, EVD-010, DEC-002..004

## VAL-001 — FALSIFIED: "all mechanisms KEPT" in goal.md v1

- **Claim**: v1 goal.md's anchors table asserted every user mechanism was KEPT.
- **Counterexample**: the table covered a 10-item subset; D-06 and D-04 were absent from the entire file
  (0 hits for "control"/"outside"/"three"/"role" in `goal.md`, and 0 hits in `deliverables.md` too —
  EVD-009).
- **Status**: Falsified. Superseded by goal.md v2's 18-row binding table.

## DEC-002 — PA-01 pass predicate ends at Released

- **Source**: audit §5; v0.3:58 vs v0.3:43/44/116
- **Decision**: PA-01 = merged, post-merge verified, reached `Released`. Liveness/`serve` proof belongs to
  PA-07. This resolves a contradiction the spec could not resolve itself: one gate cannot span two
  terminal states.
- **Owner**: agent (engineering resolution), consistent with v0.3's "proposed engineering resolutions"
  clause
- **Status**: approved

## DEC-003 — PA-06 and PA-08 are proven through harness injection seams

- **Source**: audit §5 secondary finding; v0.3:479 ("CLI and runtime")
- **Decision**: mutating an approval-bound field and requesting an out-of-profile tool are induced by
  test-only seams unreachable from the operator CLI.
- **Owner**: agent
- **Status**: approved

## DEC-004 — A failed PA gate is NOT acceptance

- **Source**: audit §3
- **Decision**: withdrawn the v1 clause that let "honest recording of a failing gate" stop the goal.
  Failure must be reported, and it never constitutes completion.
- **Owner**: agent, correcting my own contract
- **Status**: approved
## OBS-004 — M1 stop condition MET through the real CLI

- **Source**: operator run in a temp repo; commit `HEAD` after this round
- **Fact**: `factory requirement submit` → work item `WORK-bbb00f48` at `Proposed` v1 with requirement digest
  `sha256:08a4dc67…`. `factory run --until-idle` consumed the submission, the handler returned `Recorded`
  and the item became `Proposed` **v2** with a proposal digest — facts committed, state unchanged, exactly
  as v0.3 §3.2 requires. A **fresh process** read the proposal back. `factory contract approve --digest`
  froze it; `factory run` advanced `Proposed → RequirementsAligned` at **v4**; a second fresh process read
  `RequirementsAligned` from durable storage. Identical resubmission returned the same work item.
  Refusals verified: missing class + no expectations (`exit 2`), and control root inside the target
  (`control-root-inside-target`, `exit 2`).
- **Implications**: The intake → proposal → freeze → alignment spine is real and durable, and it runs
  through the production host rather than a demo. PA gates remain 0/8 because no implementation, contract
  execution, review, merge or serve exists yet.
- **Confidence**: High
- **Related**: EVD-011, EVD-012, EVD-013

## OBS-005 — Two defects found by running, not by reading

- **Source**: the first two CLI runs
- **Fact**: (1) the dispatch worker routes on `Data["workItemId"]`; my events omitted it, so every dispatch
  failed and the event was **quarantined** with "carries no 'workItemId'". (2) `ContractApproveAsync`
  committed the snapshot at its current version instead of `version + 1`, refused with "The snapshot
  version 2 must be exactly 3". Both were invisible to the build and to a code read; both surfaced on the
  first real run.
- **Implications**: Reinforces the harness rule that an unreproduced claim is not a delivery. The CLI path
  had never executed before this round.
- **Confidence**: High
- **Related**: OBS-004