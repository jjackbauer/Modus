# Round 3 — `Wip.Tholdem.Agents`: deterministic baselines, seeded equity, batch simulation and metrics

- **Date**: 2026-11-06
- **Branch**: `WIP`
- **HEAD at verification**: `409b6f9bfd0a727a32cf8c7977f2f2163730b8f0`. Another session committed to `WIP` during this round (HEAD moved from `3e0087a` to `409b6f9`); the Tholdem work in this report is uncommitted on top of that commit.
- **Scope delivered**: `Wip.Tholdem.Agents` (`IPokerAgent` over `PlayerObservation` only), deterministic Random-Legal / Tight-Aggressive / Pot-Odds-Equity baselines, seeded Monte Carlo equity, batch simulator with canonical hand histories + session manifest, and a pure metrics calculator with confidence intervals.
- **Not created**: `Wip.Tholdem.Host`. No file under `WIP/Wip.Tholdem/Cards`, `Hands`, `Engine` or `History` was changed. No `WIP/Wip.Bones*`, `WIP/Wip.Shell*` or `WIP/Wip.Runtime*` file was changed.

## Files created

| Path | Purpose |
|---|---|
| `WIP/Wip.Tholdem.Agents/Wip.Tholdem.Agents.csproj` | net10.0 library; ImplicitUsings/Nullable; one ProjectReference to `..\Wip.Tholdem\Wip.Tholdem.csproj` |
| `WIP/Wip.Tholdem.Agents/AgentTurnContext.cs` | `readonly record struct AgentTurnContext(ulong SessionSeed, int HandNumber, int Seat, int ActionIndex)` |
| `WIP/Wip.Tholdem.Agents/IPokerAgent.cs` | `Name` + `ActAsync(PlayerObservation, AgentTurnContext, CancellationToken)` |
| `WIP/Wip.Tholdem.Agents/SeedDerivation.cs` | exact unchecked splitmix64 derivation from the dispatch contract |
| `WIP/Wip.Tholdem.Agents/IllegalAgentActionException.cs` | `InvalidOperationException` carrying `AgentName`, `HandNumber`, `Action` |
| `WIP/Wip.Tholdem.Agents/Equity/EquityResult.cs` | `(Equity, Samples, Wins, Ties, Losses)` |
| `WIP/Wip.Tholdem.Agents/Equity/MonteCarloEquity.cs` | seeded Monte Carlo equity against one uniformly random opponent hand |
| `WIP/Wip.Tholdem.Agents/Randomness/Xorshift64Star.cs` | internal xorshift64* with the domain constants (zero-seed replacement included) |
| `WIP/Wip.Tholdem.Agents/Baselines/RandomLegalAgent.cs` | uniform legal kind, uniform bet/raise size in `[MinRaiseTo, MaxRaiseTo]`, all from `SeedDerivation` |
| `WIP/Wip.Tholdem.Agents/Baselines/TightAggressiveProfile.cs` | `(RaiseThreshold 0.72, CallThreshold 0.45, BluffFrequency 0.12)` |
| `WIP/Wip.Tholdem.Agents/Baselines/TightAggressiveAgent.cs` | chart-like preflop strength, postflop category + kicker, seeded bluff when no call is required |
| `WIP/Wip.Tholdem.Agents/Baselines/PotOddsEquityAgent.cs` | 2000-sample seeded equity, call at `equity >= potOdds + 0.05`, else fold / value-bet |
| `WIP/Wip.Tholdem.Agents/Baselines/LegalActionSelection.cs` | internal helper: only listed kinds; bet/raise clamped into `[MinRaiseTo, MaxRaiseTo]`; defensive legal fallback |
| `WIP/Wip.Tholdem.Agents/Baselines/HandStrength.cs` | internal deterministic strength in `[0,1]` |
| `WIP/Wip.Tholdem.Agents/Simulation/SimulationOptions.cs` | options record with `Hands > 0` validated in the constructor |
| `WIP/Wip.Tholdem.Agents/Simulation/ConfidenceInterval.cs` | `(Low, High)` |
| `WIP/Wip.Tholdem.Agents/Simulation/HandOutcome.cs` | per-hand supervision row |
| `WIP/Wip.Tholdem.Agents/Simulation/AgentMetrics.cs` | per-agent metrics record |
| `WIP/Wip.Tholdem.Agents/Simulation/MetricsCalculator.cs` | exact contracted formulas (Wilson 95%, n-1 interval, aggression, showdown) |
| `WIP/Wip.Tholdem.Agents/Simulation/SimulationReport.cs` | session id, seed, hands, manifest hash, canonical report JSON, metrics |
| `WIP/Wip.Tholdem.Agents/Simulation/MatchSimulator.cs` | reproducible batch match, canonical hand histories, manifest, report |
| `WIP/Wip.Tholdem.Agents/Simulation/CanonicalJsonWriter.cs` | internal whitespace-free, invariant-culture JSON primitives |
| `WIP/Wip.Tholdem.Tests/AgentTestSupport.cs` | seed search, record/manifest readers, temp directories, source-file list |
| `WIP/Wip.Tholdem.Tests/SeedDerivationTests.cs` | 2 facts |
| `WIP/Wip.Tholdem.Tests/RandomLegalAgentTests.cs` | 1 fact |
| `WIP/Wip.Tholdem.Tests/SimulationReproducibilityTests.cs` | 2 facts |
| `WIP/Wip.Tholdem.Tests/HistoryIntegrityTests.cs` | 1 fact |
| `WIP/Wip.Tholdem.Tests/MetricsCalculatorTests.cs` | 3 facts |
| `WIP/Wip.Tholdem.Tests/ZeroSumTests.cs` | 1 fact |
| `WIP/Wip.Tholdem.Tests/MonteCarloEquityTests.cs` | 4 facts |
| `WIP/Wip.Tholdem.Tests/TightAggressiveAgentTests.cs` | 3 facts |
| `WIP/Wip.Tholdem.Tests/PotOddsEquityAgentTests.cs` | 3 facts |
| `WIP/Wip.Tholdem.Tests/AgentInformationBoundaryTests.cs` | 2 facts |
| `WIP/Wip.Tholdem.Tests/SimulatorFailureTests.cs` | 2 facts |
| `docs/goal-tholdem/round-3/implementation.md` | this report |

