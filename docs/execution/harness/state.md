# Harness State — Bones agents on the generic WIP agent runtime

- **Round**: 2 of 12 (round 1 complete and committed; WP2 is the current objective)
- **Goal**: The four Bones domain agents run on `IChatModel` → `IAgentExecutor` → `RuntimeToolInvoker` → `IAgentMemory`, with the private DeepSeek stack test-only, parity and fail-closed controls proven, the legacy workflow shim removed, and both CI workflows green.
- **Definition of done**: 1/8 met (D1 adapter; D2–D8 open)
- **Stop criteria**: D1–D8 unmet; final HEAD green in Tests CI (six jobs) and Modus CI outstanding.

## Gate status

| Gate | Status | Detail |
|---|---|---|
| G1 Evidence | PASS | every round-1 claim resolves to a file:line, TRX or command output (EVD-086..100) |
| G2 Falsification | PASS | two *starting-point* clauses falsified and corrected (VAL-018/VAL-020); zero WP1 delivery claims falsified |
| G3 Coverage | PARTIAL | D1 met, D2–D8 `None` |
| G4 Reproduction | PASS | focused 14/14 re-run by the orchestrator in an isolated clone; control 6 failures; full Bones suite 410/409/1 |
| G5 Honesty | PASS | the falsifier's only `NotCheckable` (offline CI re-query) was already answered online (EVD-086/087); the WSL worker-DLL caveat is recorded |

## Objective ledger

| Round | Objective | Mode | Agents | New EVD | Falsified | Gates | Outcome |
|---|---|---|---|---|---|---|---|
| 0 | Verify the supplied starting point before editing | Probe (orchestrator) | 0 | 9 | 1 nuance | PASS | starting point re-verified; HEAD red on the pre-existing viewer flake |
| 1 | WP1: Bones-side `IAgentExecutor` adapter + tests, one commit | Implementer + independent Falsifier | 2 | 5 | 2 starting-point clauses | PASS | adapter + 14 tests; control proven; full Bones 410/409/1 (waived meta only); committed |
| 2 | WP2: port Ponder (single-seat + co-learning) onto the adapter — prompts class, `ToolDefinition`s through `RuntimeToolInvoker`, `AgentLoopOptions`, `ArtifactAgentMemory`, parity + controls | Implementer + Falsifier | 2 | — | — | — | in progress |

## Environment facts

- Local Windows VSTest is blocked (`Win32Exception 5`); WSL SDK 10.0.103 is the local test path.
- Verification runs use a separate ext4 clone (`/home/ricardo/verify`) overlaid with the work-package files, so a concurrent session's Windows builds cannot lock `obj/bin`. Full-suite runs in the shared tree are only used when the clone lacks a prebuilt dependency (e.g. `Wip.Sandbox.Worker.dll`).
- `LANG=en_US.UTF-8` is required for culture-sensitive prompt assertions; the main-tree run with it had no culture failures.
- **Concurrent session**: `docs/goal-tholdem/` and `WIP/Wip.Tholdem*` appeared at 10:15 and shared files (`Wip.slnx`, architecture rules, waivers, `tools/run-wip-tests.ps1`) are being modified in the same tree. Stage explicit paths only; re-read `git status`/`git log` before every commit and push.

## Next objective

**WP2 — port `BonesPonderAgent` and its co-learning wrapper onto the adapter**: dedicated prompts class, compile tool as a `ToolDefinition` routed through `RuntimeToolInvoker`, budgets from `AgentLoopOptions`, memory through `ArtifactAgentMemory`, deterministic old-vs-new parity fixtures, and negative controls (dropped tool call / altered prompt make parity fail). One commit after orchestrator reproduction.
