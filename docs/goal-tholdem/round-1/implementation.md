# Round 1 — `Wip.Tholdem` cards, seeded deck, and hand evaluator

- **Date**: 2026-11-06
- **Branch**: `WIP`
- **Commit at round start**: `ba5debff480f44a1e4cda4095e4c8fc11cfeea68`
- **Scope delivered**: card model, deterministic 52-card seeded deck, 7-card hand evaluator, xUnit suite, and the five integration edits.
- **Out of scope and not created**: `Wip.Tholdem.Agents`, `Wip.Tholdem.Host`, `docs/tholdem/`.

## Files created

| Path | Purpose |
|---|---|
| `WIP/Wip.Tholdem/Wip.Tholdem.csproj` | net10.0 library, nullable enabled, `InternalsVisibleTo Wip.Tholdem.Tests` |
| `WIP/Wip.Tholdem/Cards/Rank.cs` | `Rank` enum (`Two = 2` … `Ace = 14`) |
| `WIP/Wip.Tholdem/Cards/Suit.cs` | `Suit` enum (`Clubs = 0` … `Spades = 3`) |
| `WIP/Wip.Tholdem/Cards/Card.cs` | `readonly record struct Card` with validated construction, `init` properties, `Deconstruct`, `Parse`, `ToString` |
| `WIP/Wip.Tholdem/Cards/Xorshift64Star.cs` | internal xorshift64* PRNG with the contract constants |
| `WIP/Wip.Tholdem/Cards/SeededDeck.cs` | canonical deck and exact Fisher-Yates shuffle |
| `WIP/Wip.Tholdem/Hands/HandCategory.cs` | `HandCategory` enum, values 0–8 |
| `WIP/Wip.Tholdem/Hands/HandRank.cs` | category, tiebreakers, exact packed `Score`, `CompareTo`, `ToString` |
| `WIP/Wip.Tholdem/Hands/HandEvaluator.cs` | 5/6/7-card evaluation, best-five selection, validation |
| `WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj` | xUnit test project mirroring `Wip.Runtime.Tests` package versions |
| `WIP/Wip.Tholdem.Tests/CardTests.cs` | parse/format and construction vectors |
| `WIP/Wip.Tholdem.Tests/SeededDeckTests.cs` | pinned seed-42 order, completeness, determinism |
| `WIP/Wip.Tholdem.Tests/HandEvaluatorTests.cs` | category, ordering, wheel, kicker, tie and validation vectors |
| `WIP/Wip.Tholdem.Tests/TestCards.cs` | test-only card-list parser |
| `docs/goal-tholdem/round-1/implementation.md` | this report |

## Files modified

| Path | Change |
|---|---|
| `Wip.slnx` | added `<Folder Name="/WIP/Tholdem/">` with the two projects; no existing entry changed |
| `tests/Wip.Architecture.Tests/ArchitectureReferenceTests.cs` | `43` → `45` |
| `tests/Wip.Architecture.Tests/ArchitectureRules.cs` | added `tholdem-no-bones` rule; existing rules unchanged |
| `docs/plan/test-waivers.json` | added `"Wip.Tholdem.Tests": 22` to `expectedTotals` |
| `tools/run-wip-tests.ps1` | added the Tholdem test project to `$projects` |

## Contract notes

- `Card` is a `readonly record struct` with the positional-record public surface: `Card(Rank, Suit)`, `Rank`/`Suit` init properties, value equality, and `Deconstruct`. An explicit validating constructor is used because C# forbids replacing a positional record's generated constructor (`CS0111`); validation therefore sits in the constructor and the `init` accessors (`with` remains supported).
- `ToString()` renders rank letters from `"23456789TJQKA"` and suit letters from `"cdhs"`. `HandRank.ToString()` expands multiplicity for pair/two-pair/trips/full-house/quads, so the required example is produced exactly: a full house with tiebreakers `[K, Q]` prints `FullHouse[K,K,K,Q,Q]`. Straight and straight-flush render their single significant high-card tiebreak, e.g. the wheel prints `Straight[5]`.
- `HandEvaluator` uses no LINQ or dictionary iteration; five-card combinations are visited in lexicographic index order and a candidate replaces the best only when it is strictly stronger. All arithmetic in `SeededDeck`/`Xorshift64Star` is `unchecked`.
- `HandRank` equality and hash code are score-based, so two independently evaluated equal hands are value-equal as well as having `CompareTo == 0`.
- The pinned seed-42 first five cards (`7d Qc 9c 4s 8c`) were independently re-derived with `.tmp/tholdem-verify/derive-deck.ps1` before implementation and are asserted as string literals.

## Test-first evidence (red)

The tests were written first against signature-only implementations whose bodies were temporarily `throw new NotImplementedException()`. Command:

