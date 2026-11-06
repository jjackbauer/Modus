# Harness State — Bones agents on the generic WIP agent runtime

- **Round**: 5 of 12 (round 4 complete and committed; WP5 is the current objective)
- **Goal**: The four Bones domain agents run on `IChatModel` → `IAgentExecutor` → `RuntimeToolInvoker` → `IAgentMemory`, with the private DeepSeek stack test-only, parity and fail-closed controls proven, the legacy workflow shim removed, and both CI workflows green.
- **Definition of done**: 4/8 met (D1 adapter, D2 Ponder, D3 Enhance, D4 provider/composition; D5–D8 open)
- **Stop criteria**: D1–D8 unmet; final HEAD green in Tests CI (six jobs) and Modus CI outstanding.

## Gate status

| Gate | Status | Detail |
|---|---|---|
| G1 Evidence | PASS | round-4 claims resolve to file:line, TRX or command output (EVD-113..120) |
| G2 Falsification | PASS | the architecture rule fails on a real injected Agents→provider edge (EVD-115); zero delivery claims falsified |
| G3 Coverage | PARTIAL | D1–D4 met, D5–D8 `None` |
| G4 Reproduction | PASS | Bones 446/443/3 (waived), Runtime 150/144/6 (documented), provider 48/47/1 (waived), host composition 1/1, architecture 4/4 + control |
| G5 Honesty | PASS | the `thinking` parameter delta is recorded as RISK-008; the concurrent Tholdem hunks are excluded from the commit |

## Objective ledger

| Round | Objective | Mode | Agents | New EVD | Falsified | Gates | Outcome |
|---|---|---|---|---|---|---|---|
| 0 | Verify the supplied starting point | Probe (orchestrator) | 0 | 9 | 1 nuance | PASS | starting point re-verified |
| 1 | WP1: adapter + tests | Implementer + independent Falsifier | 2 | 5 | 2 starting-point clauses | PASS | `dad2a72`; CI green |
| 2 | WP2: port Ponder | Implementer + orchestrator falsification | 1 | 7 | 1 memory defect | PASS | `3e0087a`; CI green |
| 3 | WP3: port Enhance | Implementer + orchestrator falsification | 1 | 5 | 0 | PASS | `409b6f9`; CI green |
| 4 | WP4: retire the provider + composition + architecture rule | Implementer + orchestrator falsification | 1 | 8 | 0 (1 recorded delta) | PASS | production grep clean, rule proven with control, suites non-regressed |
| 5 | WP5: permissions, Audit mode, injection/hidden-data/loop safety, redaction | Implementer + Falsifier | 2 | — | — | — | in progress |

## Environment / process facts

- Local Windows VSTest is blocked; the isolated ext4 clone `/home/ricardo/verify` is used for
  graph/architecture checks and the main tree for project-scoped suites.
- The concurrent Tholdem session's uncommitted changes (waiver hunk, `Wip.slnx`, architecture rules,
  new projects) are excluded from this run's commits; the architecture files are staged from a clean
  reconstruction.
- `BonesHostOptions.AgentLoop` is the host-owned budget surface for both agents.

## Next objective

**WP5 — safety and permissions for the runtime agents:** prove a model tool call carries the
`CapabilityContext` and is denied without the right grant (deterministic `permission-denied`); add the
Audit-mode test (would-be denial allowed and recorded); verify prompt injection, hidden-data access
and unbounded loops fail closed; verify no secrets in prompts/logs/artifacts and that memory
redaction stays on. No production change is expected; any gap must be fixed with a failing test
first.
