# G1 gate packet — autonomy and streaming scope (2026-11-04)

**Gate:** G1 · **When:** before Phase 3 · **Owner:** product owner
**Status:** packet ready — **awaiting the human decision; not approved.**
**Plan:** `docs/WIP-Implementation-Plan.md` §5.1 (G1), §3.5 (Phase 3), §4.3 (can-compress list)

## 1. Decision requested

| ID | Question | Recommended |
|---|---|---|
| D1 | Is general autonomy a product goal, so Phase 3 (generic agent runtime) is in scope for WIP 1.0? | **Yes** |
| D2 | Is streaming in 1.0? | **No — descope to post-1.0** |
| D3 | Sample-agent scope if autonomy is in scope: read-only `RepoResearchAgent` (search, read file, git status, shell under policy) | **Read-only only for 1.0** |

The plan’s G1 pass criteria are a written decision and a sample-agent scope. This packet supplies the evidence and the options; it does not self-approve.

## 2. Verified current state (what 1.0 would build on)

| Fact | Evidence |
|---|---|
| No generic chat/tool contracts exist: no `IChatModel`, `ChatMessage`, `ToolCall`, or `StreamAsync`. The model seam is the typed capability `IModelProvider<TRequestPayload, TResultPayload>`. | `WIP/Wip.Abstractions/Capabilities/CapabilityContracts.cs:122`; no match for `IChatModel`/`StreamAsync` in `WIP/**` |
| The only production model adapter is DeepSeek chat-completions and it is non-streaming. | `WIP/Wip.Runtime/Runtime/DeepSeekModelProvider.cs:72` |
| Agents are domain-specific capability implementations, not a loop: `IAgent<TRequest,TResult>`, `ITool<TRequest,TResult>`, `Wip.Agent.Basic.PlanOnlyAgent`/`PatchAgent`. There is no turn loop, memory, budget, or tool-call protocol. | `WIP/Wip.Abstractions/Capabilities/CapabilityContracts.cs:28,34`; `WIP/Wip.Agent.Basic/*.cs` |
| Phase 3 prerequisites are not met: `WipRuntimeOrchestrator` still builds a nested `BuildServiceProvider` per run (WP-2.1 / G4), and no `IPermissionEvaluator` exists (WP-1.7 / G3). | `WIP/Wip.Runtime/Runtime/WipRuntimeOrchestrator.cs:250`; no match for `IPermissionEvaluator` in `WIP/**` |
| The sandbox gate is conditional: trusted-only, no untrusted model code, full G2 not approved. | `docs/execution/evidence/G2/decision-2026-11-03-windows-residual-accepted.md`; `docs/security/sandbox-threat-model.md` §9 |
| The plan already treats streaming as compressible and UX-only, and puts the generic loop on the critical path. | plan lines 636 (“Streaming is a UX feature, not a correctness feature”), 855 (“Can compress: Streaming (drop to post-1.0)”), 849 (critical path includes WP-3.3/WP-3.6) |

Observed during this run: with `ScriptHost:Mode=Worker`, the nested provider creates a second script host and therefore a second worker process for a workflow run (the same WP-2.1 defect). Phase 3 work would multiply that duplication, which is another reason G4/Phase 2 must land first.

## 3. Options

### Option A — full Phase 3, streaming in scope (29 ED)
WP-3.1..3.7 as written: chat/tool contracts, DeepSeek adapter, permission-aware tool invoker, agent loop, artifact memory, streaming SSE, `RunAgentAsync` + read-only `RepoResearchAgent`, safety tests.
- Pro: complete agent story and token streaming in 1.0.
- Con: +3 ED and an SSE surface to test; streaming has no correctness dependency and the plan explicitly allows dropping it.
- Prerequisites: G4 (Phase 2) and G3 (permissions) before WP-3.2/3.3 can be accepted.

### Option B — full loop, streaming descoped to post-1.0 (26 ED) — **recommended**
WP-3.1..3.4, 3.6, 3.7; `IChatModel` keeps a non-streaming `CompleteAsync` for 1.0 and streaming is added post-1.0 without breaking the contract (a later `StreamAsync` is additive).
- Pro: keeps the release-critical M3 milestone (loop + real tool + sample agent E2E) and the safety tests; removes 3 ED of schedule risk; no security trade-off.
- Con: no token-by-token output in 1.0; shell/status shows stage-level progress instead.
- Prerequisites unchanged (G4, G3).

### Option C — autonomy descoped; release as a governed coding/domain-agent runtime
Drop Phase 3 entirely. 1.0 = Phase 1 + Phase 2 + Phase 4/5/6; Bones keeps its domain agents; no `RunAgentAsync`, no `RepoResearchAgent`.
- Pro: smallest release surface and soonest G5; the permission model still gates capabilities.
- Con: the “one agent loop” target state is not met; a later agent runtime becomes post-1.0 work with its own gate.
- Consequence: update the plan’s target state and success measures (“Agent loop: None → reusable loop” becomes out of scope).

### Option D — autonomy in scope, read-only, no streaming (= B with D3 read-only)
Identical to B if D3 is read-only; listed to make the sample-agent scope explicit.

## 4. Residual risks (all options)

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| G2 is conditional (trusted-only); a tool-enabled agent expands the prompt-injection/exfiltration surface | Medium | High | Keep the sample read-only; WP-3.7 abuse tests (injection, exfiltration, tool-loop, oversized observations) before exposure; G3 audit-then-enforce; no untrusted model code |
| G3 permissions not approved; `IToolInvoker` cannot enforce least privilege | High | High | WP-3.2 depends on WP-1.7/G3; do not ship a tool invoker before G3 enforcement |
| Nested DI provider (WP-2.1) duplicates state/worker processes during agent runs | High if Phase 3 starts before G4 | Medium | Phase 2/G4 first; no Phase 3 acceptance before host-managed DI |
| Budget/cost runaway (loop multiplies model calls) | Medium | Medium | `AgentLoopOptions` max steps/tokens/timeout; Phase 5 cost metrics and SLOs |
| Streaming descope hurts UX or a demo | Medium | Low | Stage-level progress remains; additive `StreamAsync` post-1.0 |
| Scope expansion to multi-tenant/multi-agent orchestration | Low | High | Plan non-goals; the loop owns one agent, workflows own branching |

## 5. After the decision

- **A or B/D:** record the decision, update the plan’s scope/estimate, then execute Phase 2 (G4) → G3 → Phase 3. Under B, WP-3.5 is marked descoped; `IChatModel` ships non-streaming.
- **C:** update the plan’s scope, target state, and success measures; Phase 3 is removed; execute Phase 4/5/6 and release as a governed coding/domain-agent runtime.
- In every case, no Phase 3 implementation starts before G4 and G3, regardless of this gate.

## 6. Sign-off

| Field | Value |
|---|---|
| Decision (D1 / D2 / D3) | _pending_ |
| Product owner | _pending_ |
| Date | _pending_ |
| Notes / conditions | _pending_ |