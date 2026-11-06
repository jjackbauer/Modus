# Round 4 — Orchestrator verification of WP4 (provider replacement + composition)

- **Round**: 4 (goal WIP-IMPL-001 continuation, D4)
- **WP4 deliverable**: `docs/execution/harness/round-4/implementation-wp4.md`
- **Starting HEAD**: `409b6f9` (WP3)
- **Result**: PASS — the host no longer references the DeepSeek provider assembly, the two domain
  agents are composed over `IChatModel` + `AgentExecutor` + `RuntimeToolInvoker` +
  `ArtifactAgentMemory`, the new architecture rule passes on the real graph and fails on a real
  injected edge, and the suites show only pre-existing waived/platform failures.

## 1. Provider retirement

- `WIP/Wip.Bones.Host/Wip.Bones.Host.csproj` and `Wip.Bones.Host.Tests.csproj` no longer reference
  `Wip.Bones.ModelProviders.DeepSeek`.
- A production grep over `Wip.Bones.Host` + `Wip.Bones.Agents` for
  `Wip.Bones.ModelProviders.DeepSeek` / `DeepSeekBones*Provider` returns **zero** `.cs` hits; the only
  remaining csproj hit is `InternalsVisibleTo Include="Wip.Bones.ModelProviders.DeepSeek.Tests"` in
  `Wip.Bones.Agents.csproj` (a test-assembly visibility entry, not a dependency).
- The provider project remains buildable/testable for its own 48 tests (one pre-existing waived
  behavior-proof meta-test); it is now test-only in practice.
- The configuration types moved to `Wip.Bones.Agents.Configuration`; the play-turn response parser
  moved to `Wip.Bones.Agents.Play`; the provider project consumes the moved types.

## 2. Host composition (host-owned DI, explicit factories)

`BonesHostServiceCollectionExtensions.RegisterRuntimeAgents` removes the earlier agent registrations
and re-registers `BonesPonderAgent` / `BonesEnhanceStrategyAgent` with factories that compose:
`new AgentExecutor(IChatModel, new RuntimeToolInvoker(sp, BonesAgentToolDescriptors.Create()))`,
`new ArtifactAgentMemory(IArtifactStore)`, the host script host, compile-retry/promotion/aggressive
options, the game budget, the strategy library and the chat-model play-turn provider. No global
`IAgentExecutor`/`IToolInvoker` is registered, so the shell `agent` path keeps its generic tool set.

DeepSeek path: `AddDeepSeekChatModel(options.ToDeepSeekModelProviderOptions())`, then `IChatModel` is
replaced with `BonesStageChatModel(inner, configuredModel)` so Bones stage ids
(`bones-strategy-author`, `bones-strategy-enhance`, `bones-play-turn`) map to the configured DeepSeek
model. Stub path: a private `StubBonesChatModel` returns the previous canned authoring/enhancement
scripts and a legal play move.

**Behavioural delta (recorded, not hidden)**: the retired per-stage provider sent the DeepSeek
`thinking` parameter; the generic `IChatModel` request contract has no thinking field, so the host's
`Thinking` setting is still parsed/diagnosed but is **not applied** by `DeepSeekChatModel`. The old
default was `disabled`, which matches the new behaviour; a non-default setting is now ineffective and
is recorded as a known limitation (RISK-008).

## 3. Architecture rule

`bones-agents-no-legacy-provider` (`Wip.Bones.Agents` must not reach
`Wip.Bones.ModelProviders.DeepSeek`) was added; the synthetic-violation test now injects that edge and
asserts the rule fires.

Orchestrator reproduction in the clean clone at `409b6f9` with the WP4 rule/test files (43-project
graph, no concurrent Tholdem projects):

- positive: `Passed! - Failed: 0, Passed: 4, Total: 4` (1 s);
- control: adding a real `Wip.Bones.Agents -> Wip.Bones.ModelProviders.DeepSeek` `ProjectReference`
  makes the suite fail with exactly
  `Unwaived forbidden references: bones-agents-no-legacy-provider: Wip.Bones.Agents -> Wip.Bones.ModelProviders.DeepSeek`
  (`Failed: 1, Passed: 3`); the reference was restored.

## 4. Suites (orchestrator reproduction)

| Suite | Observed | Notes |
|---|---|---|
| `Wip.Bones.Tests` | **446 total, 443 passed, 3 failed** | the three failures are the pre-existing waived behavior-proof meta-tests (they randomly pass/fail; the implementer saw 446/446) |
| `Wip.Runtime.Tests` | **150 total, 144 passed, 6 failed** | exactly the documented 5 Windows-only `dotnet.cmd` tests + the waived cancellation test; includes the 3 new `AddDeepSeekChatModel` registration tests |
| `Wip.Bones.ModelProviders.DeepSeek.Tests` | **48 total, 47 passed, 1 failed** | the pre-existing waived provider compliance meta-test |
| `BonesHostRuntimeAgentCompositionTests` | **1/1 passed** | resolves both agents from the stub host, runs Ponder and Enhance, persists a Script strategy + memory artifact |
| `BonesLearningLoopReliabilityHostTests` + `BonesLearningLoopPromotionHostTests` | **13 total, 9 passed, 4 failed** | all 4 failures are already in the waiver register (same test names) |
| `Wip.Architecture.Tests` | **4/4** (clean clone) | plus the injected-edge control above |

Count reconciliation: Bones 446 = 396 declared baseline + 14 WP1 + 13 WP2 + 10 WP3 + 13 WP4; Runtime
150 = 146 declared + 1 WP2 memory + 3 WP4 registration. The waiver file's totals remain floors and
are not staged (the file still carries the concurrent session's uncommitted Tholdem hunk).

## 5. Scope / integrity

- Existing test *assertions* were preserved where the test harness changed: the host tests swapped
  `IModelProvider` fakes for `IChatModel` fakes and composed Enhance through the host factory, keeping
  the same assertions; provider tests changed only `using` lines.
- Browser/Playwright tests were not run locally (Chromium dependency); the full host suite is left to
  the Windows CI gate.
- No commit was made by the implementer.