## Files modified

| Path | Change |
|---|---|
| `WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj` | one `ProjectReference` to `..\Wip.Tholdem.Agents\Wip.Tholdem.Agents.csproj` |
| `Wip.slnx` | one `<Project Path="WIP/Wip.Tholdem.Agents/Wip.Tholdem.Agents.csproj" />` inside the existing `/WIP/Tholdem/` folder |
| `tests/Wip.Architecture.Tests/ArchitectureReferenceTests.cs` | `Assert.Equal(45, graph.Count)` → `46` (the working tree already held the round-1/2 43→45 change) |
| `tests/Wip.Architecture.Tests/ArchitectureRules.cs` | added `tholdem-agents-no-bones-or-hosts` after `tholdem-no-bones` |
| `docs/plan/test-waivers.json` | `expectedTotals["Wip.Tholdem.Tests"]` 52 → 76 |

## Implementation notes

- `SeedDerivation.Derive` is the dispatch formula verbatim, all in `unchecked`. `Derive(42,1,0,0,0) == 13149409904026330690 == 0xB67C16A39F7E2642`, matching the independent reference in `.tmp/tholdem-verify/seed-derivation-reference.txt`. The 10,000-context test covers hand 1..50 × seat 0..1 × action 0..9 × stream 0..9.
- `MonteCarloEquity.Estimate` validates exactly two hole cards, a 0/3/4/5-card board, positive samples and duplicate cards, then runs one xorshift64* state over the canonical remaining deck, partially shuffling it per sample: the first two drawn cards are the opponent hand, the next `5 - board.Count` complete the board. Equity is `(Wins + Ties/2.0)/Samples`.
- `MatchSimulator.Run`: hand `n` uses `SeedDerivation.Derive(options.Seed, n, 0, 0, 0)`, `buttonSeat = AlternateButton ? (n-1)%2 : 0`, engine `handNumber = n`. Each turn passes `PlayerObservation.ForSeat` and `AgentTurnContext(options.Seed, n, seat, actionsThatSeatHasTakenThisHand)`. An agent exception or an `IllegalActionException` from `Apply` becomes `IllegalAgentActionException` with the agent name, hand and action; no fallback is applied.
- Tracking: aggressive = `Bet`, `Raise`, `AllIn`; calls = `Call`; showdown participants = both seats when `ShowdownReached` (a contested hand cannot reach showdown after a fold).
- History: every hand is written as `hand-00001.json` … from `HandRecord.From(finalState).ToCanonicalJson()` as UTF-8 without BOM; `session.json` has the exact fixed order `{"schema":"tholdem.session.v1","sessionId":…,"seed":…,"hands":…,"startingStack":…,"smallBlind":…,"bigBlind":…,"agents":[…],"handHashes":[…]}`; `ManifestHash` is the lowercase hex SHA-256 of those bytes. The manifest is also hashed when `HistoryDirectory` is null, so the report is independent of whether files were written.
- `SessionId = "tholdem-" + first 16 lowercase hex chars of SHA-256(seed + "\n" + hands + "\n" + name0 + "\n" + name1)`. No clock, GUID or dictionary ordering.
- Report JSON order: `schema, sessionId, seed, hands, startingStack, smallBlind, bigBlind, alternateButton, agents, manifestHash, metrics[…]`; each metric is `agent, seat, hands, wins, winRate, winRateCi{low,high}, netChips, bigBlindsPer100, bigBlindsPer100Ci{low,high}, aggressionFactor, showdownRate`. Doubles use `"R"` with `InvariantCulture`; zero is written as `0`. `HistoryDirectory` is deliberately not in the report, otherwise two runs into different directories could not be byte-identical.
- `MetricsCalculator` uses the contracted formulas exactly: Wilson `z = 1.959963984540054`; sample standard deviation with `n-1`; degenerate `[value,value]` for one hand; `AggressionFactor = Σ aggressive / max(1, Σ calls)`; `ShowdownRate = count(Showdown && participant) / Hands`.

