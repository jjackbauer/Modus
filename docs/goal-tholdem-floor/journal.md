# Goal Journal — tholdem-positive-evidence-floor

> Append-only. See the stretch-goal-harness evidence protocol for entry format.

---

<!-- New entries go below this line -->

### OBS-001: The shipped policy has no positive-rate floor

- **Source**: `WIP/Wip.Tholdem.Agents/Learning/Refinement/SignatureDrivenRefinementPolicy.cs:58-71` (current tree)
- **Fact**: `StrongestUsable` selects the highest `Rate` among signals with `Sample >= MinimumSample`; a signal with `Rate == 0` is eligible, and the policy then edits the implicated parameter. `OQ-001` in the previous goal recorded this as a P1 residual.
- **Implications**: A floor experiment can be isolated behind a new default-0 option without changing the measured arm.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: DEC-001

### DEC-001: Add a default-0 `SignalRateFloor` and evaluate it on DEV then a fresh holdout

- **Decision**: Implement an inclusive `SignalRateFloor` (default 0), pre-register floors 0.00/0.05/0.10/0.20, tune on the six original DEV seeds, then run a single fresh holdout on new seeds.
- **Rationale**: OBS-001; a default of 0 preserves the 387-test baseline and the already-measured arm, while a positive floor tests the open question without contaminating the old holdout.
- **Alternatives considered**: Change the default directly (rejected: it would invalidate the accepted arm and its holdout); add a hidden test-only switch (rejected: the experiment must exercise the product path).
- **Related**: OBS-001

### RISK-001: A positive floor may reduce divergence by falling back to the legacy nudge

- **Risk**: Zero-rate signals may be rare, so the floor may change nothing; or it may remove edits that were producing divergence, making the arm worse.
- **Likelihood**: Medium
- **Impact**: Medium
- **Evidence**: The previous dev zero-effect rows were all baseline; the refinement arm had 0/30 zero-effect, so zero-rate selection may not be the driver.
- **Mitigation**: Pre-register the selection rule; if no floor qualifies, keep 0 and report the null; do not tune after the holdout.
- **Gate**: —

### OBS-002: The floor option is implemented with a default that preserves behavior

- **Source**: `WIP/Wip.Tholdem.Agents/Learning/Refinement/SignatureDrivenRefinementPolicy.cs:37-66`, `:153-165`; `WIP/Wip.Tholdem.Agents/Learning/TrainingOptions.cs:19`; `WIP/Wip.Tholdem.Agents/Learning/TrainingLoop.cs:195-200`
- **Fact**: The policy's last optional constructor parameter is a finite `[0,1]` `minimumSignalRate` (default 0), exposed as `MinimumSignalRate`; `StrongestUsable` filters `Sample >= 10 && Rate >= floor` inclusively; no passing signal falls back to the unchanged legacy proposer. `TrainingOptions.SignalRateFloor` defaults to 0 and is passed at the single policy construction site.
- **Implications**: F1 is delivered; the default 0 path is byte-identical and the positive floors are isolated opt-ins.
- **Confidence**: High
- **Agent**: implementer; verified by orchestrator
- **Related**: EVD-002, EVD-003, EVD-005

### HYP-001: F1 preserves the shipped arm while adding the floor

- **Claim**: The focused floor tests pass, the full assembly is 394/394, the pinned default challengers are unchanged, a floor-1.0 run equals the legacy challenger, and legacy CLI hashes and the frozen snapshot are unchanged.
- **Supporting OBS**: OBS-002
- **Why it matters**: A default change or a pinned-challenger change would invalidate the previously accepted experiment.
- **Falsification strategy**: Run the 7 focused tests and the full suite; compare the pinned challengers; run the floor-1.0 fallback test; run the legacy CLI and the frozen snapshot.
- **Status**: Supported
- **Confidence**: High

### VAL-001: F1 validation

