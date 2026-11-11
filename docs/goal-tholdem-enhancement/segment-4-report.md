# Segment report — both requested experiments (round 21)

**Request:** `/stretch-goal-harness do both` — (1) diagnosis arm in an equal-budget retry; (2) the
16-request LLM protocol with gate verification.
**Result:** both were run; both are nulls. No anchor-3 or LLM claim is made.

## 1. Diagnosis equal-budget ablation — `no-benefit`

- Pre-registration hash `fa312c5d…`; four starting genomes, 4 iterations each, same seed/plans;
  diagnosis arm adds a 40-hand capture per iteration.
- Deterministic: **0/16** promotions, 4 Unsafe.
- Diagnosis: **0/16** promotions, 7 Unsafe.
- Outcome rule required >=2 tasks with a verified promotion, zero Unsafe, and strictly more
  promotions; it fails all three. `HYP-003` is falsified.

## 2. LLM pilot — 16/16 parsed, 0 gate-verified

- The approved 16 requests were spent exactly once; all 16 responses parsed into bounded genomes.
- Each proposal was evaluated through the four-seat gate vs its task start on a small plan:
  **0 VerifiedImprovement**, 8 Rejected, 8 Unsafe; mean range -875.31..690.94 bb/100.
- **No control arm was run on this same candidate budget**, so this is a pilot, not an attribution
  comparison. `CAP-002` records that the live model is reachable and parseable.

## Verification

- Production code unchanged this round; the last full suite remains **584/584**, build 0 errors,
  HarnessSync exit 0.
- Experiments persisted with preregistration, results and logs.
- Anchor 3 remains the recorded null.

## Next

- Add a diagnosis arm to the equal-budget search runner itself (not only the four-seat loop), with
  more tasks and an independent replication before any anchor-3 retry.
- For LLM attribution, run the non-LLM control arms on the same candidate budget; without them the
  0/16 gate result cannot support an attribution claim.
