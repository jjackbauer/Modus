# Round 2 — DEV control analysis

> Dev-only diagnostic, written before the holdout. It does not change the pre-registered holdout claim. The control arm was not pre-registered; it exists to test whether the refinement arm's divergence is explained by step size alone.

## Arms

| Arm | Proposer | Purpose |
|---|---|---|
| `baseline` | `DeterministicStrategyProposer` with the shipped `MutationStep = 0.02` | The system as it exists |
| `refinement` | `SignatureDrivenRefinementPolicy` with 0.08 / 0.06 edits | The new arm |
| `legacy-large` | `DeterministicStrategyProposer` with `MutationStep = 0.08` | Step-size control: same step magnitude, no signatures |

All arms: six DEV seeds, five iterations, 120 training hands, 24 evaluation seeds, 10 evaluation hands. Raw rows: `results.jsonl` (baseline + refinement) and `results-control.jsonl` (legacy-large).

## Primary dev result (pre-registered comparison)

| Metric | Baseline | Refinement |
|---|---:|---:|
| Mean decision divergence | 1.81% | **9.83%** |
| Median per-seed divergence | 0.83–1.67% | 7.5–13.3% |
| Zero-effect evaluations | 7/30 | **0/30** |
| Verified promotions | 0/30 | 0/30 |

- Pooled ratio: **5.45×**.
- Median of the six per-seed ratios: **9.0×**.
- Paired bootstrap 95% CI: **[6.75, 11.5]** (10,000 resamples, seed 20261108).
- Per-seed ratios: 5.5, 8, 9, 9, 10, 13 — every seed points the same way.

## Control result

| Metric | Baseline | Refinement | Legacy-large |
|---|---:|---:|---:|
| Mean decision divergence | 1.81% | 9.83% | 7.83% |
| Zero-effect evaluations | 7/30 | 0/30 | 7/30 |
| Verified promotions | 0/30 | 0/30 | 0/30 |

- Legacy-large vs baseline: **4.34×** pooled; most of the refinement gain is step size.
- Refinement vs legacy-large: pooled **1.26×**; median per-seed ratio **1.09×**; paired bootstrap 95% CI **[0.80, 1.94]** — includes 1.0, so the incremental divergence over a large-step legacy proposer is **not established** on dev.
- Refinement vs legacy-large zero-effect: 0/30 vs 7/30; Fisher exact two-sided **p ≈ 0.0105** (hypergeometric, 30/30 split, 7 total). The zero-effect reduction is the signature policy's clear incremental effect.
- The control's per-seed ratios are mixed (0.69, 0.91, 1.0, 1.18, 1.22, 2.67), which is why the interval is wide.

## Honest interpretation

1. **Against the shipped baseline, the refinement arm is a real improvement on the measured axis.** It changes roughly five times as many decisions and eliminates the zero-effect evaluations observed in the legacy arm. The effect is consistent across all six dev seeds.
2. **Most of the divergence gain is step size.** A legacy proposer with the same 0.08 step reaches 7.83% vs 1.81%, so "the loop explores more" is mostly "the step is larger". The signature layer adds a non-significant 1.26× on top.
3. **The signature layer's distinct, statistically visible benefit is the elimination of zero-effect evaluations.** The large-step legacy still produced 7/30 zero-effect rows; the signature arm produced 0/30. Fisher p ≈ 0.0105.
4. **On the exact iterations where the shipped baseline was stuck, only the signature policy moved behaviour.** The 7 baseline zero-effect iterations are the same 7 iterations for the large-step control (the legacy 0.08 proposal still changed no decision there). The refinement policy changed decisions on **7/7** of them (4–24 hands of 120); the control changed **0/7**. This is a post-hoc paired subset on dev, but it is the cleanest mechanistic evidence that signature selection, not step size, rescues the stuck cases.
5. **No promotion, no poker-strength claim.** All three arms promoted 0/30. The experiment measures behavioural exploration, not skill.

### Paired zero-effect subset (dev, post-hoc)

| Seed | Iteration | Baseline divergence | Control divergence | Refinement divergence |
|---|---:|---:|---:|---:|
| 1099511629776 | 3 | 0 | 0 | 24 |
| 1099511629776 | 5 | 0 | 0 | 11 |
| 1099511630776 | 1 | 0 | 0 | 17 |
| 1099511631776 | 1 | 0 | 0 | 11 |
| 1099511631776 | 3 | 0 | 0 | 6 |
| 1099511632776 | 1 | 0 | 0 | 10 |
| 1099511632776 | 5 | 0 | 0 | 4 |

Refinement moved behaviour on 7/7; the large-step legacy moved behaviour on 0/7.

## Consequence for the holdout

- The pre-registered holdout compares `refinement` vs `baseline`; that claim is unchanged and will be run once on the six holdout seeds.
- The `legacy-large` control stays dev-only. It will be reported as the reason the final claim is worded as "more exploration and fewer zero-effect proposals", not "the signature direction alone makes better poker".
- No product-code change is made between dev and holdout. Changing the policy now would invalidate the pre-registered holdout.
