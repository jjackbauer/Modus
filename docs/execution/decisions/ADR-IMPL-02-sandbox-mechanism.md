# ADR-IMPL-02 — Sandbox mechanism for model-authored code

- **Status:** Proposed (G2 decision required)
- **Date:** 2026-11-02
- **Owner:** platform/security engineer + tech lead
- **Reversal cost:** High

## Context

Bones compiles and runs model-authored C# in-process. The namespace allowlist is defense in depth, not a security boundary. The host must never load untrusted assemblies.

## Decision

- Run model-authored code in a separate **`Wip.Sandbox.Worker`** process.
- Protocol: versioned JSON lines over stdio (handshake, compile, chooseMove, ping, shutdown), correlation IDs, message limits, error taxonomy.
- The worker has no host DI, no inherited secrets, no ambient network by default, and no host assembly loading.
- Limits: Windows Job Object (memory/CPU/process count), Linux prlimit/cgroup; startup/handshake/compile/move timeouts; output caps; process-tree kill; restart after crash.
- `InProcess` execution is a test/trusted-only fallback behind `ScriptHost:Mode`, never the default.
- Residual risk is documented: a local developer tool cannot provide perfect OS sandboxing; the worker is the security boundary.

## Consequences

- Positive: a killable, resource-limited trust boundary; hostile-code tests become possible.
- Negative: serialization overhead and more moving parts; worker lifecycle must be tested on Windows and Linux.

## Reversal triggers

- Required OS limits are unavailable on a supported platform.
- The worker cannot meet the move-latency target.
- All strategies become trusted by product decision (then a documented trust boundary and kill switch replace the worker).

## Open questions

- Linux cgroup v2 availability on the target runners. Owner: platform engineer.
- Exact memory/CPU defaults and the residual-risk statement. Owner: security reviewer.