## Deliberate implementation decisions

1. **TightAggressive bluff scope.** The seeded bluff is only considered when `AmountToCall == 0` (open bet or big-blind option). The required behaviour "with `7h 2d` facing a large bet it folds" would otherwise be seed-dependent because the same context can trigger a 12 % bluff-raise. A bluff raise is still legal; the baseline simply does not do it facing a bet.
2. **`SimulationOptions` shape.** It is a `sealed record` with an explicit constructor whose parameter names and defaults match the contracted positional record (`Seed`, `Hands`, `StartingStack`, `SmallBlind`, `BigBlind`, `AlternateButton`, `HistoryDirectory`), so `Hands > 0` is validated at construction. Callers use the same positional/named-argument syntax.
3. **Report excludes `HistoryDirectory`**, as explained above; all other options are present.
4. **Test scratch location.** This machine's DSH sandbox denies `%TEMP%` directory creation to apphost executables launched from the workspace (`UnauthorizedAccessException`, observed repeatedly). Test scratch therefore lives under `<test output>/test-temp/<prefix-guid>` (workspace, gitignored, deleted in `finally`); the GUID is test-only, as the dispatch allows.
5. **No opponent-card overload.** `MonteCarloEquity` has only the contracted four-argument API; adding a fixed-opponent overload would expose hidden information to agents. See the "could not make pass" section.

## Test-first evidence (red)

The 24 new facts were written first against signature-only members whose bodies were temporarily `NotImplementedException`. Command:

```text
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

Observed (complete; `TOTAL 76 PASSED 53 FAILED 23 SKIPPED 0`, exit code 1):

```text
FAIL AgentInformationBoundaryTests.ActAsync_GivenOneHundredObservations_ExpectedDeterministicLegalActions: NotImplementedException: The method or operation is not implemented.
FAIL HistoryIntegrityTests.Run_GivenHistoryDirectory_ExpectedCanonicalBytesManifestHashesAndExactReplay: NotImplementedException: The method or operation is not implemented.
FAIL MetricsCalculatorTests.Compute_GivenFiftyWinsOfOneHundred_ExpectedWilsonInterval: NotImplementedException: The method or operation is not implemented.
FAIL MetricsCalculatorTests.Compute_GivenFourSyntheticHands_ExpectedExactMetricsAndBigBlindInterval: NotImplementedException: The method or operation is not implemented.
FAIL MetricsCalculatorTests.Compute_GivenSingleHand_ExpectedDegenerateBigBlindIntervalAndFullWinRate: NotImplementedException: The method or operation is not implemented.
FAIL MonteCarloEquityTests.Estimate_GivenAcesOnEmptyBoard_ExpectedSeededRandomHandEquity: NotImplementedException: The method or operation is not implemented.
FAIL MonteCarloEquityTests.Estimate_GivenInvalidInputs_ExpectedArgumentExceptions: ThrowsException: Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(System.ArgumentNullException)
Actual:   typeof(System.NotImplementedException)
FAIL MonteCarloEquityTests.Estimate_GivenSameInputsTwice_ExpectedIdenticalEquityResult: NotImplementedException: The method or operation is not implemented.
FAIL MonteCarloEquityTests.Estimate_GivenTurnBoard_ExpectedWithinTwoPointsOfExhaustiveEnumeration: NotImplementedException: The method or operation is not implemented.
FAIL PotOddsEquityAgentTests.ActAsync_GivenAcesFacingSmallCall_ExpectedCall: NotImplementedException: The method or operation is not implemented.
FAIL PotOddsEquityAgentTests.ActAsync_GivenSevenTwoOffsuitFacingOverbet_ExpectedFold: NotImplementedException: The method or operation is not implemented.
FAIL PotOddsEquityAgentTests.Run_GivenTwoHundredSimulatedHands_ExpectedOnlyLegalActions: NotImplementedException: The method or operation is not implemented.
FAIL RandomLegalAgentTests.Run_GivenTwoRandomLegalAgents_ExpectedTwoHundredCompletedLegalHandsAndConservation: NotImplementedException: The method or operation is not implemented.
FAIL SeedDerivationTests.Derive_GivenPinnedAcesContext_ExpectedExactSplitmix64Literal: NotImplementedException: The method or operation is not implemented.
FAIL SeedDerivationTests.Derive_GivenTenThousandContextCombinations_ExpectedAllValuesDistinct: NotImplementedException: The method or operation is not implemented.
FAIL SimulationReproducibilityTests.Run_GivenDifferentSeed_ExpectedDifferentManifestHashAndReport: NotImplementedException: The method or operation is not implemented.
FAIL SimulationReproducibilityTests.Run_GivenSameSeedAndAgents_ExpectedByteIdenticalFilesManifestAndReport: NotImplementedException: The method or operation is not implemented.
FAIL SimulatorFailureTests.Run_GivenIllegalAction_ExpectedExceptionNamingAgentAndHandWithoutFallback: ThrowsException: Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(Wip.Tholdem.Agents.IllegalAgentActionException)
Actual:   typeof(System.NotImplementedException)
FAIL SimulatorFailureTests.Run_GivenThrowingAgent_ExpectedExceptionNamingAgentAndHandWithoutFallback: ThrowsException: Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(Wip.Tholdem.Agents.IllegalAgentActionException)
Actual:   typeof(System.NotImplementedException)
FAIL TightAggressiveAgentTests.ActAsync_GivenAcesPreflopWithRaiseAvailable_ExpectedRaiseNotFold: NotImplementedException: The method or operation is not implemented.
FAIL TightAggressiveAgentTests.ActAsync_GivenSevenTwoOffsuitFacingLargeBet_ExpectedFold: NotImplementedException: The method or operation is not implemented.
FAIL TightAggressiveAgentTests.Run_GivenTwoHundredSimulatedHands_ExpectedOnlyLegalActions: NotImplementedException: The method or operation is not implemented.
FAIL ZeroSumTests.Run_GivenTwoHundredHandMatch_ExpectedNetChipsSumExactlyZero: NotImplementedException: The method or operation is not implemented.
TOTAL 76 PASSED 53 FAILED 23 SKIPPED 0
```

23 of the 24 new facts failed. The 24th (`AgentInformationBoundaryTests.AgentSources_GivenAgentFiles_ExpectedNoHiddenInformationIdentifiers`) only reads source files and passed during red; it is still a real assertion and later caught nothing because the implementation never referenced hidden state.

Note: the red run was captured before the mutation phase. During that phase the different-seed fact was renamed from `Run_GivenDifferentSeed_ExpectedDifferentManifestHashAndReport` to `Run_GivenDifferentSeed_ExpectedDifferentManifestHandHashesAndReport` and strengthened with per-hand hash/file comparisons; the fact count and the red fail/pass totals are unchanged.

## Test count

**76 test cases**: 76 `[Fact]` methods, 0 `[Theory]`, 0 `[InlineData]`, 0 skips.

| Suite | Facts |
|---|---|
| `CardTests` (pre-existing) | 4 |
| `SeededDeckTests` (pre-existing) | 6 |
| `HandEvaluatorTests` (pre-existing) | 12 |
| `HeadsUpHandEngineTests` (pre-existing) | 24 |
| `HandRecordTests` (pre-existing) | 3 |
| `PlayerObservationTests` (pre-existing) | 3 |
| `SeedDerivationTests` (new) | 2 |
| `RandomLegalAgentTests` (new) | 1 |
| `SimulationReproducibilityTests` (new) | 2 |
| `HistoryIntegrityTests` (new) | 1 |
| `MetricsCalculatorTests` (new) | 3 |
| `ZeroSumTests` (new) | 1 |
| `MonteCarloEquityTests` (new) | 4 |
| `TightAggressiveAgentTests` (new) | 3 |
| `PotOddsEquityAgentTests` (new) | 3 |
| `AgentInformationBoundaryTests` (new) | 2 |
| `SimulatorFailureTests` (new) | 2 |
| **Total** | **76** |

`docs/plan/test-waivers.json` parses and `expectedTotals["Wip.Tholdem.Tests"] == 76`.

## Mutation experiment

### Primary mutant (required): `return sessionSeed;`

- Original `WIP/Wip.Tholdem.Agents/SeedDerivation.cs` SHA-256: `33F45DACEFD844FF356C9AB87F4EAF7E52ED172A3F9361DDE5C80C047E91736B` (892 bytes)
- Mutated SHA-256: `512A3793F6843CB131369BC4FFD2323301FAD958035D21F30DFAF731088E3089`
- Forced rebuild: `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --no-incremental` → `0 Error(s)`
- Observed mutant run (complete failures; `TOTAL 76 PASSED 72 FAILED 4 SKIPPED 0`, exit code 1):

```text
FAIL SeedDerivationTests.Derive_GivenPinnedAcesContext_ExpectedExactSplitmix64Literal: EqualException: Assert.Equal() Failure: Values differ
Expected: 13149409904026330690
Actual:   42
FAIL SeedDerivationTests.Derive_GivenTenThousandContextCombinations_ExpectedAllValuesDistinct: EqualException: Assert.Equal() Failure: Values differ
Expected: 10000
Actual:   1
FAIL SimulationReproducibilityTests.Run_GivenSameSeedAndAgents_ExpectedByteIdenticalFilesManifestAndReport: TrueException: Expected at least 100 distinct deals in 120 hands, found 1.
FAIL ZeroSumTests.Run_GivenTwoHundredHandMatch_ExpectedNetChipsSumExactlyZero: TrueException: At least one seat must move chips over two hundred hands.
TOTAL 76 PASSED 72 FAILED 4 SKIPPED 0
```

- Restore: `RESTORED_SHA256=33F45DACEFD844FF356C9AB87F4EAF7E52ED172A3F9361DDE5C80C047E91736B`, `RESTORED_MATCHES_ORIGINAL=True`, `MUTANT_MARKER_ABSENT=True`, `CONTRACT_LINE_PRESENT=True`.
- Forced rebuild: `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --no-incremental` → `0 Warning(s) 0 Error(s)` (35.55 s).
- Restored green run:

```text
TOTAL 76 PASSED 76 FAILED 0 SKIPPED 0
```

The primary mutant is caught by the pinned derivation literal, the 10,000-context distinctness test, the byte-identical reproducibility test's "at least 100 distinct deals" guard, and the zero-sum movement guard. It cannot fail `Run_GivenDifferentSeed_…` because returning the session seed still makes different session seeds produce different hand seeds; see the supplementary mutant below, which proves that test is discriminating.

### Supplementary mutant (extra evidence): `return 0UL;`

- Mutated run (`TOTAL 76 PASSED 72 FAILED 4 SKIPPED 0`, exit code 1):

```text
FAIL SeedDerivationTests.Derive_GivenPinnedAcesContext_ExpectedExactSplitmix64Literal: EqualException: Assert.Equal() Failure: Values differ
Expected: 13149409904026330690
Actual:   0
FAIL SeedDerivationTests.Derive_GivenTenThousandContextCombinations_ExpectedAllValuesDistinct: EqualException: Assert.Equal() Failure: Values differ
Expected: 10000
Actual:   1
FAIL SimulationReproducibilityTests.Run_GivenDifferentSeed_ExpectedDifferentManifestHandHashesAndReport: FalseException: A different session seed must deal different hands, not only embed a different seed value.
FAIL SimulationReproducibilityTests.Run_GivenSameSeedAndAgents_ExpectedByteIdenticalFilesManifestAndReport: TrueException: Expected at least 100 distinct deals in 120 hands, found 1.
TOTAL 76 PASSED 72 FAILED 4 SKIPPED 0
```

- Restore: `RESTORED_SHA256=33F45DACEFD844FF356C9AB87F4EAF7E52ED172A3F9361DDE5C80C047E91736B`, `RESTORED_MATCHES_ORIGINAL=True`, `CONSTANT_ZERO_ABSENT=True`; forced rebuild and final green run as above.

No mutated file remains in the repository. The source file hash at final verification was re-checked as `33F45DACEFD844FF356C9AB87F4EAF7E52ED172A3F9361DDE5C80C047E91736B`.

## Verification (final observed runs)

### 1. Full solution build

Command:

```text
dotnet build Wip.slnx --configuration Release
```

Final observed output:

```text
    126 Warning(s)
    0 Error(s)

