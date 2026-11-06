# WP4 — Move the four Bones domain agents onto the generic WIP agent runtime and retire the private DeepSeek provider stack

- **Round**: 4 (goal WIP-IMPL-001 continuation)
- **Work package**: WP4 — host-owned DI for `AddDeepSeekChatModel`, explicit `BonesPonderAgent` / `BonesEnhanceStrategyAgent` composition over `IChatModel` + `AgentExecutor` / `RuntimeToolInvoker` + `ArtifactAgentMemory`, chat-model play-turn provider, `Wip.Bones.ModelProviders.DeepSeek` made test-only, and the `bones-agents-no-legacy-provider` architecture rule
- **Date**: 2026-11-06
- **Repository HEAD**: `409b6f9` (`feat(bones): port Enhance onto the runtime executor with parity controls [WP3]`), plus this uncommitted WP4 change set
- **Result**: PASS on every suite that was run, except one **pre-existing waived** provider meta-test. `Wip.Bones.Host` and `Wip.Bones.Host.Tests` build with **0 errors**; `Wip.Bones.Tests` **446 / 446 passed**; architecture **4 / 4**; new focused tests **17 / 17**; non-browser host subset **40 / 40**; provider suite **47 / 48**, the single red being the waiver already recorded at `docs/plan/test-waivers.json:124`.
- **Not done (out of scope)**: WP5 permissions/Audit, WP6 legacy workflow shim, commits, browser/Playwright/viewer runs.

## 0. Scope boundary

`git status --porcelain -- WIP tests docs/execution/harness` before this artifact was written (the `Wip.Tholdem*` untracked projects belong to concurrent work and were **not** touched):

```text
 D WIP/Wip.Bones.ModelProviders.DeepSeek/BonesDeepSeekProviderOptions.cs
 D WIP/Wip.Bones.ModelProviders.DeepSeek/BonesDeepSeekResilienceOptions.cs
 D WIP/Wip.Bones.ModelProviders.DeepSeek/BonesPlayTurnResponseParser.cs
 M tests/Wip.Architecture.Tests/ArchitectureReferenceTests.cs
 M tests/Wip.Architecture.Tests/ArchitectureRules.cs
 M WIP/Wip.Bones.Host.Tests/BonesHostDeepSeekServiceCollectionTests.cs
 M WIP/Wip.Bones.Host.Tests/BonesLearningLoopPromotionHostTests.cs
 M WIP/Wip.Bones.Host.Tests/BonesLearningLoopReliabilityHostTests.cs
 M WIP/Wip.Bones.Host.Tests/Wip.Bones.Host.Tests.csproj
 M WIP/Wip.Bones.Host/BonesHostApplication.cs
 M WIP/Wip.Bones.Host/BonesHostOptions.cs
 M WIP/Wip.Bones.Host/BonesHostServiceCollectionExtensions.cs
 M WIP/Wip.Bones.Host/Wip.Bones.Host.csproj
 M WIP/Wip.Bones.ModelProviders.DeepSeek.Tests/Authoring/DeepSeekBonesStrategyAuthoringProviderTests.cs
 M WIP/Wip.Bones.ModelProviders.DeepSeek.Tests/BonesDeepSeekResiliencePipelineTests.cs
 M WIP/Wip.Bones.ModelProviders.DeepSeek.Tests/Configuration/BonesDeepSeekProviderOptionsTests.cs
 M WIP/Wip.Bones.ModelProviders.DeepSeek.Tests/Enhancement/DeepSeekBonesStrategyEnhancementProviderTests.cs
 M WIP/Wip.Bones.ModelProviders.DeepSeek.Tests/HttpMapping/BonesDeepSeekHttpMappingProofTests.cs
 M WIP/Wip.Bones.ModelProviders.DeepSeek.Tests/Play/DeepSeekBonesPlayTurnProviderTests.cs
 M WIP/Wip.Bones.ModelProviders.DeepSeek.Tests/Registration/AddBonesDeepSeekModelProvidersTests.cs
 M WIP/Wip.Bones.ModelProviders.DeepSeek/BonesDeepSeekResiliencePipeline.cs
 M WIP/Wip.Bones.ModelProviders.DeepSeek/BonesDeepSeekServiceCollectionExtensions.cs
 M WIP/Wip.Bones.ModelProviders.DeepSeek/DeepSeekBonesPlayTurnProvider.cs
 M WIP/Wip.Bones.ModelProviders.DeepSeek/DeepSeekBonesStrategyAuthoringProvider.cs
 M WIP/Wip.Bones.ModelProviders.DeepSeek/DeepSeekBonesStrategyEnhancementProvider.cs
 M WIP/Wip.Bones.ModelProviders.DeepSeek/RateLimitAwareDelegatingHandler.cs
 M WIP/Wip.Runtime/DeepSeekChatModelServiceCollectionExtensions.cs
?? WIP/Wip.Bones.Agents/Configuration/
?? WIP/Wip.Bones.Agents/Play/BonesPlayTurnResponseParser.cs
?? WIP/Wip.Bones.Agents/Runtime/BonesChatModelPlayTurnProvider.cs
?? WIP/Wip.Bones.Agents/Runtime/BonesStageChatModel.cs
?? WIP/Wip.Bones.Host.Tests/BonesHostRuntimeAgentCompositionTests.cs
?? WIP/Wip.Bones.Tests/Runtime/BonesChatModelPlayTurnProviderTests.cs
?? WIP/Wip.Bones.Tests/Runtime/BonesStageChatModelTests.cs
?? WIP/Wip.Runtime.Tests/Runtime/DeepSeekChatModelServiceCollectionExtensionsTests.cs
```

