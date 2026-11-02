---
kind: rule
mode: agent
description: Unit tests passing does not prove integration works. When crossing subsystem boundaries (DI, host, workflow, ASP.NET), runtime verification against the live API is mandatory.
---

# Rule: Runtime-Verify Cross-Boundary Changes

## The Problem

Unit tests can pass (46/46 ✅) while the runtime integration is completely broken. This happens when:

- The code you modified is not on the actual execution path (dead code)
- DI resolution produces a different instance than expected
- The framework bypasses your factory registration
- An optional parameter silently defaults to null and your code never executes

## Mandatory Runtime Verification

When a change crosses any of these boundaries, you MUST verify at runtime with the live API:

| Boundary | Verification |
|----------|-------------|
| DI registration | Confirm the registered type is actually resolved and used |
| Host startup | Check startup logs for your code path |
| ASP.NET middleware/routes | Hit the endpoint and check the response |
| Workflow/agent pipeline | Poll the status API across multiple iterations |
| File I/O in host context | Check the file was actually written |
| External API calls | Verify the real API responds (not mocked) |

## Minimum Verification Steps

1. **Build succeeds** — necessary but not sufficient
2. **Unit tests pass** — necessary but not sufficient
3. **Start the app** with real configuration (real API keys if needed)
4. **Poll the status API** repeatedly over multiple iterations
5. **Assert the actual metric changes** — if `poolSize` should grow from 4 to 40, prove it grows

## Failure Pattern (DO NOT DO)

```markdown
✅ Build: 0 errors
✅ Tests: 46 passed
✅ "The fix is in place"
--- reality: poolSize = 4, unchanged ---
```

## Success Pattern

```markdown
✅ Build: 0 errors
✅ Tests: 46 passed
📊 poll1: poolSize=7, sessions=1
📊 poll2: poolSize=8, sessions=2
📊 poll3: poolSize=11, sessions=3
✅ "Pool grows with sessions"
```
