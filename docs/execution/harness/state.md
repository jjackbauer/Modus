# Harness State — Bones agents on the generic WIP agent runtime

- **Round**: 4 of 12 (round 3 complete and committed; WP4 is the current objective)
- **Goal**: The four Bones domain agents run on `IChatModel` → `IAgentExecutor` → `RuntimeToolInvoker` → `IAgentMemory`, with the private DeepSeek stack test-only, parity and fail-closed controls proven, the legacy workflow shim removed, and both CI workflows green.
- **Definition of done**: 3/8 met (D1 adapter, D2 Ponder, D3 Enhance; D4–D8 open)
- **Stop criteria**: D1–D8 unmet; final HEAD green in Tests CI (six jobs) and Modus CI outstanding.

## Gate status

| Gate | Status | Detail |
|---|---|---|
| G1 Evidence | PASS | every round-3 claim resolves to a file:line, TRX or command output (EVD-108..112) |
| G2 Falsification | PASS | both Enhance parity controls fail as required; zero delivery claims falsified |
| G3 Coverage | PARTIAL | D1–D3 met, D4–D8 `None` |
| G4 Reproduction | PASS | focused 10/10, full Bones 433/430/3 (all waived), controls reproduced in the isolated clone; WP3 touched no Runtime code |
| G5 Honesty | PASS | the implementer's shell-exit caveat and the 396-floor/433-real count are recorded |

## Objective ledger

| Round | Objective | Mode | Agents | New EVD | Falsified | Gates | Outcome |
|---|---|---|---|---|---|---|---|
| 0 | Verify the supplied starting point before editing | Probe (orchestrator) | 0 | 9 | 1 nuance | PASS | starting point re-verified |
| 1 | WP1: `IAgentExecutor` adapter + tests | Implementer + independent Falsifier | 2 | 5 | 2 starting-point clauses | PASS | `dad2a72`; CI green |
| 2 | WP2: port Ponder | Implementer + orchestrator falsification | 1 | 7 | 1 memory defect | PASS | `3e0087a`; CI green |
| 3 | WP3: port Enhance | Implementer + orchestrator falsification | 1 | 5 | 0 | PASS | focused 10/10, full 433/430/3; controls proven |
| 4 | WP4: replace the provider, host composition, architecture rule | Implementer + Falsifier | 2 | — | — | — | in progress |

## Environment / process facts

- Local Windows VSTest is blocked; the isolated ext4 clone at `/home/ricardo/verify` (current HEAD +
  overlaid work-package files) is the local verification environment.
- **WP4 composition decision (DEC-021)**: the host will compose `BonesPonderAgent` and
  `BonesEnhanceStrategyAgent` with explicit factories over `IChatModel` + a Bones
  `AgentExecutor`/`RuntimeToolInvoker` + `ArtifactAgentMemory`, instead of registering a global
  `IAgentExecutor`/`IToolInvoker` (the shell agent path must keep the generic tool set). This also
  sidesteps the DI ambiguity in RISK-007.
- **Concurrent session**: Tholdem work remains uncommitted in the same tree (waiver hunk, `Wip.slnx`,
  architecture rules). Stage explicit paths only.

## Next objective

**WP4 — move the production stack off `Wip.Bones.ModelProviders.DeepSeek`:** relocate the DeepSeek
config types to `Wip.Bones.Agents.Configuration`, register `AddDeepSeekChatModel` in the host with the
per-stage model-id decorator, register a chat-model play-turn provider, remove the DeepSeek project
reference and the three `IModelProvider` registrations from the host, compose both agents explicitly,
add the `bones-agents-no-legacy-provider` architecture rule plus its synthetic violation, and run the
host/Bones suites before committing.