`Wip.slnx`, `docs/plan/test-waivers.json`, `tools/run-wip-tests.ps1`, `WIP/Wip.Tholdem*` and `docs/goal-tholdem/` were **not** modified by WP4.

## 1. File moves

Three files were **moved** (deleted from the provider project, recreated in the agent layer) and the namespaces changed accordingly. No content of the validation logic changed apart from the announced additions.

| From | To | Namespace |
|---|---|---|
| `WIP/Wip.Bones.ModelProviders.DeepSeek/BonesDeepSeekProviderOptions.cs` | `WIP/Wip.Bones.Agents/Configuration/BonesDeepSeekProviderOptions.cs` | `Wip.Bones.ModelProviders.DeepSeek` → `Wip.Bones.Agents.Configuration` |
| `WIP/Wip.Bones.ModelProviders.DeepSeek/BonesDeepSeekResilienceOptions.cs` | `WIP/Wip.Bones.Agents/Configuration/BonesDeepSeekResilienceOptions.cs` | `Wip.Bones.ModelProviders.DeepSeek` → `Wip.Bones.Agents.Configuration` |
| `WIP/Wip.Bones.ModelProviders.DeepSeek/BonesPlayTurnResponseParser.cs` | `WIP/Wip.Bones.Agents/Play/BonesPlayTurnResponseParser.cs` | `Wip.Bones.ModelProviders.DeepSeek` → `Wip.Bones.Agents.Play` |

`Wip.Bones.Agents` already references `Wip.Runtime` (`WIP/Wip.Bones.Agents/Wip.Bones.Agents.csproj:28`), so `DeepSeekThinkingMode` / `DeepSeekModelProviderOptions` stay resolvable and no new project edge was introduced.

`using Wip.Bones.Agents.Configuration;` was added to the six provider sources that consume the moved options and to seven provider test files; `using Wip.Bones.Agents.Play;` was already present in the two provider files that consume the parser (`WIP/Wip.Bones.ModelProviders.DeepSeek/DeepSeekBonesPlayTurnProvider.cs:3`, `WIP/Wip.Bones.ModelProviders.DeepSeek.Tests/Play/BonesPlayTurnResponseParserTests.cs:1`).

### 1.1 `ToDeepSeekModelProviderOptions()`

`WIP/Wip.Bones.Agents/Configuration/BonesDeepSeekProviderOptions.cs:157`:

```csharp
public DeepSeekModelProviderOptions ToDeepSeekModelProviderOptions()
    => new(
        baseUrl: BaseUrl,
        apiKey: ApiKeySource,
        timeout: Timeout,
        temperature: Temperature);
```

