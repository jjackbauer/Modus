---
agent: agent
description: When multiple test classes mutate a static singleton like StrategyPool.Instance, xUnit parallel execution causes non-deterministic interference. Use [Collection] to serialize access.
---

#file:harness/rules/isolate-static-state-in-parallel-tests.md