Time Elapsed 00:01:19.14
```

Exit code `0`, 0 lines containing `: error `. The 126 warnings are pre-existing warnings in other WIP projects; the Tholdem projects build with `0 Warning(s), 0 Error(s)`.

Transient note (honesty): the first attempt of this command in the final state failed with 3 errors because a concurrent session had deleted (and was rewriting) `WIP/Wip.Bones.ModelProviders.DeepSeek/BonesDeepSeekProviderOptions.cs`, `BonesDeepSeekResilienceOptions.cs` and `BonesPlayTurnResponseParser.cs`. The exact errors were `CSC : error CS2001: Source file '…' could not be found` for those three paths, `3 Error(s)`. That session then committed its refactor (HEAD `409b6f9`); the command above is the re-run after that commit, and it is green. This round did not modify any `WIP/Wip.Bones*` file.

### 2. Tholdem suite

Command:

```text
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

Observed: `TOTAL 76 PASSED 76 FAILED 0 SKIPPED 0`, exit code `0`.

### 3. Architecture suite

Command:

```text
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- tests/Wip.Architecture.Tests/bin/Release/net10.0/Wip.Architecture.Tests.dll
```

Observed: `TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0`, exit code `0`. The project-graph assertion is `Assert.Equal(46, graph.Count)` and the `tholdem-agents-no-bones-or-hosts` rule is present.