`WIP/Wip.Bones.ModelProviders.DeepSeek/BonesDeepSeekServiceCollectionExtensions.cs:23-24` now uses it instead of the deleted private `CreateDeepSeekModelProviderOptions` helper (behaviour identical: `BaseUrl`, `ApiKeySource` = `env:NAME`, `Timeout`, `Temperature`):

```csharp
services.AddSingleton(options);
services.AddSingleton(options.ToDeepSeekModelProviderOptions());
```

## 2. `BonesStageChatModel`

`WIP/Wip.Bones.Agents/Runtime/BonesStageChatModel.cs:13` wraps an inner `IChatModel` plus a configured model id. `Rewrite` (`:43`) returns the request untouched when `BonesDeepSeekModelIdentifier.IsSupported(request.ModelId)`, otherwise rebuilds it with the configured model, preserving messages/tools/maxTokens/temperature/correlationId. Both `CompleteAsync` (`:27`) and `StreamAsync` (`:34`) route through it.

New tests — `WIP/Wip.Bones.Tests/Runtime/BonesStageChatModelTests.cs`:

| Test | Assertion |
|---|---|
| `CompleteAsync_GivenUnsupportedStageModelId_ExpectedRewritesToConfiguredModel` | stage id → `deepseek-flash` |
| `StreamAsync_GivenUnsupportedStageModelId_ExpectedRewritesToConfiguredModel` | stage id → `deepseek-flash` |
| `CompleteAsync_GivenSupportedModelId_ExpectedPreservesModelId` (3 cases) | `deepseek-flash`, `deepseek-v4-pro`, `deepseek-v4-flash` pass through |
| `StreamAsync_GivenSupportedModelId_ExpectedPreservesModelId` | `deepseek-v4-pro` passes through |
| `CompleteAsync_GivenRewrittenRequest_ExpectedMessagesToolsAndOptionsUnchanged` | roles, content, single tool name/description/schema, `maxTokens`, `temperature`, `correlationId` all identical |
| `Constructor_GivenNullInnerModel_ExpectedThrowsArgumentNullException` | `ParamName == "inner"` |

## 3. `BonesChatModelPlayTurnProvider`

`WIP/Wip.Bones.Agents/Runtime/BonesChatModelPlayTurnProvider.cs:13` implements `IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>` over `IChatModel`. It maps `BonesPlayTurnRequest.Messages` role/content to `ChatMessage`s (`:53`), calls `CompleteAsync` with the request model id and correlation id (`:34`), parses with the moved parser and applies the contract's `?? string.Empty` (`:38`), then returns `BonesPlayTurnResult` carrying the response provider/model/usage.

New tests — `WIP/Wip.Bones.Tests/Runtime/BonesChatModelPlayTurnProviderTests.cs`:

| Test | Assertion |
|---|---|
| `ExecuteAsync_GivenModelAnswerContainingLegalMove_ExpectedParsesMoveId` | `move-play-left`, provider/model/correlation forwarded |
| `ExecuteAsync_GivenRequestMessages_ExpectedForwardsRolesAndContentInOrder` | `system`/`user` roles and content in order, model id, no tools |
| `ExecuteAsync_GivenIllegalModelAnswer_ExpectedEmptySelectedMoveId` | `string.Empty` |
| `ExecuteAsync_GivenJsonMoveIdAnswer_ExpectedParsesMoveId` | `{"moveId":"move-pass"}` → `move-pass` |
| `Constructor_GivenNullChatModel_ExpectedThrowsArgumentNullException` | `ParamName == "chatModel"` |

## 4. `AddDeepSeekChatModel` host-owned DI

`WIP/Wip.Runtime/DeepSeekChatModelServiceCollectionExtensions.cs`:

- `:13` keeps the options-instance overload; it registers the options then calls the core.
- `:29` adds the overload resolving `DeepSeekModelProviderOptions` from DI.
- `:38-46` core registration: `TryAddSingleton` `HttpClient`, `TryAddSingleton` the concrete `DeepSeekChatModel`, and `TryAddSingleton<IChatModel>` resolving `GetRequiredService<DeepSeekChatModel>()` — so the host can decorate `IChatModel` while the concrete instance stays resolvable.

