# ADR-IMPL-05 — Agent runtime scope

- **Status:** Proposed (G1 decision required)
- **Date:** 2026-11-02
- **Owner:** product owner + agent/LLM engineer
- **Reversal cost:** High

## Context

WIP has no generic agent loop; Bones owns a domain-specific loop. The plan assumes general autonomy is a product goal (A-02). If G1 says no, Phase 3 is dropped and WIP is positioned as a governed coding/domain runtime.

## Decision (if G1 approves)

- Add a model-agnostic `IChatModel` contract (messages, tools, tool calls, streaming chunks).
- `AgentExecutor` owns turns, tool calls, observations, memory, budgets, and termination.
- `ToolInvoker` is permission-aware and timeout-bounded; `AgentMemory` is session-scoped and artifact-backed by default.
- Domain agents supply prompts and tool catalogs; they do not implement the loop.
- 1.0 scope: loop, tools, memory, budgets; streaming only if G1 confirms. RAG and multi-agent orchestration are post-1.0.

## Consequences

- Positive: one reusable loop; Bones becomes a consumer; provider abstraction is possible.
- Negative: new abstraction surface and cost/latency controls; risk of scope creep into a workflow engine.

## Reversal triggers

- G1 descopes general autonomy or streaming.
- DeepSeek changes the production provider contract.
- The loop cannot enforce budgets/termination deterministically.

## Open questions

- Streaming in 1.0? Owner: product owner (G1).
- Memory backend and retention? Owner: agent engineer + privacy reviewer.