- **Target**: HYP-001
- **Method**: Rebuilt Agents/tests; ran the focused `HarnessSignalFloor` class and the unfiltered assembly; read the filter/validation; ran the Host CLI twice into fresh legacy dirs and compared all hashes; rechecked the round-6 frozen snapshot.
- **Evidence examined**: `.tmp/goal-floor/focused-floor-verify.log` (7/7); `full-verify.log` (394/394); `SignatureDrivenRefinementPolicy.cs:153-165`; `HarnessSignalFloorTests.cs`; `.tmp/goal-floor/legacy-a/b` (7/7); `round-6/frozen-after-skills.txt`.
- **Result**: Supported
- **Conclusion**: F1 is delivered; 394/394 tests; legacy 7/7; pinned challengers and frozen hashes unchanged. The three positive floors are ready for the DEV probe.
- **Related**: EVD-002 to EVD-005

### DEC-002: Accept F1 and run the DEV floor arms

- **Decision**: Accept F1; keep the default 0; run the pre-registered DEV floors 0.05/0.10/0.20 and reuse the existing floor-0 rows.
- **Rationale**: VAL-001; the option is default-preserving and the baseline is green.
- **Alternatives considered**: Skip F1 and only test policy-level behavior (rejected: the probe needs the product pass-through); run the holdout now (rejected: the floor must be chosen on DEV first).
- **Related**: VAL-001

### OBS-003: All three positive floors are behaviorally identical to floor 0 on DEV

- **Source**: `.tmp/goal-floor/results-floor005.jsonl`, `results-floor010.jsonl`, `results-floor020.jsonl`; `.tmp/goal-refinement/experiment/results.jsonl`
- **Fact**: Each floor file has 30 refinement rows, 0 errors, and the same SHA-256 `2735ab51d5605553179d670674d61b65dbdca07d2a7b2ee691a277f0ac51b08d`; a field-by-field comparison of the 30 floor-0 refinement rows against the 30 floor-0.05 rows found 0 differences in seed, iteration, challenger id, divergence or mean. All floors match floor 0, not just each other.
- **Implications**: The floor is behaviorally inert on the DEV seeds; no zero-rate edit was filtered there. The mechanical selection rule chooses 0.05, but the benefit is unproven.
- **Confidence**: High
- **Agent**: probe; verified by orchestrator
- **Related**: EVD-006, EVD-007

### VAL-002: DEV floor validation

- **Target**: the three floor runs and the selection rule
- **Method**: Read all 90 rows, checked line counts, arm labels, `totalHands`, and SHA-256; compared floor-0.05 rows field by field to the floor-0 rows; ran `aggregate-floors.ps1` and read its selection.
- **Evidence examined**: `.tmp/goal-floor/results-floor00{5,10,20}.jsonl`; `.tmp/goal-refinement/experiment/results.jsonl` (hash `729a3a18…` unchanged); `docs/goal-tholdem-floor/round-2/floor-selection.md`/`.json`.
- **Result**: Supported
- **Conclusion**: The three positive floors are byte-identical to each other and field-identical to floor 0 on DEV. The pre-registered rule mechanically selects 0.05, but the floor produced no observed behavioral change.
- **Related**: EVD-006 to EVD-008

### DEC-003: Holdout the mechanically chosen 0.05, with an explicit no-op decision rule

- **Decision**: Run the fresh holdout with baseline, floor 0 and floor 0.05 on the same seeds. Adopt 0.05 only if it differs behaviorally from floor 0 on the holdout **and** meets the success rule; if it is identical to floor 0, retain 0 and report that the floor was a no-op on the tested space.
- **Rationale**: The goal says adopt only if the floor preserves exploration while removing arbitrary zero-rate edits. A floor that changes nothing removes nothing; the holdout decides whether it has any effect on fresh seeds. The mechanical selection rule chose 0.05, but the adoption criterion is the goal's stronger wording.
- **Alternatives considered**: Adopt 0.05 despite no observed effect (rejected: no evidence of benefit); skip the holdout (rejected: the fresh seeds could still expose a zero-rate case); change the floor after DEV (rejected: p-hacking).
- **Related**: OBS-003, VAL-002

### RISK-002: The floor may be a no-op on the fresh holdout too