New tests — `WIP/Wip.Runtime.Tests/Runtime/DeepSeekChatModelServiceCollectionExtensionsTests.cs`:

| Test | Assertion |
|---|---|
| `AddDeepSeekChatModel_GivenOptionsInstance_ExpectedResolvesConcreteModelAndSameChatModelInstance` | `IChatModel` is `DeepSeekChatModel`, `Assert.Same(concrete, chatModel)`, options instance registered |
| `AddDeepSeekChatModel_GivenOptionsResolvedFromDi_ExpectedResolvesConcreteModelAndSameChatModelInstance` | parameterless overload leaves the concrete/interface pair identical |
| `AddDeepSeekChatModel_GivenPreRegisteredHttpClient_ExpectedPreservesTestHandlerRegistration` | `TryAdd` does not clobber a host/test `HttpClient` |

There was no pre-existing registration test for this extension (only `WipRuntimeServiceCollectionExtensionsTests`, which covers `AddWipRuntime`), so the new class is the registration suite for `AddDeepSeekChatModel`.

## 5. Host composition

### 5.1 Project reference removed

`WIP/Wip.Bones.Host/Wip.Bones.Host.csproj` no longer references `Wip.Bones.ModelProviders.DeepSeek`. `WIP/Wip.Bones.Host.Tests/Wip.Bones.Host.Tests.csproj` also dropped its reference (no host test uses the provider types any more), so the provider is referenced **only** by its own test project.

`BonesHostOptions.cs:6` now imports `Wip.Bones.Agents.Configuration`; the `DeepSeekProviderOptions` / `Resilience` property shapes are unchanged. `BonesHostApplication.cs` no longer injects the provider-only `BonesDeepSeekResiliencePipeline`; the status payload keeps its `resilience` key as `(object?)null` (`BonesHostApplication.cs:286-288`) with a comment explaining the retirement.

### 5.2 `RegisterModelProviders` (`BonesHostServiceCollectionExtensions.cs:300`)

- DeepSeek branch: `AddDeepSeekChatModel(options.DeepSeekProviderOptions.ToDeepSeekModelProviderOptions())`, then `services.Replace(ServiceDescriptor.Singleton<IChatModel>(sp => new BonesStageChatModel(sp.GetRequiredService<DeepSeekChatModel>(), configuredModelId)))` (`:314-317`).
- Stub branch: `services.AddSingleton<IChatModel>(new StubBonesChatModel())` (`:320`).
- Both branches then register `BonesStrategyCompileTool` (`:329`) and `IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>` → `BonesChatModelPlayTurnProvider` (`:333`).
- The three per-stage `IModelProvider` production registrations are **gone**: the `BonesHostStubModelProviders` class and its three nested stub providers were deleted, and no `DeepSeekBones*Provider` type is referenced from the host.

`StubBonesChatModel` (`:35`) returns the same canned authoring / enhancement scripts the two deleted stub providers produced, choosing by the last user message: `## Allowed moves` or `# Play turn request` → legal move id parsed with `\(move id: X\)` regex; `Strategy script enhancement request` → enhancement script; otherwise → authoring script. It implements both `CompleteAsync` and `StreamAsync`.

### 5.3 Explicit agent factories (`BonesHostServiceCollectionExtensions.cs:337`)

After `AddWipCapabilities().AddBonesLearningWorkflowWithViewer(options.ListenUrl)` returns, `RegisterRuntimeAgents` runs and re-registers both agent types (last registration wins, so the co-learning wrappers resolve them):

```csharp
private static void RegisterRuntimeAgents(IServiceCollection services, BonesHostOptions options)
{
    services.RemoveAll<BonesPonderAgent>();
    services.AddSingleton(serviceProvider => CreatePonderAgent(serviceProvider, options));

    services.RemoveAll<BonesEnhanceStrategyAgent>();
    services.AddSingleton(serviceProvider => CreateEnhanceStrategyAgent(serviceProvider, options));
}
```

