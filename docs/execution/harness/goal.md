# Goal — WIP-IMPL-001 (continuation): Bones domain agents on the generic WIP agent runtime

> This contract supersedes the completed G5 goal of the run of 2026-11-05, whose full record
> remains appended in `journal.md` (OBS-001..029, DEC-016/017, EVD-001..085) and `deliverables.md`
> (ART-001..011). Nothing in that record is rewritten. Round numbering restarts for this goal; the
> evidence/journal IDs continue.

**Outcome (one sentence):** The four Bones domain agents (Ponder, CoLearning Ponder, Enhance,
CoLearning Enhance) run on the generic WIP agent runtime — `IChatModel` → `IAgentExecutor` →
`RuntimeToolInvoker` → `IAgentMemory`/`ArtifactAgentMemory` — in production code, with the private
DeepSeek provider stack retired to test-only, behavior proven by old-vs-new parity fixtures and
failing controls, permissions and fail-closed behavior verified, the legacy Bones workflow shim
removed, and both CI workflows green at the final HEAD. No release claim and no publish/sign/deploy.

## Definition of done (falsifiable)

| # | Check | Command / observable |
|---|---|---|
| D1 | A Bones-side adapter implements the domain contract `IAgent<TRequest,TResult>` and delegates the model loop to `IAgentExecutor.ExecuteAsync` with a full `AgentExecutionRequest` (session, task, model id, system prompt, `ToolDefinition`s, `AgentLoopOptions`), mapping `AgentExecutionResult` to the domain result and non-success statuses to deterministic domain exceptions | focused `dotnet test` exit 0 for the adapter tests; a control that removes the executor delegation fails them |
| D2 | `BonesPonderAgent` and `BonesCoLearningPonderAgent` use the adapter: prompts in a dedicated prompts class, tools as `ToolDefinition`s invoked through `RuntimeToolInvoker`, budgets via `AgentLoopOptions`, memory via `IAgentMemory`/`ArtifactAgentMemory`; single-seat and co-learning behavior stays distinct | focused Ponder tests + parity fixtures exit 0; a control that drops tool calls or alters the prompt makes parity fail |
| D3 | `BonesEnhanceStrategyAgent` and `BonesCoLearningEnhanceAgent` get the same treatment; Observe/Play stay deterministic tools, not model agents | focused Enhance tests + parity fixtures exit 0; control fails as above |
| D4 | Production registers `AddDeepSeekChatModel` through host-owned DI; `Wip.Bones.ModelProviders.DeepSeek` has no production call site and is test-only, guarded by an architecture rule | `grep` for the provider in production source returns no call site; architecture test passes and its synthetic violation fails |
| D5 | Every model tool call carries the `CapabilityContext`/grants and is denied without them (`RuntimeToolInvoker` behavior); Audit mode is tested; prompt injection, hidden-data access and unbounded loops fail closed; no secrets in prompts/logs/artifacts and memory redaction stays on | focused safety tests exit 0; negative controls reproduce each failure |
| D6 | The legacy `IWorkflow<,>` implementation/registration and the stub dispatch `ExecuteAsync` are gone; the declarative plan is the only Bones workflow path | source read + full Bones suite green; registration/plan tests still assert the plan |
| D7 | Full Bones + Runtime + Shell suites green at the final HEAD with waiver totals that match real discovered counts (never raised to hide a failure) | offline gate output / CI `wip-offline-tests` green; `docs/plan/test-waivers.json` totals justified by TRX counters |
| D8 | Tests CI all six jobs green and Modus CI green at the committed HEAD | GitHub Actions API run ids and every job conclusion read by the orchestrator |

## Hard constraints

- Do not change poker/Bones game rules, viewer design, prompts semantics beyond what parity
  requires, or the sandbox/worker boundary.
- Preserve `no Worker → Bones`, `no ShellHost → Bones`, `no nested BuildServiceProvider`, host-owned
  DI, worktree-confined writes, no auto-merge, no secrets in logs/artifacts.
- Do not weaken or delete existing tests to reach green; no tolerance widening, no skipped tests, no
  waivers added. Existing 396 Bones tests must keep their assertions.
- One work package per commit; UTF-8 without BOM and the HarnessSync check before every commit.
- Local Windows VSTest is blocked (`Win32Exception 5`, reproduced); Linux runs (WSL/CI) are the
  test evidence. WSL currently has no .NET SDK and must be provisioned for local verification.
- No release claim; no publish/sign/deploy.

## Stop criteria

- **Met** when D1–D8 hold, every parity and negative-control gate is reproduced by the orchestrator,
  and the final committed HEAD is green in both CI workflows.
- **Round cap:** 12 rounds for this continuation. If the cap is reached, leave the goal active and
  write a handoff packet.