```text
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

Observed output (complete):

```text
FAIL CardTests.Constructor_GivenUndefinedRankOrSuit_ExpectedArgumentException: ThrowsException: Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(System.ArgumentException)
Actual:   typeof(System.NotImplementedException)
FAIL CardTests.Parse_GivenCanonicalText_ExpectedRoundTrip: NotImplementedException: The method or operation is not implemented.
FAIL CardTests.Parse_GivenInvalidText_ExpectedArgumentException: ThrowsException: Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(System.ArgumentException)
Actual:   typeof(System.NotImplementedException)
FAIL CardTests.Parse_GivenNull_ExpectedArgumentException: ThrowsException: Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(System.ArgumentException)
Actual:   typeof(System.NotImplementedException)
FAIL HandEvaluatorTests.Evaluate_GivenAdjacentCategories_ExpectedStrongerScoreAndReverseComparison: NotImplementedException: The method or operation is not implemented.
FAIL HandEvaluatorTests.Evaluate_GivenBoardPlays_ExpectedIdenticalBestFive: NotImplementedException: The method or operation is not implemented.
FAIL HandEvaluatorTests.Evaluate_GivenFourOfAKind_ExpectedBestKickerUsed: NotImplementedException: The method or operation is not implemented.
FAIL HandEvaluatorTests.Evaluate_GivenNineKicker_ExpectedBeatsEightKicker: NotImplementedException: The method or operation is not implemented.
FAIL HandEvaluatorTests.Evaluate_GivenSameCardsInDifferentOrder_ExpectedEqualScore: NotImplementedException: The method or operation is not implemented.
FAIL HandEvaluatorTests.Evaluate_GivenSevenCards_ExpectedCategory: NotImplementedException: The method or operation is not implemented.
FAIL HandEvaluatorTests.Evaluate_GivenSixCards_ExpectedBestFiveSelected: NotImplementedException: The method or operation is not implemented.
FAIL HandEvaluatorTests.Evaluate_GivenSixHighStraight_ExpectedBeatsWheel: NotImplementedException: The method or operation is not implemented.
FAIL HandEvaluatorTests.Evaluate_GivenSixSuitedCards_ExpectedBestFiveFlush: NotImplementedException: The method or operation is not implemented.
FAIL HandEvaluatorTests.Evaluate_GivenTooFewTooManyOrDuplicateCards_ExpectedArgumentException: ThrowsException: Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(System.ArgumentException)
Actual:   typeof(System.NotImplementedException)
FAIL HandEvaluatorTests.Evaluate_GivenTwoTrips_ExpectedHigherTripsFullHouse: NotImplementedException: The method or operation is not implemented.
FAIL HandEvaluatorTests.Evaluate_GivenWheel_ExpectedFiveHighStraight: NotImplementedException: The method or operation is not implemented.
FAIL SeededDeckTests.Create_GivenDifferentSeeds_ExpectedDifferentSequences: NotImplementedException: The method or operation is not implemented.
FAIL SeededDeckTests.Create_GivenSameSeedTwice_ExpectedIdenticalSequence: NotImplementedException: The method or operation is not implemented.
FAIL SeededDeckTests.Create_GivenSeed42_ExpectedCompleteDeckWithAllDistinctCards: NotImplementedException: The method or operation is not implemented.
FAIL SeededDeckTests.Create_GivenSeed42_ExpectedPinnedFirstFiveCards: NotImplementedException: The method or operation is not implemented.
FAIL SeededDeckTests.Create_GivenSeed7_ExpectedCompleteDeckWithAllDistinctCards: NotImplementedException: The method or operation is not implemented.
FAIL SeededDeckTests.Create_GivenSeedZero_ExpectedDeterministicSequence: NotImplementedException: The method or operation is not implemented.
TOTAL 22 PASSED 0 FAILED 22 SKIPPED 0
```

Every delivered test failed at this point (22/22), confirming each test reaches the implementation body.

## Test-case count

**22 test cases**: 22 `[Fact]` methods, 0 `[Theory]`, 0 `[InlineData]`.

- `CardTests`: 4 facts
- `SeededDeckTests`: 6 facts
- `HandEvaluatorTests`: 12 facts
- `docs/plan/test-waivers.json` `expectedTotals["Wip.Tholdem.Tests"] = 22`

Why all facts: the fixed supplementary runner at `.tmp/tholdem-verify/RunXunit` reads `Xunit.InlineDataAttribute.Data`, but xunit 2.9.3 exposes `GetData` (observed metadata: `props=[Skip:String; TypeId:Object] methods=[GetData] fields=[data]`). Every `[Theory]` was therefore reported as `SKIP … no inline or member data resolved`. `.tmp` tooling is outside this round's scope, so the required vectors are expressed as `[Fact]` cases (an allowed attribute), which the runner executes in full and which keeps the count exactly stable.

## Verification

### 1. Full solution build

```text
dotnet build Wip.slnx --configuration Release
```

Observed (final run): exit code `0`.

```text
  Wip.Tholdem -> C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem\bin\Release\net10.0\Wip.Tholdem.dll
  Wip.Tholdem.Tests -> C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\bin\Release\net10.0\Wip.Tholdem.Tests.dll

