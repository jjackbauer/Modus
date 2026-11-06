# Harness State — Bones agents on the generic WIP agent runtime

- **Round**: 7 of 12 (round 6 complete and committed; final CI verification/records)
- **Goal**: The four Bones domain agents run on `IChatModel` → `IAgentExecutor` → `RuntimeToolInvoker` → `IAgentMemory`, with the private DeepSeek stack test-only, parity and fail-closed controls proven, the legacy workflow shim removed, and both CI workflows green.
- **Definition of done**: 6/8 met (D1–D6; D7 full-suite counts and D8 CI green pending)
- **Stop criteria**: D1–D8 unmet until the final HEAD is green in Tests CI (six jobs) and Modus CI.

## Gate status

| Gate | Status | Detail |
|---|---|---|
| G1 Evidence | PASS | round-6 claims resolve to file:line, TRX or command output (EVD-126..130) |
| G2 Falsification | PASS | the legacy grep is empty and the viewer failure is reproduced pre-WP6 (control) |
| G3 Coverage | PARTIAL | D1–D6 met; D7/D8 pending final CI |
| G4 Reproduction | PASS | Bones 455/454/1 (waived), Runtime 153/147/6 (documented), Shell 34/34, Builder 34/34, architecture 4/4 |
| G5 Honesty | PASS | the pre-existing viewer failure and the flaky waived meta-tests are recorded, not hidden |

## Objective ledger (final rows)

| Round | Objective | Mode | Agents | New EVD | Falsified | Gates | Outcome |
|---|---|---|---|---|---|---|---|
| 0 | Verify the supplied starting point | Probe (orchestrator) | 0 | 9 | 1 nuance | PASS | starting point re-verified |
| 1 | WP1: adapter + tests | Implementer + independent Falsifier | 2 | 5 | 2 starting-point clauses | PASS | `dad2a72`; CI green |
| 2 | WP2: port Ponder | Implementer + orchestrator falsification | 1 | 7 | 1 memory defect | PASS | `3e0087a`; CI green |
| 3 | WP3: port Enhance | Implementer + orchestrator falsification | 1 | 5 | 0 | PASS | `409b6f9`; CI green |
| 4 | WP4: retire the provider + composition + rule | Implementer + orchestrator falsification | 1 | 8 | 0 (1 recorded delta) | PASS | `d7d9723`; CI green |
| 5 | WP5: permissions/safety/redaction | Implementer + orchestrator falsification | 1 | 5 | 1 tool-artifact leak (fixed) | PASS | `7158280`; CI green |
| 6 | WP6: retire the legacy workflow shim | Implementer + orchestrator falsification | 1 | 5 | 0 | PASS | grep 0 hits; all gate suites non-regressed |
| 7 | Final: commit, push, verify both CI workflows, close the evidence | Orchestrator | 0 | — | — | — | in progress |

## Next objective

**Final verification** — commit WP6, push, wait for Tests CI (six jobs) and Modus CI on the final
HEAD, record the run ids and every job conclusion, update `deliverables.md`, and only then mark D7/D8
met. No release claim; no publish/sign/deploy.
