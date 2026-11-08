# Harness State — Bones agents on the generic WIP agent runtime

- **Round**: 10 of 12 — the pre-registered live experiment.
- **Goal**: unchanged (agents on `IChatModel` → `IAgentExecutor` → `RuntimeToolInvoker` → `IAgentMemory`).
- **Definition of done**: D1–D8 met at the enhancement HEAD `9314113` (CI green). The experiment did
  not change the code; it corrected the cost claim and confirmed the lazy-tool decision.
- **Stop criteria**: met for the migration; the experiment is complete and reported.

## Experiment result (round 10)

Protocol fixed before the run; **176/176 runs, 0 infrastructure errors, 0 exclusions**.

- **Success**: normal 21/24 in every arm (87.5%, Fisher p=1.0; risk difference 0, 95% CI
  −20.8..+16.7 pp). No measured success benefit or harm.
- **Cost**: old vs new-lazy median tokens 2,096 vs 5,339 (**2.55×**; p=0.108); the pre-registered
  1.5× non-inferiority **fails**. Median latency 6.4 s vs 7.9 s (1.24×; CI upper 1.58×, not
  established). On successful runs only, 1,938 vs 2,312 median tokens (1.19×).
- **Ablation**: lazy vs eager median tokens 5,339 vs 9,138 (1.71×, p=6.3e-6); latency 7.9 vs 13.8 s
  (1.75×, p=5.9e-8); success equal. Lazy is a real win over the naive composition.
- **Safety**: forced tool loop old 8/8 success vs new 0/8, all **8/8 `MaxStepsReached`**; injection
  old 15/16 vs new 14/16 (p=1.0), **0 non-compile tool calls**; long-context 16/16 both.
- **Enhance**: 11/12 vs 10/12 (p=1.0), all `Rejected`; median tokens 2,610 vs 4,026.

Report: `docs/execution/harness/round-9/experiment-report.md`; raw: `results.jsonl`, `analysis.json`,
`analysis.txt`; protocol: `experiment-protocol.md`.

## Decision

Keep lazy tools (causally better than eager) and `thinking: disabled`. **Do not claim cost parity**:
the new path is functionally equivalent and safer, but more expensive on live-validation retries.
The next cost experiment, if needed, is a pre-registered no-tool-retry arm vs the current retry; do
not change production behavior on this observation alone.

## Gate status

| Gate | Status | Detail |
|---|---|---|
| G1 Evidence | PASS | pre-registered protocol, raw data, analysis, report |
| G2 Falsification | PASS | the experiment falsified the cost-parity claim; the report states it |
| G3 Coverage | PASS | D1–D7 met; D8 green at `9314113` |
| G4 Reproduction | PASS | 176/176, 0 infra; analysis selftest + byte-identical regeneration |
| G5 Honesty | PASS | the failed non-inferiority, the retry cost and the wide success CIs are reported; no release claim |

## Next objective

Commit the experiment artifacts and the harness records, push, and verify the docs HEAD CI (the code
HEAD `9314113` is already green). No release claim.
