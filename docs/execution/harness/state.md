# Harness State — Bones agents on the generic WIP agent runtime

- **Round**: 8 of 12 — live-model comparison and the production defect it exposed.
- **Goal**: unchanged (agents on `IChatModel` → `IAgentExecutor` → `RuntimeToolInvoker` → `IAgentMemory`).
- **Definition of done**: D1–D8 were met at `f1a26bb`; the requested **live** comparison then found a
  real production defect in the new path. The fix is committed as `39258c0`; its first CI run had
  5/6 jobs green with only the pre-existing viewer-stage flake in the offline gate, so the records
  commit triggers a retry (EVD-138). D8 will be re-confirmed on the retried run.
- **Stop criteria**: D1–D7 remain met; D8 is pending a green run that does not hit the viewer flake.

## Live model comparison (2026-11-07)

| | Old provider path | New runtime path (pre-fix) | New runtime path (post-fix) |
|---|---|---|---|
| Live result | OK both rounds | **HTTP 400 both rounds** | OK both rounds |
| API error | — | `Invalid 'tools[0].function.name': … '^[a-zA-Z0-9_-]+$'` | — |
| First-call prompt | — | byte-identical to old | byte-identical to old |
| Calls / tokens / time | 1 / ~1.8k / 5–20 s | — | 2–3 / 11.7k–13.5k / 63–68 s |
| Generated script | compiles | — | compiles |

Root cause: WIP capability ids are dotted (`tool.bones.strategy-compile`, `tool.repo.*`) but DeepSeek
function names allow only `[A-Za-z0-9_-]`. The fix lives in `DeepSeekChatModel`: sanitize outgoing
names deterministically and restore the canonical dotted name on incoming tool calls; the model-visible
observation is unchanged. This also repairs the shell's live `tool.repo.*` path.

Full report: `docs/execution/harness/round-7/live-comparison.md` (EVD-133..137). The credential was
read from the machine credential store for the run only and appears nowhere in the repo or logs.

Residual: the new path uses ~6–7× the tokens and ~3–12× the wall time because the live model calls
the compile tool before answering — recorded as RISK-010.

## Gate status

| Gate | Status | Detail |
|---|---|---|
| G1 Evidence | PASS | live comparison + fix evidence (EVD-133..137) |
| G2 Falsification | PASS | the live comparison falsified the “offline-green means production-ready” assumption; the defect is fixed and re-tested live |
| G3 Coverage | PASS | D1–D7 met; D8 pending the new CI run |
| G4 Reproduction | PASS | pre-fix 400 and post-fix success reproduced; offline suites Runtime 155/149/6, Bones 455/454/1 |
| G5 Honesty | PASS | the cost trade-off and the one-time credential use are disclosed; the CI flake is recorded as pre-existing, not claimed as the fix's result |

## Objective ledger (new rows)

| Round | Objective | Mode | Agents | New EVD | Falsified | Gates | Outcome |
|---|---|---|---|---|---|---|---|
| 7 | Final CI + records | Orchestrator | 0 | 2 | — | PASS | green at `f1a26bb` |
| 8 | Live model comparison (old vs new) | Orchestrator + live API | 0 | 5 | 1 production defect (HTTP 400) | G2 PASS | fixed in the adapter; live re-run green; new commit/CI pending |

## Next objective

Commit the adapter fix + live tests + report, push, and verify Tests CI (six jobs) and Modus CI at the
new HEAD. Then update the final records. No release claim.
