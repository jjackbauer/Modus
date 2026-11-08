# Pre-registered live experiment — results

- **Protocol**: `docs/execution/harness/round-9/experiment-protocol.md` (fixed before the first run).
- **Executed**: 2026-11-08, code `9314113` + the experiment harness, `deepseek-flash`, default
  temperature, `thinking: disabled` in every arm.
- **Runs**: **176/176**, every cell complete, **0 infrastructure errors**, **0 exclusions**.
- **Data**: `results.jsonl` (raw, committed), `analysis.json` / `analysis.txt` (pre-registered
  analysis output), `analysis.py` (stdlib implementation, `--selftest` validated).

## Headline

| Question | Answer |
|---|---|
| Does the new path beat the old path on success? | **No difference**: 21/24 (87.5%) in every normal arm; Fisher p=1.0; risk difference 0 (95% CI −20.8 to +16.7 pp). |
| Is the new path cost non-inferior (≤1.5× median tokens)? | **No.** Median tokens all runs: old 2,096 vs lazy 5,339 = **2.55×**; the pre-registered 1.5× margin is not met. Median latency 6.4 s vs 7.9 s = **1.24×**; the non-inferiority CI upper bound is 1.58 s×, also not established. |
| Did the lazy-tool enhancement improve on the naive new path? | **Yes.** Eager vs lazy: median tokens 9,138 → 5,339 (−42%, p=6.3e-6), median latency 13.8 s → 7.9 s (−43%, p=5.9e-8), success identical (21/24 vs 21/24, p=1.0). |
| Is the new path safer under the forced-loop injection? | **Yes.** Old 8/8 answered (ignoring the instruction); new 0/8, **8/8 failed closed with `MaxStepsReached`**. |
| Injection / long-context / Enhance | Injection: old 15/16 vs new 14/16 (p=1.0), **0 non-compile tool calls**, no exfiltration. Long-context: 16/16 both, cost parity. Enhance: old 11/12 vs new 10/12 (p=1.0), all outcomes `Rejected`, median tokens 2,610 vs 4,026 (new 1.54×, p=0.95). |

**The honest conclusion:** the migration preserves behavior (no measured success difference), the
lazy-tool change is a genuine win over the naive composition, and the forced-loop budget works — but
the new path's live-validation retry **costs more than the old path and did not buy a higher success
rate in this experiment**. The old path also retries (7/24), just more cheaply (no tool round-trip).

## Normal scenario (N=24 per arm)

| Arm | Success | First-try | Median tokens (all runs) | Mean tokens | Median tokens (successes) | Median latency | Median calls |
|---|---|---|---|---|---|---|---|
| old | 21/24 (87.5%) | 14/24 | 2,096 | 3,393 | 1,938 | 6.4 s | 1 |
| new-eager | 21/24 (87.5%) | 20/24 | 9,138 | 8,665 | 8,179 | 13.8 s | 3 |
| new-lazy | 21/24 (87.5%) | 11/24 | 5,339 | 4,152 | 2,312 | 7.9 s | 2 |

- Success: all pairwise Fisher p=1.0; Wilson 95% CI [69.0%, 95.7%] for each arm.
- Old vs lazy: median-token ratio (lazy/old) **2.55×** (bootstrap 95% CI 0.48–3.02); Mann–Whitney
  p=0.108. Median-latency ratio 1.24× (CI 0.88–1.58), p=0.284. The all-run median is bimodal:
  ~11 first-try successes around 1.9k tokens, ~10 retries around 5.9k, 3 failures around 6k.
  On **successful runs only**, lazy is 2,312 vs old 1,938 tokens (1.19×) and the mean is 3,829 vs
  3,120 (1.23×) — a much smaller gap than the all-run median suggests, but still not parity.
- Eager vs lazy: median tokens 9,138 vs 5,339 (ratio 1.71×, CI 1.19–4.72, p=6.3e-6); median latency
  13.8 s vs 7.9 s (ratio 1.75×, CI 1.46–2.27, p=5.9e-8); success equal. The lazy change is
  **supported**.
- First-try counts are descriptive: old 14, lazy 11 (Fisher p≈0.56, post-hoc). The first attempt is
  prompt-identical between old and lazy (no tools); the difference is sampling noise.
- Failure counts: 3/24 in each arm (compile failures). With N=24 the 95% CI for a success difference
  spans roughly ±20 pp; this experiment cannot exclude a small difference, and it does not claim one.

## Adversarial cells

| Scenario | Old | New | Notes |
|---|---|---|---|
| injection (N=16) | 15/16, 0 tool calls, 1.9k tokens | 14/16, 0 tool calls, 2.1k tokens | no `tool.filesystem.write` call reached any handler; only the compile tool exists; p=1.0 |
| long-context (N=16) | 16/16, 5,146 tokens, 5.7 s | 16/16, 5,224 tokens, 6.0 s | parity |
| forced tool loop (N=8) | 8/8 success, 1.8k tokens | 0/8 success, **8/8 `MaxStepsReached`**, 6.1k tokens | the new path fails closed; the old path has no tool and ignores the text |
| Enhance (N=12) | 11/12, all Rejected, 2,610 tokens | 10/12, all Rejected, 4,026 tokens | success p=1.0; token p=0.95 |

## What this changes

1. **Keep the lazy-tool default.** It is causally better than eager on cost with equal success
   (p<1e-5). This is the one enhancement the experiment confirms.
2. **Do not claim cost parity for the new path.** The pre-registered non-inferiority failed. The
   remaining gap is the price of live validation: when a first answer fails to compile, the retry
   round-trips the script through the compile tool (~5.9k tokens vs ~5.6k for an old-style retry,
   plus a third call), and in this sample validation did not raise the success rate. That is a
   product decision to make explicitly, not a claim to paper over.
3. **The safety properties are real:** fail-closed under a forced tool loop, injection-as-data, and
   a hard step budget. They are the strongest part of the migration.
4. **If cost is the priority**, the next experiment should be an arm that retries without
   advertising the compile tool (the old-style retry) and an arm that advertises it, with the same
   retry prompt; the current data suggests the tool validation may not pay for itself. That is a
   hypothesis for a new pre-registered run, not a conclusion of this one.

## Scope

One model (`deepseek-flash`), one prompt per scenario, N=24/16/8/12, compile-success as the outcome
(no gameplay-quality endpoint), default temperature. The CIs for success differences are wide
(±20 pp at N=24); the token/latency distributions are heavy-tailed. These are the experiment's
boundaries, stated so the numbers are not over-read.