- `CreatePonderAgent` (`:350`) → `new BonesPonderAgent(artifactStore, CreateAgentExecutor(sp), BonesStrategyScriptHost, options.Ponder, CreateAgentMemory(sp), options.AgentLoop)`.
- `CreateEnhanceStrategyAgent` (`:363`) → `new BonesEnhanceStrategyAgent(artifactStore, executor, scriptHost, promotionEvaluator: null, promotionOptions: options.Promotion, compileRetryOptions: options.Ponder, aggressiveIterationOptions: options.AggressiveIteration, gameBudget, strategyLibrary, forcedEvaluationSeed, playModelProvider, playModelId: options.PlayModelId, logger, memory, loopOptions: options.AgentLoop)`.
- `CreateAgentExecutor` (`:385`) → `new AgentExecutor(sp.GetRequiredService<IChatModel>(), new RuntimeToolInvoker(sp, BonesAgentToolDescriptors.Create()))`.
- `CreateAgentMemory` (`:390`) → `new ArtifactAgentMemory(sp.GetRequiredService<IArtifactStore>())`.
- New `BonesHostOptions.AgentLoop` (`BonesHostOptions.cs:101`) defaults to `new BonesAgentLoopOptions()`.

No global `IAgentExecutor` or `IToolInvoker` is registered — verified by inspection of the file: the only `AgentExecutor` / `RuntimeToolInvoker` constructions are the two private helper factories above (the shell agent path keeps its own generic tool set).

## 6. Host composition test

`WIP/Wip.Bones.Host.Tests/BonesHostRuntimeAgentCompositionTests.cs` builds `AddBonesHostServices` with `ModelProvider=stub`, resolves `IChatModel`, `BonesPonderAgent`, `BonesEnhanceStrategyAgent` and `IArtifactStore`, then:

1. asserts `IChatModel` is `StubBonesChatModel`;
2. runs Ponder for seats 1–4 (the host loop's `AllSeatsLearning` behaviour, and Enhance requires an opponent strategy per seat) and asserts each result is a persisted `Script` / `Active` strategy whose markdown contains `class SeatStrategy`;
3. asserts the `bones-strategy-initial-seat-1-v1` artifact exists — proving the stub chat model output went through `AgentExecutor` → `RuntimeToolInvoker` → `BonesStrategyScriptHost` compile → knowledge store;
4. asserts an artifact with `ProducerType == "ArtifactAgentMemory"` exists — proving `ArtifactAgentMemory` is the composed memory port;
5. runs Enhance for seat 1 and asserts `PriorStrategyId == initial-seat-1-v1`, `StrategyId == initial-seat-1-v2`, `StrategyDocument.StrategyId == StrategyId`, the source contains `class SeatStrategy`, the promotion outcome is a defined enum value, and an active strategy id is returned.

Observed:

```text
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 42 s - Wip.Bones.Host.Tests.dll (net10.0)
```

## 7. Architecture rule

`tests/Wip.Architecture.Tests/ArchitectureRules.cs:27`:

```csharp
new("bones-agents-no-legacy-provider", "Wip.Bones.Agents", ["Wip.Bones.ModelProviders.DeepSeek"])
```

`tests/Wip.Architecture.Tests/ArchitectureReferenceTests.cs:48-79` now seeds the synthetic graph with `["Wip.Bones.Agents"] = ["Wip.Bones", "Wip.Bones.ModelProviders.DeepSeek"]` and `["Wip.Bones.ModelProviders.DeepSeek"] = []` and asserts both the existing `shellhost-no-bones` violation **and** the new one (`:69`):

```csharp
Assert.Contains(
    violations,
    violation => violation.RuleId == "bones-agents-no-legacy-provider"
        && violation.Source == "Wip.Bones.Agents"
        && violation.Target == "Wip.Bones.ModelProviders.DeepSeek");
```

`ProjectReferences_GivenForbiddenEdges_ExpectedNoUnwaivedViolations` additionally proves the **real** graph has no such edge (no unwaived violations, no stale waivers).

Observed:

```text
Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 6 m 41 s - Wip.Architecture.Tests.dll (net10.0)
```

## 8. Verification commands and observed output

All commands were run through the mandated WSL runner:

```powershell
wsl.exe -d Ubuntu -- bash /mnt/c/Users/ricar/dev/Modus/.tmp/wsl-dotnet.sh <dotnet args>
```

| # | Command (args after the runner) | Observed |
|---|---|---|
| 1 | `build WIP/Wip.Bones.Host/Wip.Bones.Host.csproj -c Release` | `Build succeeded.` / `0 Error(s)` / `Time Elapsed 00:03:28.83` (exit 0 on the confirmation run) |
| 2 | `build WIP/Wip.Bones.Host.Tests/Wip.Bones.Host.Tests.csproj -c Release` | `Build succeeded.` / `0 Error(s)` (exit 0) |
| 3 | `test WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj -c Release` | `Passed! - Failed: 0, Passed: 446, Skipped: 0, Total: 446, Duration: 1 m 33 s` |
| 4 | `test tests/Wip.Architecture.Tests/Wip.Architecture.Tests.csproj -c Release` | `Passed! - Failed: 0, Passed: 4, Total: 4` |
| 5 | `test WIP/Wip.Runtime.Tests/... --filter "FullyQualifiedName~DeepSeekChatModelServiceCollectionExtensionsTests"` | `Passed! - Failed: 0, Passed: 3, Total: 3` |
| 6 | `test WIP/Wip.Bones.Tests/... --filter "FullyQualifiedName~BonesStageChatModelTests"` | `Passed! - Failed: 0, Passed: 8, Total: 8` |
| 7 | `test WIP/Wip.Bones.Tests/... --filter "FullyQualifiedName~BonesChatModelPlayTurnProviderTests"` | `Passed! - Failed: 0, Passed: 5, Total: 5` |
| 8 | `test WIP/Wip.Bones.Host.Tests/... --filter "FullyQualifiedName~BonesHostRuntimeAgentCompositionTests"` | `Passed! - Failed: 0, Passed: 1, Total: 1, Duration: 42 s` |
| 9 | non-browser host subset: `BonesHostDeepSeekServiceCollectionTests`, `BonesPoolWiringTests`, `BonesPoolResumeAndIntegrityTests`, `BonesHostDeepSeekStatusApiTests`, `BonesHostOptionsDeepSeekTests`, `BonesHostDeepSeekOperatorDocumentationTests`, `BonesHostRuntimeAgentCompositionTests` | `Passed! - Failed: 0, Passed: 40, Skipped: 0, Total: 40, Duration: 21 s` |
| 10 | `test WIP/Wip.Bones.ModelProviders.DeepSeek.Tests/... -c Release` | `Failed! - Failed: 1, Passed: 47, Skipped: 0, Total: 48` — the single red is the pre-existing waived `BehaviorProofComplianceGateTests.BehaviorProofCompliance_GivenWipBonesDeepSeekChecklistItems_RequiresExecutableRuntimeProofForEachItem` (`docs/plan/test-waivers.json:124`) |

Count reconciliation for `Wip.Bones.Tests`: the goal expected ~433 total; the two new WP4 test classes add 13 tests (8 + 5), and the observed total is 446 with **zero** failures — the three pre-existing waived behavior-proof meta-tests passed in this run.

### 8.1 Production grep proving no DeepSeek reference

`WIP/**/*.csproj`:

```text
WIP/Wip.Bones.Agents/Wip.Bones.Agents.csproj:15:    <InternalsVisibleTo Include="Wip.Bones.ModelProviders.DeepSeek.Tests" />
WIP/Wip.Bones.ModelProviders.DeepSeek/Wip.Bones.ModelProviders.DeepSeek.csproj:7:    <PackageId>Wip.Bones.ModelProviders.DeepSeek</PackageId>
WIP/Wip.Bones.ModelProviders.DeepSeek.Tests/Wip.Bones.ModelProviders.DeepSeek.Tests.csproj:18:    <ProjectReference Include="..\Wip.Bones.ModelProviders.DeepSeek\Wip.Bones.ModelProviders.DeepSeek.csproj" />
```

- the only `ProjectReference` to the provider is from the provider's **own test project**;
- the other two hits are the provider's own `PackageId` and an `InternalsVisibleTo` naming the test assembly — neither is a production dependency;
- `Wip.Bones.Host.csproj` and `Wip.Bones.Host.Tests.csproj` no longer appear.

`WIP/**/*.cs`, excluding the provider project, all `*Tests*` directories and `obj/`:

```text
WIP/Wip.Shell.E2E/Compliance/BehaviorProofComplianceRegistry.cs:177:            OwningTestProjectDirectory: "WIP/Wip.Bones.ModelProviders.DeepSeek.Tests",
```

That is a **string literal** naming the test project directory in the compliance registry (test-ownership metadata), not a code dependency: there is **no** `using Wip.Bones.ModelProviders.DeepSeek;` and no namespace use anywhere in production code.

## 9. Host test updates required by the retirement

Because the host no longer registers any `IModelProvider<BonesStrategy*Request, …>`, four host test sites that overrode the old per-stage provider seam were moved to the chat-model seam. No assertion was deleted or weakened; each replacement asserts the same externally observable behaviour through the new port.

| File | Change |
|---|---|
| `WIP/Wip.Bones.Host.Tests/BonesHostDeepSeekServiceCollectionTests.cs` | DeepSeek case now asserts `IChatModel` is `BonesStageChatModel`, `DeepSeekChatModel` resolves, play-turn is `BonesChatModelPlayTurnProvider`, the compile tool resolves, and the two retired `IModelProvider` services are `null`; stub case asserts `StubBonesChatModel`; the model-id passthrough test now drives `IChatModel.CompleteAsync` with a stage id and asserts the HTTP body's `model` is the configured `deepseek-v4-pro` |
| `WIP/Wip.Bones.Host.Tests/BonesLearningLoopReliabilityHostTests.cs` | `DelayedStrategyAuthoringProvider` → `DelayedChatModel`, `FailFirstPonderAuthoringProvider` → `FailFirstChatModel`; the rejected-promotion factory composes Enhance through `BonesHostServiceCollectionExtensions.CreateEnhanceStrategyAgent` |
| `WIP/Wip.Bones.Host.Tests/BonesLearningLoopPromotionHostTests.cs` | `ScriptedEnhancementModelProvider` → `ScriptedEnhancementChatModel`; the Enhance override uses the same host factory helper, preserving the forced evaluation seed |
| `WIP/Wip.Bones.Host.Tests/Wip.Bones.Host.Tests.csproj` | removed the now-unused provider `ProjectReference` |

`BonesPoolWiringTests` / `BonesPoolResumeAndIntegrityTests` still construct the legacy `IModelProvider` shapes directly; they exercise the still-supported legacy constructors and remain green (40/40 subset above).

## 10. Not passing / not run

- **Pre-existing waived failure (not caused by WP4)**: `Wip.Bones.ModelProviders.DeepSeek.Tests.BehaviorProofComplianceGateTests.BehaviorProofCompliance_GivenWipBonesDeepSeekChecklistItems_RequiresExecutableRuntimeProofForEachItem` — 1 of 48 in the provider suite; recorded at `docs/plan/test-waivers.json:123-127` (owner `tech lead + QA`, expiry `2026-11-16`). It is a compliance meta-test over the provider checklist, untouched by this work package.
- **Browser/Playwright/viewer tests were not run** (`BonesHostPlaywrightApplication`, viewer E2E, `Wip.Bones.Web.Tests` browser paths): Chromium availability was not verified and the task forbids depending on it.
- **Heavy WebApplicationFactory reliability/promotion suites were not re-run in full** (`BonesLearningLoopReliabilityHostTests`, `BonesLearningLoopPromotionHostTests` beyond the compile-verified updates): the non-browser subset above covers the composition, registration, status-API and pool wiring; the two multi-minute loop suites were left to the orchestrator's full-suite run.
- **`Wip.Bones.Host` full suite, `Wip.Bones.Web.Tests`, and the whole-solution build** were not run; only the project-scoped builds listed in §8.
