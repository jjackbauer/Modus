# Round 5 — `TholdemRuntimeAgent`: LLM runtime agent over the WIP agent runtime

- **Date**: 2026-11-06
- **Branch**: `WIP`
- **HEAD at verification**: `d7d97239d58fbb5b73fa5f8a8b53e5ea97436174` (moved during this round: a concurrent Bones session committed WP4 while this round was in flight; this round's files are uncommitted working-tree additions, matching rounds 1–4)
- **Scope delivered**: `Wip.Tholdem.Agents.Runtime` — `TholdemRuntimeAgent : IPokerAgent` deciding through `IChatModel` → `AgentExecutor` → `RuntimeToolInvoker` → `ArtifactAgentMemory`; the session hand store; exactly four tools (seeded equity, pot odds, session-scoped hand history, opponent statistics) with descriptors and permissions; the live-model gate; 17 new committed tests including the four security suites.
- **Not created / not changed**: no `Wip.Tholdem.Host`; no file under `WIP/Wip.Tholdem/**`, `WIP/Wip.Tholdem.Web/**`, the existing Agents baseline/simulation sources, `Wip.slnx`, `tests/Wip.Architecture.Tests/**`, `tools/run-wip-tests.ps1`, or any other Tholdem project. The WIP project count stays 47.

## Files created

| Path | Purpose |
|---|---|
| `WIP/Wip.Tholdem.Agents/Runtime/TholdemAgentOptions.cs` | `record TholdemAgentOptions(string ModelId = "scripted", AgentLoopOptions? LoopOptions = null, int MaxObservationCharacters = 4000, int EquitySamples = 2000, int MemoryMessages = 4)` |
| `WIP/Wip.Tholdem.Agents/Runtime/SessionHandView.cs` | contracted eight-field per-seat view of one recorded hand |
| `WIP/Wip.Tholdem.Agents/Runtime/SeatStatistics.cs` | `record SeatStatistics(int Hands, double Vpip, double Pfr, double AggressionFactor, double ShowdownRate)` |
| `WIP/Wip.Tholdem.Agents/Runtime/SessionHandStore.cs` | thread-safe, session-scoped `Record` / `BuildFor(seat, limit)` / `BuildStatisticsFor(seat)` |
| `WIP/Wip.Tholdem.Agents/Runtime/EquityTool.cs` | `EquityToolRequest` / `EquityToolResult` / `EquityTool` over `Card.Parse` + `MonteCarloEquity.Estimate` |
| `WIP/Wip.Tholdem.Agents/Runtime/PotOddsTool.cs` | `PotOddsRequest` / `PotOddsResult` / `PotOddsTool` |
| `WIP/Wip.Tholdem.Agents/Runtime/HandHistoryTool.cs` | `HandHistoryRequest` / `HandHistoryResult` / `HandHistoryTool(seat, store)` (seat fixed at construction) |
| `WIP/Wip.Tholdem.Agents/Runtime/OpponentStatsTool.cs` | `OpponentStatsRequest` / `OpponentStatsResult` / `OpponentStatsTool(seat, store)` (opposing seat fixed at construction) |
| `WIP/Wip.Tholdem.Agents/Runtime/TholdemToolCatalog.cs` | four `ToolDefinition`s with JSON schemas + four `CapabilityDescriptor`s with permissions |
| `WIP/Wip.Tholdem.Agents/Runtime/LiveModelGate.cs` | `IsEnabled(out string? reason)`; never constructs a model, never echoes the key |
| `WIP/Wip.Tholdem.Agents/Runtime/TholdemRuntimeAgent.cs` | the agent under test: observation serialization/truncation, fixed system prompt, task text, memory tail, parse → validate → documented fallback, violation ledger |
| `WIP/Wip.Tholdem.Tests/Runtime/RuntimeAgentTestSupport.cs` | `ScriptedChatModel` (records every `ChatRequest`, empty `StreamAsync`), `InMemoryArtifactStore`, `DenyingPermissionEvaluator`, `RuntimePipeline` (real `ServiceCollection` wiring), helpers |
| `WIP/Wip.Tholdem.Tests/Runtime/TholdemRuntimeAgentTests.cs` | 11 facts: happy path, tool loop, illegal action, malformed output, budget, hidden information, permission denial, prompt injection (model output + memory), truncation, 100 KB message |
| `WIP/Wip.Tholdem.Tests/Runtime/SessionHandStoreTests.cs` | 2 facts: newest-last seat-scoped views without hidden information + tool seat projection; crafted-hand VPIP/PFR/AF/showdown |
| `WIP/Wip.Tholdem.Tests/Runtime/ToolContractTests.cs` | 2 facts: pot-odds math/validation; catalog names, JSON schemas, permissions, descriptor types |
| `WIP/Wip.Tholdem.Tests/Runtime/LiveModelGateTests.cs` | 1 fact: environment matrix, restore in `finally`, collection with `DisableParallelization` |
| `WIP/Wip.Tholdem.Tests/Runtime/RuntimeSourceBoundaryTests.cs` | 1 fact: case-sensitive source scan for `TableState`, `SpectatorSnapshot`, `HoleCardsSeat0`, `HoleCardsSeat1`, `Deck` |
| `docs/goal-tholdem/round-5/implementation.md` | this report |

## Files modified

| Path | Change |
|---|---|
| `WIP/Wip.Tholdem.Agents/Wip.Tholdem.Agents.csproj` | added `ProjectReference` to `..\Wip.Abstractions\Wip.Abstractions.csproj` and `..\Wip.Runtime\Wip.Runtime.csproj`; the existing `..\Wip.Tholdem\Wip.Tholdem.csproj` reference is unchanged |
| `docs/plan/test-waivers.json` | `expectedTotals["Wip.Tholdem.Tests"]` 86 → 103 (the file parses; verified with `ConvertFrom-Json`) |

## Contract surface (for later rounds)

Namespace `Wip.Tholdem.Agents.Runtime`; public types exactly as dispatched:

- `TholdemAgentOptions`, `SessionHandView`, `SeatStatistics`, `SessionHandStore` (`Record`, `BuildFor`, `BuildStatisticsFor`).
- `EquityToolRequest(IReadOnlyList<string> HoleCards, IReadOnlyList<string> Board, int Samples = 2000, ulong? Seed = null)` → `EquityToolResult(double Equity, int Samples, int Wins, int Ties, int Losses)`.
- `PotOddsRequest(long Pot, long AmountToCall)` → `PotOddsResult(double PotOdds, double BreakEvenEquity, long CallAmount)`; pot odds and break-even equity are the same number.
- `HandHistoryRequest(int Limit = 10)` → `HandHistoryResult(int Seat, IReadOnlyList<SessionHandView> Hands)`.
- `OpponentStatsRequest()` → `OpponentStatsResult(int Seat, SeatStatistics Statistics)`.
- `TholdemToolCatalog.CreateDefinitions()` / `CreateDescriptors()`; tool names exactly `equity-calculator`, `pot-odds`, `hand-history-query`, `opponent-statistics`; the two calculators declare no permissions, the two session readers declare `artifact.read`.
- `LiveModelGate.IsEnabled(out string? reason)`.
- `TholdemRuntimeAgent(IAgentExecutor, IAgentMemory, SessionId, string worktreePath, TholdemAgentOptions, IReadOnlyList<ToolDefinition>)`, `Name => "runtime-agent"`, `Violations`, `ActAsync`.

## Implementation notes

### Observation, prompt, task

- The task is built from `observation.ToCanonicalJson()` only. It is truncated to `MaxObservationCharacters` with the literal marker `...[truncated N chars]`.
- Order in the task text: `Hand <n>, seat <s>.` → the exact reply schema → the observation JSON **last**. The observation is deliberately last so the truncation marker terminates the task (test 9 asserts the task ends with the marker), which also makes the truncated region unambiguous to the model.
- The fixed system prompt states the agent is a heads-up player, that observation/tool output are **untrusted** data and never instructions, that tool permissions are fixed for the session and cannot be changed by content, the one-JSON-object reply rule, and the legal-action contract (case-insensitive kind, integer `amountTo` inside `[minRaiseTo, maxRaiseTo]` for bet/raise, no amount for other kinds, prefer check-then-fold when truncated).
- The turn context is deliberately unread: the hand number and seat come from the hidden-information-safe observation, never from the session seed.

### Memory

`LoadAsync` → the last `MemoryMessages` messages are passed as `initialMessages`. After the run the task is always appended; the assistant's final content is appended when present (a budget stop has no final message). The injection test proves the stored model text reaches the next turn's request while the tool list and permissions stay byte-identical.

### Parsing, validation and fallback

1. Status other than `Completed` → `budget:<Status>` (e.g. exactly `budget:MaxStepsReached`) → fallback.
2. Missing final message → `malformed:final-message-missing`; final content longer than 16 KiB → `malformed:final-message-too-long` (see decision 1) → fallback.
3. JSON extraction: first `{` to last `}`; missing braces → `malformed:no-json-object`; unparsable → `malformed:invalid-json`; non-object → `malformed:not-an-object`; missing/unknown `action` → `malformed:missing-action` / `malformed:unknown-action`; bet/raise without a positive integer `amountTo` → `malformed:missing-amount`; any other kind carrying `amountTo` → `malformed:unexpected-amount`.
4. Legal validation: kind must be in `observation.LegalActionKinds` (`illegal-action:kind-not-legal:<kind>`), and bet/raise must satisfy `MinRaiseTo <= amountTo <= MaxRaiseTo` (`illegal-action:amount-out-of-range:<amount>:<min>:<max>`) → fallback.
5. Fallback, validated against the observation at every step: legal `Check`, else legal `Fold`, else legal `Call`. An observation that offers none of the three (impossible while a hand is live) throws rather than returning an illegal action.

### Session store

- `BuildFor(seat, limit)` returns at most `limit` views with the newest last; a non-positive limit returns an empty list; the record list is snapshotted under a lock, so recording and reading never race.
- Per-seat hole cards are obtained by replaying the record through the public `HeadsUpHandEngine.Replay` and projecting with `PlayerObservation.ForSeat`. That is the only public path that yields one seat's cards, it validates the record, and it keeps the four forbidden tokens out of the runtime sources — the store never names `HoleCardsSeat0/1`, never reads the seed and never reads the undealt card order.
- Statistics: `Hands` = recorded hands; VPIP counts hands whose action log holds a preflop `Call`/`Bet`/`Raise`/`AllIn` for that seat; PFR counts preflop `Raise`/`AllIn`; `AggressionFactor = (Bet+Raise+AllIn) / max(1, Call)`; `ShowdownRate` = showdown hands / hands. Empty store → all zeros.

### Decisions that are not literal in the dispatch (all deliberate, all documented in code)

1. **16 KiB final-message cap.** The contract's "or parsing fails" is extended with a dedicated length bound, because a 100 KB model message must be rejected as malformed rather than parsed into an action. A bounded parse of untrusted model text is the only implementation that satisfies test 9b: with the cap removed, a 100 KB `{"action":"raise","amountTo":30,...}` parses into a legal `RaiseTo(30)`.
2. **Equity sample budget.** `EquityTool` has an optional `sampleBudget` constructor argument (default 2000); the request's `Samples` is honoured up to that budget, and a non-positive `Samples` means "use the budget". This makes `TholdemAgentOptions.EquitySamples` the composition-time budget (`new EquityTool(options.EquitySamples)`, as the test pipeline does) and stops a model from requesting an unbounded Monte Carlo run. Stale JSON binding of an omitted optional argument and an explicit `0` are indistinguishable, hence the same treatment.
3. **Hand-history limit.** `Limit <= 0` means the documented default 10; the tool caps at 100. The store's own contract (`limit <= 0` → empty) is unchanged and covered directly.
4. **Session read tools are `artifact.read`-gated but read process memory, not artifacts.** The dispatch fixes the permission, so the descriptors declare it; the tools themselves read the in-process `SessionHandStore`. A deployment that resolves history from artifacts can swap the store without touching the agent or the permissions.

## Test count

**103 test cases**: 76 pre-existing from rounds 1–3 + 10 from round 4 + **17 new `[Fact]` methods**, 0 `[Theory]`, 0 skips, 0 waived failures.

| New suite | Facts |
|---|---|
| `TholdemRuntimeAgentTests` | 11 (happy path, equity tool loop, illegal action, malformed output with fold and check fallbacks, step budget, hidden-information scan, permission denial + unknown tool, injection in model output, injection in memory, truncation, 100 KB message) |
| `SessionHandStoreTests` | 2 |
| `ToolContractTests` | 2 |
| `LiveModelGateTests` | 1 |
| `RuntimeSourceBoundaryTests` | 1 |
| **Total new** | **17** |

`docs/plan/test-waivers.json` parses and `expectedTotals["Wip.Tholdem.Tests"] == 103`.

## Test-first evidence (red)

The six test files were written before any production type existed (only the two `ProjectReference` lines were added afterwards). The first build, run before the implementation, could not compile the test assembly:

```text
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Runtime\RuntimeAgentTestSupport.cs(3,11): error CS0234: The type or namespace name 'Abstractions' does not exist in the namespace 'Wip' (are you missing an assembly reference?) [C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Wip.Tholdem.Tests.csproj]
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Runtime\RuntimeAgentTestSupport.cs(4,11): error CS0234: The type or namespace name 'Abstractions' does not exist in the namespace 'Wip' (are you missing an assembly reference?) [C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Wip.Tholdem.Tests.csproj]
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Runtime\LiveModelGateTests.cs(1,26): error CS0234: The type or namespace name 'Runtime' does not exist in the namespace 'Wip.Tholdem.Agents' (are you missing an assembly reference?) [C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Wip.Tholdem.Tests.csproj]
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Runtime\RuntimeAgentTestSupport.cs(21,43): error CS0246: The type or namespace name 'IChatModel' could not be found (are you missing a using directive or an assembly reference?) [C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Wip.Tholdem.Tests.csproj]
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Runtime\RuntimeAgentTestSupport.cs(171,9): error CS0246: The type or namespace name 'SessionHandStore' could not be found (are you missing a using directive or an assembly reference?) [C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Wip.Tholdem.Tests.csproj]
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Runtime\RuntimeAgentTestSupport.cs(173,9): error CS0246: The type or namespace name 'TholdemRuntimeAgent' could not be found (are you missing a using directive or an assembly reference?) [C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Wip.Tholdem.Tests.csproj]
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Runtime\RuntimeAgentTestSupport.cs(195,9): error CS0246: The type or namespace name 'TholdemAgentOptions' could not be found (are you missing a using directive or an assembly reference?) [C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Wip.Tholdem.Tests.csproj]
    1 Warning(s)
    58 Error(s)

Time Elapsed 00:00:15.91
[exit code: 1]
```

Command: `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release`. The production files were then written; the first implementation build failed on one real error (a missing `using Wip.Abstractions.Agents;` in the test file, then one duplicate variable name), which were fixed before the green run. No test was weakened, skipped or deleted to reach green.

## Final green run

```text
$ dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
    2 Warning(s)
    0 Error(s)

Time Elapsed 00:00:26.79
EXIT=0

$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 103 PASSED 103 FAILED 0 SKIPPED 0
EXIT=0
```

The two warnings are pre-existing in `WIP/Wip.Runtime` (which this round did not touch; it entered the Tholdem build graph only because of the new `ProjectReference`) and are not produced by any file this round created:

```text
C:\Users\ricar\dev\Modus\WIP\Wip.Runtime\Runtime\AgentExecutor.cs(143,53): warning CS8425: Async-iterator 'AgentExecutor.ExecuteStreamingAsync(...)' has one or more parameters of type 'CancellationToken' but none of them is decorated with the 'EnumeratorCancellation' attribute ...
C:\Users\ricar\dev\Modus\WIP\Wip.Runtime\Runtime\DeepSeekChatModel.cs(106,17): warning CA2024: Do not use 'reader.EndOfStream' in an async method ...
```

## Mutation

Mutant: `TholdemRuntimeAgent.TryValidateAction` replaced by an unconditional accept, i.e. the action parser ignores `LegalActionKinds` (and the raise bounds) and accepts any parsed action.

Pre-mutation SHA-256 of `WIP/Wip.Tholdem.Agents/Runtime/TholdemRuntimeAgent.cs`:
`b6e04293f786090f5064d602b081ae00909021e8ae03a3fadf01ff2346d8526a`

Forced rebuild (`--no-incremental`) and run:

```text
    2 Warning(s)
    0 Error(s)
--- mutant run ---
FAIL TholdemRuntimeAgentTests.ActAsync_GivenInjectionInMemory_ExpectedUnchangedToolsAndPermissions: EqualException: Assert.Equal() Failure: Values differ
FAIL TholdemRuntimeAgentTests.ActAsync_GivenInjectionInModelOutput_ExpectedIllegalActionRejected: SingleException: Assert.Single() Failure: The collection was empty
FAIL TholdemRuntimeAgentTests.ActAsync_GivenRaiseAboveMaximum_ExpectedIllegalActionViolationAndLegalFallback: SingleException: Assert.Single() Failure: The collection was empty
TOTAL 103 PASSED 100 FAILED 3 SKIPPED 0
[exit code: 1]
```

Test 3 (`ActAsync_GivenRaiseAboveMaximum_…`) fails as required (no violation recorded; the agent returns the illegal `RaiseTo(999999)` instead of the fold fallback). Restored and verified by hash:

```text
restored_sha256=b6e04293f786090f5064d602b081ae00909021e8ae03a3fadf01ff2346d8526a
match_pre_mutation=True
```

Forced rebuild and re-run after the restore:

```text
    2 Warning(s)
    0 Error(s)
--- restored run ---
TOTAL 103 PASSED 103 FAILED 0 SKIPPED 0
```

## Verification commands (final tree, exact observed output)

### 1. Test project build

```text
$ dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
    2 Warning(s)
    0 Error(s)
EXIT=0
```

### 2. Tholdem suite (supplementary runner; `dotnet test` is forbidden here and aborts locally)

```text
$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 103 PASSED 103 FAILED 0 SKIPPED 0
EXIT=0
```

### 3. Whole solution

```text
$ dotnet build Wip.slnx --configuration Release
    126 Warning(s)
    0 Error(s)

Time Elapsed 00:01:57.31
EXIT=0
```

No unrelated WIP project was mid-refactor in a failing state, so the direct build was not needed.

### 4. Architecture tests

```text
$ dotnet build tests/Wip.Architecture.Tests/Wip.Architecture.Tests.csproj -c Release
    0 Warning(s)
    0 Error(s)

$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- tests/Wip.Architecture.Tests/bin/Release/net10.0/Wip.Architecture.Tests.dll
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0
EXIT=0
```

`ArchitectureReferenceTests.ProjectGraph_GivenWipSolution_ExpectedAllWipProjects` asserts `Assert.Equal(47, graph.Count)` (`tests/Wip.Architecture.Tests/ArchitectureReferenceTests.cs:16`); the new references add no project.

### 5. Solution coverage

```text
$ powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-solution-coverage.ps1
solution=Wip.slnx
wip_project_count=47
solution_wip_project_count=47
missing_from_solution=0
solution_paths_not_on_disk=0
solution_coverage=ok
EXIT=0
```

### 6. Harness sync

```text
$ dotnet run --project tools/HarnessSync -- --check
EXIT=0
```

`HarnessSync --check` prints nothing on success; only the exit code is observable. It also proves every new file is UTF-8 without a BOM (byte-checked separately: 0 BOM, 0 UTF-16 across all 17 new files).

## What I could not verify / make pass

- **Nothing in the required verification set failed.** All six commands above were observed green on the final tree.
- `dotnet test` was not run: the dispatch forbids it and the local VSTest host aborts with `Win32Exception (5)`; local verification is the supplementary `RunXunit` runner over the same compiled assembly. CI (`Tests CI` / `Modus CI`) is not observable from this machine (no `gh`, no token), so this is a local green claim only — recorded, never presented as CI green.
- The supplementary runner ignores xUnit `[Collection]` metadata, so `LiveModelGateTests`' `DisableParallelization` collection is enforced only under VSTest/CI. Locally the runner is sequential, so no environment race was possible, but that specific isolation attribute was not exercised.
- `HarnessSync --check` has no success output to paste; exit code `0` is the whole observation.
- Round 5 does not deliver the CLI, the learning loop or `Wip.Tholdem.Host`; those remain rounds 6+ and were not attempted.

## Explicitly out of scope and untouched

`WIP/Wip.Tholdem/**`, `WIP/Wip.Tholdem.Web/**`, `WIP/Wip.Tholdem.Agents/Baselines/**`, `WIP/Wip.Tholdem.Agents/Simulation/**`, `WIP/Wip.Tholdem.Agents/Equity/**`, `WIP/Wip.Tholdem.Agents/IPokerAgent.cs`, `WIP/Wip.Tholdem.Agents/AgentTurnContext.cs`, `Wip.slnx`, `tests/Wip.Architecture.Tests/**`, `tools/run-wip-tests.ps1`, and every `docs/goal-tholdem/` file other than this report.