Build succeeded.

    126 Warning(s)
    0 Error(s)

Time Elapsed 00:01:30.93
```

The 126 warnings are all pre-existing warnings in the other WIP projects (`Wip.Bones*`, `Wip.Shell*`, `Wip.Runtime`); a standalone Tholdem build reports `0 Warning(s), 0 Error(s)`.

### 2. Tholdem test suite

```text
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

Observed:

```text
TOTAL 22 PASSED 22 FAILED 0 SKIPPED 0
EXIT=0
```

### 3. Architecture tests

```text
dotnet build tests/Wip.Architecture.Tests -c Release
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- tests/Wip.Architecture.Tests/bin/Release/net10.0/Wip.Architecture.Tests.dll
```

Observed:

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)

TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0
RUN_EXIT=0
```

The four passing tests include `ProjectGraph_GivenWipSolution_ExpectedAllWipProjects` with the `45` assertion and `ProjectReferences_GivenForbiddenEdges_ExpectedNoUnwaivedViolations` with the new `tholdem-no-bones` rule.

### 4. Solution coverage

`pwsh` is not installed on this machine, so the equivalent Windows PowerShell invocation was used (per the orchestrator's environment correction):

```text
powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-solution-coverage.ps1
```

Observed:

```text
solution=Wip.slnx
wip_project_count=45
solution_wip_project_count=45
missing_from_solution=0
solution_paths_not_on_disk=0
solution_coverage=ok
EXIT=0
```

### 5. HarnessSync check

```text
dotnet run --project tools/HarnessSync -- --check
```

Observed: no stdout, exit code `0`.

```text
EXIT=0
```

### 6. Encoding check

Exact command from the dispatch:

```text
Get-ChildItem WIP/Wip.Tholdem,WIP/Wip.Tholdem.Tests -Recurse -Include *.cs,*.csproj | ForEach-Object { $b=[IO.File]::ReadAllBytes($_.FullName); '{0}: {1},{2},{3}' -f $_.Name,$b[0],$b[1],$b[2] }
```

Observed:

```text
Card.cs: 110,97,109
Rank.cs: 110,97,109
SeededDeck.cs: 110,97,109
Suit.cs: 110,97,109
Xorshift64Star.cs: 110,97,109
HandCategory.cs: 110,97,109
HandEvaluator.cs: 117,115,105
HandRank.cs: 117,115,105
.NETCoreApp,Version=v10.0.AssemblyAttributes.cs: 47,47,32
Wip.Tholdem.AssemblyInfo.cs: 47,47,45
Wip.Tholdem.GlobalUsings.g.cs: 47,47,32
Wip.Tholdem.csproj: 60,80,114
.NETCoreApp,Version=v10.0.AssemblyAttributes.cs: 47,47,32
Wip.Tholdem.Tests.AssemblyInfo.cs: 47,47,45
Wip.Tholdem.Tests.GlobalUsings.g.cs: 47,47,32
CardTests.cs: 117,115,105
HandEvaluatorTests.cs: 117,115,105
SeededDeckTests.cs: 117,115,105
TestCards.cs: 117,115,105
Wip.Tholdem.Tests.csproj: 60,80,114
```

No first bytes are `255,254`, `254,255`, or `239,187,191`; an explicit BOM scan over all source files reported `BOM_FILES=0`.

## Environment notes (not repository defects)

1. **`pwsh` is absent.** All PowerShell work used Windows PowerShell 5.1 (`powershell -NoProfile -ExecutionPolicy Bypass -File …`).
2. **Supplementary runner cannot execute xunit theories.** As explained above, xunit 2.9.3's `InlineDataAttribute` has no `Data` property. The runner is orchestrator-owned under `.tmp` and out of scope, so the suite is fact-based.
3. **`HarnessSync --check` initially crashed** with `System.UnauthorizedAccessException` against stale `.tmp/testhost-temp/wip-sandbox-*` directories. Those are empty scratch trees left by earlier testhost attempts. The tree was moved out of the repository to `C:\Users\ricar\AppData\Local\Temp\modus-testhost-temp-hold-20261106`; the check then exited `0`. This is the only write outside the round's file list, and it touched no repository content.
4. **One incremental solution-build retry.** The first post-integration build succeeded. A later incremental run transiently failed with `MSB3030` (`Wip.Bones.dll` not found during parallel copy) while the DLL was written seconds later by the same build; the retry below passed with `0 Error(s)`.
5. **`dotnet test` was not run**, per the dispatch's documented local VSTest failure (`Win32Exception (5)`); the supplementary runner is the local evidence path.

## Anything that could not be made to pass

None in the round-1 scope. All six verification commands above were observed to pass. The only unexecuted gate is the repository's `tools/verify-test-results.ps1` TRX/waiver gate, which requires `dotnet test` output; the waiver count was instead checked against the static `[Fact]` count (22 = 22) and the JSON was parse-validated.
