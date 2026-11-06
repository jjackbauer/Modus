# Harness State — Bones agents on the generic WIP agent runtime

- **Round**: 6 of 12 (round 5 complete and committed; WP6 is the current objective)
- **Goal**: The four Bones domain agents run on `IChatModel` → `IAgentExecutor` → `RuntimeToolInvoker` → `IAgentMemory`, with the private DeepSeek stack test-only, parity and fail-closed controls proven, the legacy workflow shim removed, and both CI workflows green.
- **Definition of done**: 5/8 met (D1–D5; D6 legacy shim, D7 suites, D8 CI open)
- **Stop criteria**: D1–D8 unmet; final HEAD green in Tests CI (six jobs) and Modus CI outstanding.

## Gate status

| Gate | Status | Detail |
|---|---|---|
| G1 Evidence | PASS | round-5 claims resolve to file:line, TRX or command output (EVD-121..125) |
| G2 Falsification | PASS | the denied-tool and Audit-mode paths are exercised with real plugin-origin descriptors; the tool-artifact gap was found and fixed red→green |
| G3 Coverage | PARTIAL | D1–D5 met, D6–D8 `None` |
| G4 Reproduction | PASS | safety 9/9 (clone), invoker 10/10 (clone), Bones 455/454/1 (waived), Runtime 152/146/6 (documented) |
| G5 Honesty | PASS | the built-in-tool trust boundary is recorded as RISK-009, not claimed as a plugin grant |

## Objective ledger

| Round | Objective | Mode | Agents | New EVD | Falsified | Gates | Outcome |
|---|---|---|---|---|---|---|---|
| 0 | Verify the supplied starting point | Probe (orchestrator) | 0 | 9 | 1 nuance | PASS | starting point re-verified |
| 1 | WP1: adapter + tests | Implementer + independent Falsifier | 2 | 5 | 2 starting-point clauses | PASS | `dad2a72`; CI green |
| 2 | WP2: port Ponder | Implementer + orchestrator falsification | 1 | 7 | 1 memory defect | PASS | `3e0087a`; CI green |
| 3 | WP3: port Enhance | Implementer + orchestrator falsification | 1 | 5 | 0 | PASS | `409b6f9`; CI green |
| 4 | WP4: retire the provider + composition + rule | Implementer + orchestrator falsification | 1 | 8 | 0 (1 recorded delta) | PASS | `d7d9723`; CI green |
| 5 | WP5: permissions, Audit, injection/hidden-data/loops, redaction | Implementer + orchestrator falsification | 1 | 5 | 1 tool-artifact leak (fixed) | PASS | 9/9 safety, redaction red→green, suites non-regressed |
| 6 | WP6: retire the legacy workflow shim + full suites | Implementer + Falsifier | 2 | — | — | — | in progress |

## Environment / process facts

- The concurrent Tholdem session's changes remain uncommitted and are excluded from every commit.
- Counts at WP5: Bones 455, Runtime 152, provider 48, host composition 1; the waiver floors remain
  396/146 (TO-DO refresh is deferred to the final counts).

## Next objective

**WP6 — remove the legacy `IWorkflow<,>` Bones shim:** add `WipBuilder.AddDeclarativeWorkflow` (a
selection registration whose execution is the definition plan), make `BonesLearningWorkflow`
implement only `IWorkflowDefinition`, delete the stub `ExecuteAsync`, switch the Bones registration,
reconcile the tests that used the legacy registration, and run the full Bones + Runtime + Shell
suites before committing the final work package.
