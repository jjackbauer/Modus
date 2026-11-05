# G5 release-readiness — **not requested; no release claim**

**Date:** 2026-11-05
**Branch / HEAD:** `WIP` @ the commit carrying this packet.
**Status:** **WIP 1.0 is not release-ready.** G5 (the human release decision) has not been requested
and no release is claimed. This packet records what is done, what passed verification, and the
concrete blockers that remain.

## Ready and verified

| Area | State |
|---|---|
| Phase 0 (baseline, decisions, G0, ADR) | done |
| Phase 1 isolation (WP-1.1..1.6) | worker process, resource limits, hostile tests, feature flag; G2 conditional/trusted-only |
| WP-1.7 permissions + G3 | catalog, evaluator Enforce/Audit, enforcement at dispatcher/tool gateway/plugin load/model invocation, grants, manifest surfacing; G3 accepted |
| Phase 2 runtime seams (WP-2.1..2.7) + G4 | host-managed DI, no static pool, declarative plans + executor, explicit maps (registry/heuristics deleted), Bones definition, shell decoupled from Bones, collectible plugin ALC with a conflicting-version proof; G4 accepted |
| Phase 3 agent runtime core | chat/tool contracts, permission-aware tool invoker (timeouts/truncation/errors/artifacts), executor loop with budgets/cancellation, artifact memory (cross-turn E2E), streaming execution (delta/tool events), write-capable worktree-confined repo tools, `RunAgentAsync` + `AgentExecutorTool`, worktree-escape/delete/permission/loop safety cases |
| Gates | 16-project offline gate green (last full run 1,112/1,089/23 waived, zero unwaived); architecture 4/4 over 43 projects with an empty waiver file; security jobs green; Tests CI green on `4509e04`; Modus CI hard gates green |

## Blockers to a WIP 1.0 release (G5)

1. **Phase 3 completion**
   - DeepSeek `IChatModel` adapter (non-streaming + SSE) with mock tests; the streaming contract and executor path exist but no vendor adapter is wired.
   - Shell/status progress rendering of stream events.
   - A **real-model** write-capable sample agent E2E (the current E2E uses a scripted model over the real tools/invoker/executor) plus an answer-a-repository-question demonstration.
   - Remaining WP-3.7 safety fixtures: prompt injection via tool output, self-approval/merge attempt, exfiltration via shell/search, and Agent/Security job wiring.
2. **Phases 4–6**
   - Phase 4 CI truth: reconcile the legacy Modus full-suite meta-tests (`BLK-MODUS-LEGACY-SUITE`) and make that suite blocking; docs/runbooks/operator guide.
   - Phase 5 security review (WP-5.5) of sandbox, permissions, plugin ALC, prompt/artifact handling, approval/merge boundary; privacy/retention (WP-5.2).
   - Phase 6 release engineering: versioning/signing, packaging, upgrade/compatibility notes, post-release verification plan.
3. **G2 full re-review** is scheduled for 2026-12-03 (currently conditional/trusted-only); Windows OS containment (C1) remains unclaimed (AppContainer helper error 5).
4. **Human gates**: G5 is the human release decision. G4/G3 were accepted as internal engineering gates; G2 remains conditional.

## Recommendation

Do **not** release. Continue Phase 3 (adapter, real-model sample, safety) and Phases 4–6, complete the G2 re-review, then bring G5 a packet with the golden-path demos and the security review. No release claim is made here.