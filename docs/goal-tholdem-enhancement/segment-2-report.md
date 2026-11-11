# Segment report — items 1, 2 and 3

**Request:** `/stretch-goal-harness go for 1 2 and 3` (the three follow-ups in the final report).
**Status:** item 1 delivered; item 2 delivered as a proposer adapter, not a retry; item 3a delivered;
item 3b configured but not exercised. Goal active.

## Item 1 — four-seat training/promotion path: delivered

- `FourSeatTraining` evaluates a challenger on a protected selection block, spends a disjoint
  holdout block plus an anchor composition, and promotes only on `VerifiedImprovement`; the champion
  pointer lives under `four-seat/`, never the v1 pointer.
- `train4` CLI exposes it end-to-end.
- Evidence: 4/4 loop tests, 1/1 CLI test, an observed real CLI trace, byte-identical reruns, no v1
  pointer; full suite 575/575 at that point.

## Item 2 — diagnosis-driven proposer: adapter delivered, retry not run

- `DiagnosisDrivenProposer` turns a measured `SessionSignatures` into a genome edit through the
  existing refinement policy: fold-to-raise 10/10 moves the call threshold 0.45 -> 0.37 and nothing
  else; empty/below-floor signatures fall back to the deterministic proposer; repeats are equal.
- **Not delivered:** the four-seat loop does not yet capture hand records to feed the adapter, so no
  new anchor-3 retry was run. The adapter is the missing plumbing piece, not the retry.

## Item 3a — D5 diagnostics: partial

- DIA-004: fold-to-raise follows the outstanding aggressive action; a fold after an intervening
  caller now counts. The existing fixture keeps sample 3, canonical bytes unchanged.
- DIA-005: `FailureSignal.Observed` separates zero observations from measured zero failures.
- Still open: DIA-001/002/003 (decision-time counterfactual EV), DIA-006-010.

## Item 3b — LLM attribution: configured, not exercised

- CAP-001: `.secrets/deepseek.env` contains `DEEPSEEK_API_KEY` and `DEEPSEEK_BASE_URL` (names only);
  the live path additionally requires `THOLDEM_LIVE_MODEL=1`; no key is in the process environment.
- No model call and no LLM claim: no budget or attribution protocol was supplied. RISK-006 records
  this.

## Verification

- Full suite: **582/582**, 0 failed; solution build 0 errors; `HarnessSync --check` exit 0.
- Anchor 3: still the recorded null from the equal-budget ablation. No learning claim.

## Next

Capture per-iteration hand records in `FourSeatTraining`, feed them to `DiagnosisDrivenProposer`, and
run the pre-registered diagnosis-vs-controls comparison; only then is an anchor-3 retry meaningful.
For 3b, define the attribution task set, model budget, and equal-budget controls before any call.
