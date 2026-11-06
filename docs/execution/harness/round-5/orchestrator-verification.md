# Round 5 — Orchestrator verification of WP5 (permissions, safety, redaction)

- **Round**: 5 (goal WIP-IMPL-001 continuation, D5)
- **WP5 deliverable**: `docs/execution/harness/round-5/implementation-wp5.md`
- **Starting HEAD**: `d7d9723` (WP4)
- **Result**: PASS — 9 safety tests reproduced 9/9; the tool-call-artifact redaction gap was fixed
  red→green; full Bones 455/454/1 (only a waived meta-test fails) and full Runtime 152/146/6 (the
  documented platform/waiver set).

## 1. Safety tests reproduced (isolated clone at `d7d9723`)

`BonesAgentRuntimeSafetyTests` → **9/9 passed, 12 s** (`.tmp/wp5-trx/wp5-focused.trx`). Coverage:

- plugin compile tool without a grant → exact
  `permission-denied: Permission 'workspace.write' denied: no grant for plugin 'plugin.bones'`;
  the tool body is not executed (compile counter proves it) and the agent still persists its strategy;
- repeated denials → `ToolFailureLimitReached`, 2 steps, zero tool executions, no strategy artifact;
- Audit mode → the tool executes, the sink records exactly one `Allowed=true, Audited=true`
  decision, and the strategy is persisted;
- `CapabilityContext` propagation → the spy tool observes the agent's session id and worktree path;
- cross-session memory isolation on the real `WipArtifactStoreLocal` → session B's prompt/artifacts
  contain none of session A's memory;
- prompt injection as observation data → only the compile tool is advertised, the injected
  `tool.filesystem.write` call is rejected as `unknown-tool`, no exfiltration artifact;
- Ponder and Enhance always-call-tool models → `MaxStepsReached` at `MaxSteps = 2`, no artifacts;
- secret in memory → `Authorization: [REDACTED]` in the prompt/snapshot and no raw secret in any
  artifact.

## 2. Tool-call artifact redaction (fixed, red→green)

The implementer's own residual-gap review found that `RuntimeToolInvoker.TryRecordArtifactAsync`
persisted raw tool-call arguments and results. Fix: resolve `ISecretRedactor` from DI (falling back
to `SecretRedactor.Default`) and redact the serialized arguments and result before writing the
artifact; the returned `ToolResult` stays raw so the model observation is unchanged.

- New `[Theory]` in `RuntimeToolInvokerTests` (DI redactor + fallback redactor):
  before the fix the implementer observed `Failed: 2, Passed: 8`; after the fix the orchestrator ran
  the class in the isolated clone: **10/10 passed** (`.tmp/wp5-trx/wp5-invoker.trx`), and the full
  Runtime suite includes the two new cases (**152 total**, +2).
- The test asserts the raw secret is still visible in the returned observation and absent from the
  persisted artifact, which now contains `[REDACTED]`.

## 3. Suites (orchestrator reproduction, main tree)

| Suite | Observed | Notes |
|---|---|---|
| `Wip.Bones.Tests` | **455 total, 454 passed, 1 failed** | the single failure is the pre-existing waived `BehaviorProofCompliance_GivenScriptStrategiesRequirements...`; 455 = 446 WP4 + 9 WP5 |
| `Wip.Runtime.Tests` | **152 total, 146 passed, 6 failed** | the six are exactly the documented 5 Windows-only `dotnet.cmd` tests + the waived cancellation test; 152 = 150 WP4 + 2 new invoker theory cases |
| `RuntimeToolInvokerTests` | **10/10** (clone) | includes the 2 redaction cases |
| `BonesAgentRuntimeSafetyTests` | **9/9** (clone) | all security scenarios above |

## 4. Residual / design notes

- The production host composes `BonesAgentToolDescriptors.Create()` without a plugin origin, so the
  compile capability is a **built-in/trusted** tool and the permission guard allows it by design; the
  deny/audit posture is exercised with a plugin-origin descriptor, which is exactly how a third-party
  model tool would be registered. This is a design boundary, not a bypass: `PermissionGuard` is the
  single evaluation point and it runs before every tool body.
- The step budget is per executor invocation; the Ponder/Enhance outer compile-retry loop is bounded
  separately by `MaxCompileRetries`, so total model calls are bounded by
  `MaxCompileRetries × MaxSteps`.
- Audit decisions are reported to the caller-supplied sink only (no persistent audit artifact in this
  increment); the sink is the operator's hook.

## 5. Scope / integrity

- Production change is limited to `RuntimeToolInvoker` artifact redaction (small, test-first).
- No existing test assertion was weakened; the security suite is additive.
- Browser/Playwright tests were not run (unchanged).
