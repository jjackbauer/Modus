# Segment report — items 1–3, round 20

**Request:** `/stretch-goal-harness go on` after the items 1–3 segment.
**Status:** item 1 delivered; item 2 now wired into the four-seat loop and piloted; item 3a partial;
item 3b pre-registered and pending a budget. Goal active.

## Item 2 — diagnosis-driven proposals: wired, pilot null

- `TrainingTrajectoryCapture` gained a four-seat overload; `FourSeatTrainingOptions` gained
  `DiagnosisDriven`, `SignatureSource` and `DiagnosisHands`; `train4` gained `--diagnosis`/
  `--diagnosis-hands`.
- Injected fold-to-raise 10/10 yields challenger call threshold **0.37** (0.45 - 0.08); the real
  2-hand capture writes `iter-1-signatures.json` and deletes its scratch hands.
- Pilot (seed 1099511627776, 6 iterations, 8x4 selection, 8x4 holdout): **deterministic 0/6**
  promotions (one holdout no-improvement, +9.06); **diagnosis 0/6** (one holdout Unsafe anchor
  regression, +45.94). Champion unchanged both arms; no v1 pointer.
- This is a pilot, **not** an equal-budget controlled comparison and not an anchor-3 result.

## Item 3a — diagnostics: partial

DIA-004 and DIA-005 repaired and verified; DIA-001/002/003 (decision-time counterfactual EV) and
DIA-006–010 remain open. D5 is Partial.

## Item 3b — LLM attribution: pre-registered, not run

- CAP-001: live DeepSeek is configured (`.secrets/deepseek.env`, names only); live path needs
  `THOLDEM_LIVE_MODEL=1`.
- `experiments/llm-attribution-preregistration.md`: 4 tasks x 4 proposals = 16 requests, five arms
  (LLM, Random, Grid, Greedy, NoSearch), gate-based outcome (>=2 verified, 0 unsafe, strictly better
  than each control).
- OQ-004 requests the 16-request budget. No model call, no LLM claim.

## Verification

- Full suite **584/584**, 0 failed; build 0 errors; HarnessSync --check exit 0.
- Anchor 3 remains the recorded null.

## Next

1. Add a diagnosis arm to the equal-budget search runner and run a pre-registered retry (anchor 3).
2. Approve and run the 16-request LLM protocol; verify each response through the gate.