### 4. Solution coverage

Command:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-solution-coverage.ps1
```

Observed output (exit code `0`):

```text
solution=Wip.slnx
wip_project_count=46
solution_wip_project_count=46
missing_from_solution=0
solution_paths_not_on_disk=0
solution_coverage=ok
```

### 5. HarnessSync check

Command:

```text
dotnet run --project tools/HarnessSync -- --check
```

Observed: no stdout, exit code `0`.

### 6. Source hygiene

- 43 files under `WIP/Wip.Tholdem.Agents` and `WIP/Wip.Tholdem.Tests` (including the pre-existing round-1/2 test files) scanned: `BOM_OR_UTF16_FILES=0`.
- `NotImplementedException` / `TODO` in `WIP/Wip.Tholdem.Agents`: none.
- The only `Guid.NewGuid` in the new Tholdem trees is in `WIP/Wip.Tholdem.Tests/AgentTestSupport.cs` for test-only temp directory names, which the dispatch explicitly allows.
- `WIP/Wip.Tholdem.Agents` references only `Wip.Tholdem`; the architecture rule `tholdem-agents-no-bones-or-hosts` passes.
- The agent source scan (`Baselines/*.cs`, `IPokerAgent.cs`, `AgentTurnContext.cs`) is case-insensitive for standalone `TableState`, `HandRecord`, `HoleCardsSeat0`, `HoleCardsSeat1`, `Seed`, `Deck` and finds no offenders.

## Anything that could not be made to pass

1. **Test 7's literal `0.826366` for `As Ah` on an empty board.** The contracted `MonteCarloEquity.Estimate(ownHoleCards, board, seed, samples)` has no opponent-card input, so it models one uniformly random opponent hand. The `0.826366` reference (`docs/goal-tholdem/evidence.md` EVD-032, `.tmp/tholdem-verify/equity-reference.txt`) is the exhaustive equity of `As Ah` against the **fixed** hand `Ks Kh`; it is not the equity against a random hand. With seed 7 and 20,000 samples the random-opponent result is `Wins=16971, Ties=103, Losses=2926, Equity=0.851125`, which is what the test pins, together with an independent exhaustive turn-board enumeration (45,540 boards × opponent hands) required to be within 0.02 of the Monte Carlo result. Making the empty-board result equal `0.826366` within 0.02 would require either an opponent-card overload (hidden information, contract violation) or overriding the contract's random-opponent semantics. I did neither. This is the only dispatch check not satisfied literally.
2. **The required `return sessionSeed;` mutant does not fail the different-seed test.** By construction it still varies output with the session seed, so only the pinned literal, the 10,000-context distinctness test, the byte-identical reproducibility test and the zero-sum movement guard fail. The supplementary `return 0UL;` mutant proves the strengthened different-seed test does bite when the derivation is truly constant.
3. **`dotnet test` was not run.** The local VSTest testhost aborts with `Win32Exception (5)` (documented limitation); the compiled assembly was executed with the supplementary runner. CI was not triggered or observed from this session.
4. **The red output predates one test rename.** The different-seed fact was later renamed and strengthened (see above); the total count (76) and the red/green totals are unaffected. Re-skeletonising the implementation solely to re-capture the red run was judged not worth the risk of disturbing a verified green tree.
5. **Concurrent working-tree changes.** Another session committed to `WIP` during this round and briefly broke `dotnet build Wip.slnx` by deleting three Bones files; the final build after its commit is green. `tools/run-wip-tests.ps1` was already modified before this round and was not touched here.

No other required behavior failed. No stub, `TODO`, `NotImplementedException`, `System.Random`, clock or GUID exists in the delivered library code; no test was skipped, weakened or deleted; no file outside the stated scope was edited by this round.