- **Risk**: If no signal with a rate below 0.05 is selected on the fresh seeds, floor 0.05 will be identical to floor 0; the experiment would conclude the floor is inert rather than beneficial.
- **Likelihood**: Medium
- **Impact**: Medium
- **Evidence**: All three DEV floors were identical to floor 0; the selected signal's rate was above 0.20 on the observed seed.
- **Mitigation**: Report the no-op honestly; retain the shipped 0; record that a future adversarial/zero-rate fixture would be needed to prove the floor's behavior end-to-end.
- **Gate**: —

### TODO-003: Run the fresh holdout once

- **Task**: Run baseline, floor 0 and floor 0.05 on the six fresh holdout seeds, write raw rows, and aggregate against the pre-registered adoption rule.
- **Priority**: P0
- **Depends on**: DEC-003
- **Status**: Done
- **Related**: DEC-003

### OBS-004: The chosen floor is a no-op on the fresh holdout too

- **Source**: `.tmp/goal-floor/results-holdout-floor0.jsonl`, `.tmp/goal-floor/results-holdout.jsonl`
- **Fact**: The holdout used six never-used seeds (`1099511827776 + i*1000`). The floor-0 file has 60 rows (30 baseline, 30 floor 0); the chosen file has 30 rows (floor 0.05). The 30 chosen rows are field-identical to the 30 floor-0 rows (0 differences in seed, iteration, challenger, divergence or mean). Chosen vs baseline: median per-seed ratio 4.906 (pooled 4.8125, one zero-baseline substitution), bootstrap 95% CI [3.917, 16], zero-effect 0/30 vs 7/30; success rule met. Chosen vs floor 0: ratio 1.0 on every seed and equal zero-effect.
- **Implications**: The floor is behaviorally inert on 12 tested seeds; the success rule is satisfied, but the floor has no observed effect over floor 0, so the no-op adoption rule says retain 0.
- **Confidence**: High
- **Agent**: probe; verified by orchestrator
- **Related**: EVD-010, EVD-011, EVD-012

### VAL-003: Holdout validation

- **Target**: F4 and the no-op adoption rule
- **Method**: Read all 90 rows, checked counts/arms/seeds/`totalHands`, hashes and the raw row equality; ran `aggregate-holdout.ps1` with the pre-registered pooled-mean substitution; compared the chosen arm to floor 0 field by field.
- **Evidence examined**: `.tmp/goal-floor/results-holdout-floor0.jsonl` (`3dcd6373…`), `results-holdout.jsonl` (`27b6d7f1…`), `round-3/holdout-summary.md`/`.json`.
- **Result**: Supported
- **Conclusion**: The 0.05 arm reproduces the floor-0 arm exactly on the fresh holdout; the baseline comparison passes (median 4.91, CI lower 3.92, zero-effect 7/30 to 0/30), but the floor itself has no demonstrated effect.
- **Related**: EVD-010 to EVD-013

### DEC-004: Retain floor 0; reject the floor as behaviorally inert

- **Decision**: Keep the shipped `SignalRateFloor = 0`. Do not promote 0.05 (or any positive floor) as the recommended/default setting. Keep the default-0 option and its tests in the product for future adversarial zero-rate fixtures.
- **Rationale**: OBS-004/VAL-003; the floor was byte-identical to floor 0 on 30 DEV and 30 fresh-holdout refinement rows. A floor that removes no observed edit has no demonstrated benefit, even though the arm-vs-baseline success rule passes.
- **Alternatives considered**: Adopt 0.05 as a harmless guard (rejected: no evidence of effect, and changing the default would invalidate the pinned arm); raise the floor (rejected: equally inert on the tested space); claim a new improvement (rejected: the divergence gain is the existing signature arm, not the floor).
- **Related**: VAL-003, OBS-003

### RISK-003: The floor's intended behavior is untested end-to-end

- **Risk**: The product-level filter is proven by unit tests, but no tested seed actually selected a zero-rate signal, so the end-to-end zero-rate-rescue path was not observed.
- **Likelihood**: High (observed)
- **Impact**: Low (the option remains default-off and the filter is unit-tested)
- **Evidence**: All 30+30 refinement rows identical to floor 0; the DEV signature dump showed the usable signal at rate 0.257 on the observed seed.
- **Mitigation**: A future fixture can force a zero-rate signal through a scripted proposer/signature input; no product change is needed.
- **Gate**: —
