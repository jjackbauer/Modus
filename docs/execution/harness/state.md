# Harness State — Bones agents on the generic WIP agent runtime

- **Round**: 9 of 12 — adversarial live comparison and the enhanced final composition.
- **Goal**: unchanged (agents on `IChatModel` → `IAgentExecutor` → `RuntimeToolInvoker` → `IAgentMemory`).
- **Definition of done**: D1–D7 met; the live comparisons and both composition fixes are committed;
  D8 is pending the CI run for the enhancement commit.
- **Stop criteria**: D1–D7 met; D8 pending.

## Live comparisons

**Round 7 (provider migration):** the live run found the dotted-tool-name 400; the adapter
sanitize/restore fix made both paths complete (round-7 report).

**Round 8 (adversarial + enhanced composition):** the first adversarial matrix showed the new path
spending 10–20× the completion tokens and 60–133 s because it did not send `thinking: disabled`.
Two changes closed it:

1. `DeepSeekChatModel` accepts an explicit `DeepSeekThinkingMode`; the Bones host passes the
   migrated `Disabled`, matching the legacy provider.
2. The compile tool is advertised lazily: no tools on the first turn, compile tool only on retry
   turns (`AdvertiseToolsOnFirstTurn`, default false).

Post-enhancement live results (same prompts/model):

| Scenario | Old | New (enhanced) |
|---|---|---|
| normal r1 | failed after 2 calls, 23.3 s (uncompilable script) | OK, 1 call, 6.4 s, 2,138 tokens |
| normal r2 | OK, 1 call, 5.1 s, 1,790 tokens | OK, 3 calls, 12.4 s, 9,890 tokens (retry + tool after a compile failure) |
| prompt injection | OK, safe | OK, safe; 2 calls |
| long context | OK, 4,965 tokens | OK, 4,989 tokens |
| forced tool loop | OK, ignores the instruction | fails closed with `MaxStepsReached` after 3 calls |
| Enhance | Rejected, 2,223 tokens | Rejected, 6,415 tokens |

Full report: `docs/execution/harness/round-8/adversarial-comparison.md` (EVD-140..144).

## Gate status

| Gate | Status | Detail |
|---|---|---|
| G1 Evidence | PASS | live matrices, fix tests, suite counts |
| G2 Falsification | PASS | found and fixed the thinking-cost gap; the injection/loop cases are adversarial evidence, not assumptions |
| G3 Coverage | PASS | D1–D7 met; D8 pending the new CI run |
| G4 Reproduction | PASS | Runtime 156/150/6, Bones 457/454/3 waived, host 1/1; live numbers in the report |
| G5 Honesty | PASS | the retry cost and the stochastic one-run limits are disclosed; no release claim |

## Objective ledger (new rows)

| Round | Objective | Mode | Agents | New EVD | Falsified | Gates | Outcome |
|---|---|---|---|---|---|---|---|
| 7 | Live model comparison | Orchestrator + live API | 0 | 7 | dotted-tool 400 | G2 PASS | fixed, green |
| 8 | Adversarial comparison + enhance composition | Orchestrator + live API | 0 | 5 | thinking-mode cost gap | G2 PASS | lazy tools + thinking parity; live re-run |

## Next objective

Commit the enhancement + adversarial report, push, and verify Tests CI (six jobs) and Modus CI at the
new HEAD. Then update the final records. No release claim.
