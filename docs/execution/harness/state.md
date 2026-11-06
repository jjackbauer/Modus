# Harness State — Bones agents on the generic WIP agent runtime

- **Round**: 3 of 12 (round 2 complete and committed; WP3 is the current objective)
- **Goal**: The four Bones domain agents run on `IChatModel` → `IAgentExecutor` → `RuntimeToolInvoker` → `IAgentMemory`, with the private DeepSeek stack test-only, parity and fail-closed controls proven, the legacy workflow shim removed, and both CI workflows green.
- **Definition of done**: 2/8 met (D1 adapter, D2 Ponder; D3–D8 open)
- **Stop criteria**: D1–D8 unmet; final HEAD green in Tests CI (six jobs) and Modus CI outstanding.

## Gate status

| Gate | Status | Detail |
|---|---|---|
| G1 Evidence | PASS | every round-2 claim resolves to a file:line, TRX or command output (EVD-101..107) |
| G2 Falsification | PASS | two parity controls fail as required (EVD-103); one pre-existing memory defect falsified and fixed (VAL-022); zero delivery claims falsified |
| G3 Coverage | PARTIAL | D1–D2 met, D3–D8 `None` |
| G4 Reproduction | PASS | focused 13/13, full Bones 423/420/3 (all waived), full Runtime 147/141/6 (documented set), controls reproduced in the isolated clone |
| G5 Honesty | PASS | the 423/147 real counts and the 396/146 waiver floors are both recorded; the DI ambiguity and the concurrent waiver hunk are recorded, not hidden |

## Objective ledger

| Round | Objective | Mode | Agents | New EVD | Falsified | Gates | Outcome |
|---|---|---|---|---|---|---|---|
| 0 | Verify the supplied starting point before editing | Probe (orchestrator) | 0 | 9 | 1 nuance | PASS | starting point re-verified; HEAD red on the pre-existing viewer flake |
| 1 | WP1: `IAgentExecutor` adapter + tests, one commit | Implementer + independent Falsifier | 2 | 5 | 2 starting-point clauses | PASS | committed `dad2a72`; CI green |
| 2 | WP2: port Ponder — prompts, tools, budgets, memory, parity + controls | Implementer + orchestrator falsification | 1 | 7 | 1 pre-existing memory defect | PASS | committed; full suites unchanged apart from the 27 new tests |
| 3 | WP3: port Enhance (single-seat + co-learning) — same pattern, parity + controls | Implementer + Falsifier | 2 | — | — | — | in progress |

## Environment / process facts

- Local Windows VSTest is blocked (`Win32Exception 5`); WSL SDK 10.0.103 is the local path. Verification
  runs use the isolated ext4 clone `/home/ricardo/verify` with the work-package files overlaid.
- `LANG=en_US.UTF-8` is required for culture-sensitive prompt assertions.
- **DI constraint (RISK-007)**: once both `IModelProvider` and `IAgentExecutor` are registered,
  `BonesPonderAgent`/`BonesEnhanceStrategyAgent` constructors are ambiguous. WP4 must retire the
  provider registrations in the same change that adds the executor.
- **Concurrent session**: Tholdem work is still uncommitted in the same tree (waiver hunk, `Wip.slnx`,
  architecture rules). Stage explicit paths only; never commit the waiver file while that hunk is
  uncommitted.

## Next objective

**WP3 — port `BonesEnhanceStrategyAgent` and its co-learning wrapper onto the adapter**: reuse
`BonesAgentTools`/`BonesAgentLoopOptions`/the prompts-mapping pattern, deterministic promotion seed
for parity, tool routing through `RuntimeToolInvoker`, memory through `ArtifactAgentMemory`, negative
controls (MaxSteps, tool-failure limit) and a parity sensitivity control; one commit after
orchestrator reproduction.
