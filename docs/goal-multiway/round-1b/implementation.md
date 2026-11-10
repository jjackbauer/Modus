# Round 1b — Per-seat starting stacks, reachable side pots, history v2 `startingStacks`

**Status:** complete for the objective; local supplementary suite green at **246 tests**
(241 before + 5 net new), zero failures. `dotnet test` was **not** run (prohibited by the task and
unusable in this session); the compiled test assembly was executed with the supplementary xUnit
runner `.tmp/tholdem-verify/RunXunit`. CI was not triggered or observed.

## 1. Scope and environment

- Repository `C:\Users\ricar\dev\Modus`, branch `WIP`.
- Changed only the allowed paths:
  `WIP/Wip.Tholdem/Engine/`, `WIP/Wip.Tholdem/History/`, `WIP/Wip.Tholdem.Tests/`,
  `docs/tholdem/engine-rules.md`, `docs/plan/test-waivers.json` and this file.
- No project was added or removed. `Wip.slnx` and the architecture count stay at **48**
  (`tests/Wip.Architecture.Tests/ArchitectureReferenceTests.cs:16` asserts `Assert.Equal(48,
  graph.Count)`).
- Scratch files are under `WIP/Wip.Tholdem.Tests/bin/Release/net10.0/scratch/`; `%TEMP%` was not used.

## 2. What changed

| File | Change |
|---|---|
| `WIP/Wip.Tholdem/Engine/HoldemHandEngine.cs` | trailing `IReadOnlyList<long>? startingStacks = null` on `Start`; per-seat validation and state initialisation; `Replay` passes `record.StartingStacks`; class/`ResolveShowdown` docs updated |
| `WIP/Wip.Tholdem/Engine/TableState.cs` | public `StartingStacks`; `StartingStack` documented as the first seat's compatibility projection; class conservation doc now `sum(Stacks) + Pot == sum(StartingStacks)` |
| `WIP/Wip.Tholdem/History/HandRecord.cs` | `StartingStacks` init member (defaults to the scalar repeated); `From` projects the final state's per-seat stacks |
| `WIP/Wip.Tholdem/History/HandRecordCanonicalWriter.cs` | v1 only for two seats with all-equal starting stacks; v2 gains `"startingStacks":[…]` after `"startingStack"` |
| `WIP/Wip.Tholdem/History/HandRecordJson.cs` | v2 accepts 2–6 seats and requires/validates `startingStacks`; parsed records carry it |
| `WIP/Wip.Tholdem.Tests/MultiwayTestSupport.cs` | deleted `CraftRiverState`/`SplitCards`; added independent `ExpectedFinalStacks` award re-computation |
| `WIP/Wip.Tholdem.Tests/MultiwayHoldemEngineTests.cs` | all side-pot tests rewritten through `Start(..., startingStacks: …)`; per-seat property test; three new `Start` validation/behaviour tests |
| `WIP/Wip.Tholdem.Tests/MultiwayHistoryTests.cs` | two new unequal-stack v2 round-trip/replay tests; v2 fold prefix updated for the new field |

`PlayerObservation` was **not** changed; starting stacks are not needed in the agent-facing
projection for this contract.

### API contract details

- `Start(seed, seats, handNumber = 1, startingStack = 1000, smallBlind = 5, bigBlind = 10,
  buttonSeat = 0, startingStacks = null)`.
  - `startingStacks == null` → every seat starts with `startingStack` (unchanged behaviour).
  - supplied → exactly `seats` values, each `>= bigBlind`; otherwise
    `ArgumentException` (count; message names the expected seat count) or
    `ArgumentOutOfRangeException` (value; message names the big blind). `startingStack` is ignored,
    including when it would otherwise be invalid (the test passes `startingStack: 7` with per-seat
    stacks).
- `TableState.StartingStacks` is the authoritative per-seat array. `TableState.StartingStack` is
  the first seat's value (identical to every seat in every pre-existing equal-stack case).
- `HandRecord.StartingStacks` defaults to `[StartingStack, StartingStack]`, so every v1 construction
  or parse site still works. `ToCanonicalJson()` picks v1 iff `SeatCount == 2` **and** all starting
  stacks are equal; otherwise v2. Consequently every existing two-seat hash and every recorded v1
  file is byte-identical (the pinned `c953464b…` hash and the 20-hand CLI manifest were re-observed
  below).
- `Replay` validates `StartingStacks.Count == SeatCount` and calls `Start(..., startingStacks)`, so a
  v2 two-seat unequal-stack hand and a 3–6 seat hand both replay.

## 3. Test-first evidence (red)

The rewritten tests were written first against the current engine. The test project then failed to
compile because `Start` had no `startingStacks` parameter and the state/record had no per-seat
property:

```text
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\MultiwayHoldemEngineTests.cs(327,13): error CS1739: The best overload for 'Start' does not have a parameter named 'startingStacks' [...]
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\MultiwayHoldemEngineTests.cs(352,13): error CS1739: The best overload for 'Start' does not have a parameter named 'startingStacks' [...]
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\MultiwayHoldemEngineTests.cs(359,13): error CS1739: The best overload for 'Start' does not have a parameter named 'startingStacks' [...]
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\MultiwayHoldemEngineTests.cs(375,13): error CS1739: The best overload for 'Start' does not have a parameter named 'startingStacks' [...]
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\MultiwayHoldemEngineTests.cs(583,42): error CS1501: No overload for method 'Start' takes 8 arguments [...]
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\MultiwayHistoryTests.cs(202,38): error CS1501: No overload for method 'Start' takes 8 arguments [...]
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\MultiwayHistoryTests.cs(248,38): error CS1501: No overload for method 'Start' takes 8 arguments [...]
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\MultiwayTestSupport.cs(162,34): error CS1061: 'TableState' does not contain a definition for 'StartingStacks' [...]
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\MultiwayHistoryTests.cs(220,60): error CS1061: 'HandRecord' does not contain a definition for 'StartingStacks' [...]
    0 Warning(s)
    20 Error(s)
build_exit=1
```

(Full log: `WIP/Wip.Tholdem.Tests/bin/Release/net10.0/scratch/round1b-red-build.txt`.)

## 4. Rewritten side-pot tests (all reached through `Start` + legal actions)

Every test below lives in `MultiwayHoldemEngineTests.cs`; none calls `ResolveShowdown`,
`CraftRiverState` or any state constructor. `StartWithStacks` is a local helper that calls
`HoldemHandEngine.Start(..., startingStacks: startingStacks)`.

1. `Start_GivenThreeSeatsWithStacks1000_600_300_ExpectedAllInLayers900_600_400` — seed 424242,
   stacks `[1000, 600, 300]`, button 0. Actions: seat 2 all-in (300), seat 0 all-in (1000), seat 1
   all-in (600). Asserts the total commitments `[1000, 600, 300]`, the exact layers
   `[900, 600, 400]`, the main-layer winners directly from `HandEvaluator` ranks over the seeded
   hole cards and board, every layer against `MultiwayTestSupport.BruteForcePots`, and final stacks
   against the independently recomputed awards.
2. `Start_GivenFoldedSmallBlindWithUnequalStacks_ExpectedDeadChipsStayAndSeatNeverWins` — seed 777,
   stacks `[1000, 600, 300]`. seat 2 all-in, seat 0 folds its 5-chip small blind, seat 1 all-in.
   Asserts totals `[5, 600, 300]`, layers `[15, 590, 300]`, the folded seat has 5 committed, is in
   no `Winners` array and wins no layer, and the 905-chip pot is fully awarded.
3. `Start_GivenFourSeatsAndHugeAllIn_ExpectedUncalledExcessReturnedInFinalStack` — seed 1600,
   stacks `[1000, 300, 200, 100]`. seat 2 all-in 200, seat 3 all-in 100, seat 0 all-in 1000, seat 1
   all-in 300. Asserts layers `[400, 300, 200, 700]`, the top 700-chip layer has only seat 0, and
   seat 0's final stack is `>= 700` (the uncalled excess is in the final stack, not silently lost).
4. `Start_GivenOddChipSplitAtMainPot_ExpectedOddChipToFirstEligibleClockwiseFromButton` — finds a
   seed (deterministically, seeds 1..1,000,000) whose three-seat board is four of a kind plus an
   ace, so the board plays for both remaining seats. Stacks `[1000, 600, 300]`; seat 2 all-in,
   seat 0 folds its 5, seat 1 all-in. Layers `[15, 590, 300]`: the 15-chip main layer splits 8/7
   with the odd chip to seat 1 (first eligible clockwise from button 0), the 590 layer splits
   295/295, and the 300 top layer returns to seat 1. Asserts the exact final stacks
   `[995, 603, 302]`.
5. `Start_GivenSixSeatsWithTwoAllInLevels_ExpectedLayeredAwards` — seed 4100, stacks
   `[1000, 1000, 600, 600, 600, 600]`. Seats 2–5 all-in for 600, then seats 0 and 1 all-in for 1000.
   Asserts totals `[1000, 1000, 600, 600, 600, 600]`, two exact layers `[3600, 800]`, awarded pot
   4400, and final stacks against the independent recomputation.
6. `Start_GivenConsecutiveLayersWithSameWinners_ExpectedSeparatePotAwards` — seed 1010,
   stacks `[1000, 600, 300]`, button 2. Seat 1 calls 10, button/SB seat 2 folds its 5, seat 0
   checks, then both check down flop/turn/river. Totals `[10, 10, 5]` produce layers `[15, 10]`
   with the same winner set and are deliberately not merged.
7. `Start_GivenThreeSeatsAndFoldedSeat_ExpectedBoardPlaysForRemainingSeatsAndFoldedSeatNeverWins` —
   the same board-plays seed, stacks `[1000, 1000, 1000]`. Seat 2 all-in, seat 0 all-in, seat 1
   folds its 10-chip big blind. Asserts the board's rank equals both remaining seats' best rank,
   layers `[30, 1980]` both won by seats 0 and 2, seat 1 never wins, and final stacks
   `[1005, 990, 1005]`.

### Per-seat property test

`Apply_GivenThreeHundredRandomMultiwayHandsWithPerSeatStacks_ExpectedConservationTerminationAndMatchingBruteForcePots`
plays 300 deterministic hands over 2–6 seats with deterministic per-seat stacks (100..900). After
**every** action it asserts `sum(Stacks) + Pot == sum(StartingStacks)`; at completion it asserts
termination, `Pot == 0`, `sum(Stacks) == sum(StartingStacks)`, `sum(Pots) == AwardedPot`, equality
with `BruteForcePots` (layer amounts and winners), the per-seat award formula, and
`ExpectedFinalStacks`. It also requires that at least one hand produced layered pots and at least
one ended by fold.

### `Start` tests

- `Start_GivenPerSeatStartingStacks_ExpectedPerSeatStacksAndScalarIgnored` — `startingStack: 7`
  is ignored when `[1000, 600, 300]` is supplied; exposes `StartingStacks` and the first-seat
  `StartingStack`.
- `Start_GivenNullStartingStacks_ExpectedEverySeatToUseScalarStartingStack` — four equal stacks.
- `Start_GivenInvalidStartingStacks_ExpectedSpecificArgumentException` — wrong count and a value
  below the big blind, including `ParamName` and reason text.

## 5. Mutation evidence

Mutation applied to `ResolveShowdown`'s layer arithmetic: each layer amount was multiplied by
`state.SeatCount` instead of the number of contributors (`var amount = (level - previous) *
state.SeatCount;`). Forced rebuild, then the multiway engine class:

```text
FAIL MultiwayHoldemEngineTests.Start_GivenThreeSeatsWithStacks1000_600_300_ExpectedAllInLayers900_600_400: EqualException: Assert.Equal() Failure: Collections differ
Expected: [900, 600, 400]
Actual:   [900, 900, 1200]
FAIL MultiwayHoldemEngineTests.Start_GivenFoldedSmallBlindWithUnequalStacks_ExpectedDeadChipsStayAndSeatNeverWins: EqualException: Assert.Equal() Failure: Collections differ
Expected: [15, 590, 300]
Actual:   [15, 885, 900]
FAIL MultiwayHoldemEngineTests.Start_GivenFourSeatsAndHugeAllIn_ExpectedUncalledExcessReturnedInFinalStack: EqualException: Assert.Equal() Failure: Collections differ
Expected: [400, 300, 200, 700]
Actual:   [400, 400, 400, 2800]
FAIL MultiwayHoldemEngineTests.Start_GivenOddChipSplitAtMainPot_ExpectedOddChipToFirstEligibleClockwiseFromButton: EqualException: Assert.Equal() Failure: Collections differ
Expected: [15, 590, 300]
Actual:   [15, 885, 900]
FAIL MultiwayHoldemEngineTests.Start_GivenSixSeatsWithTwoAllInLevels_ExpectedLayeredAwards: EqualException: Assert.Equal() Failure: Collections differ
Expected: [3600, 800]
Actual:   [3600, 2400]
FAIL MultiwayHoldemEngineTests.Start_GivenThreeSeatsAndFoldedSeat_ExpectedBoardPlaysForRemainingSeatsAndFoldedSeatNeverWins: EqualException: Assert.Equal() Failure: Collections differ
Expected: [30, 1980]
Actual:   [30, 2970]
FAIL MultiwayHoldemEngineTests.Start_GivenConsecutiveLayersWithSameWinners_ExpectedSeparatePotAwards: EqualException: Assert.Equal() Failure: Collections differ
Expected: [15, 10]
Actual:   [15, 15]
FAIL MultiwayHoldemEngineTests.Apply_GivenThreeHundredRandomMultiwayHandsWithPerSeatStacks_ExpectedConservationTerminationAndMatchingBruteForcePots: EqualException: Assert.Equal() Failure: Values differ
Expected: 500
Actual:   570
TOTAL 39 PASSED 31 FAILED 8 SKIPPED 0
```

Eight side-pot/property facts failed (at least two required). The original file was saved before the
mutation and restored with a hash check:

```text
restored_sha256=53e9f85a4497215f62a01fa7e343be160e5a12ed33f14a803e5514212bb2ebac
matches_original=True
```

Then a forced `--no-incremental` rebuild and the full suite:

```text
Build succeeded.
    2 Warning(s)
    0 Error(s)
restored_build_exit=0
TOTAL 246 PASSED 246 FAILED 0 SKIPPED 0
restored_run_exit=0
```

(The two warnings are pre-existing `Wip.Runtime` warnings emitted when the forced rebuild also
rebuilds its dependencies; no Tholdem project warned.)

## 6. Verification (observed output)

All commands were run from the repository root. `dotnet test` was **not** run.

### 6.1 Test project build

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --nologo
```

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### 6.2 Full Tholdem suite (supplementary runner)

```powershell
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

```text
TOTAL 246 PASSED 246 FAILED 0 SKIPPED 0
runner_exit=0
```

The final re-run after the last test edit and the final solution build was identical
(`round1b-final2-full-run.txt`).

**Exact test count: 246** (241 pre-existing + 5 net new: three `Start` tests and two history tests;
the seven side-pot tests were rewritten in place, not added). The pinned two-seat canonical hash
`c953464b065b991bcda5c550c3ff5167b7fef6ccbd417636e822cf1c58a842c5` is still green, as is the sweep
over all recorded `artifacts/tholdem/sessions` v1 hand files.

### 6.3 Solution build

```powershell
dotnet build Wip.slnx --configuration Release
```

```text
    6 Warning(s)
    0 Error(s)

Time Elapsed 00:00:32.51
final_solution_build_exit=0
```

This was the final build of the exact delivered tree; the first full (non-incremental) solution build
observed the same result with 125 pre-existing warnings and 0 errors (`solution_build_exit=0`). The
warnings are pre-existing solution warnings; no Tholdem project produced one.

### 6.4 Architecture

```powershell
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- tests/Wip.Architecture.Tests/bin/Release/net10.0/Wip.Architecture.Tests.dll
```

```text
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0
architecture_exit=0
```

`ArchitectureReferenceTests.cs:16` asserts the graph has exactly 48 projects.

### 6.5 Solution coverage

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-solution-coverage.ps1
```

```text
solution=Wip.slnx
wip_project_count=48
solution_wip_project_count=48
missing_from_solution=0
solution_paths_not_on_disk=0
solution_coverage=ok
coverage_exit=0
```

### 6.6 HarnessSync

```powershell
dotnet run --project tools/HarnessSync -- --check
```

```text
harnesssync_exit=0
```

(no output; all text files remain UTF-8 without BOM)

### 6.7 Two-seat CLI oracle (byte-identical)

```powershell
WIP\Wip.Tholdem.Host\bin\Release\net10.0\Wip.Tholdem.Host.exe play --seed 42 --hands 20 --allow-weak-seed --data-dir <scratch>
```

```text
session: tholdem-06795fbeacfe528b
seed: 42  hands: 20  manifest sha256: 87f90413e929d99fff9c833f9768cda3da22e4939e2d3224438d8d477538e618
agent                    hands    bb/100       95% CI (bb/100)  win rate     95% CI (win rate)  aggression  showdown
tight-aggressive            20  -1883.00   -3511.94 .. -254.06      0.15          0.05 .. 0.36        0.10      0.20
random-legal                20   1883.00     254.06 .. 3511.94      0.85          0.64 .. 0.95        3.25      0.20
cli_exit=0
```

Session and manifest hash match the pre-change oracle exactly.

### 6.8 New three-seat unequal-stack v2 replay

A scratch probe (removed afterwards so the project count stays 48) played `Start(424242, 3, …,
startingStacks: [1000, 600, 300])` plus three all-ins and wrote the canonical v2 file
`scratch/round1b-3seat-unequal-hand.json` (hash
`db4fb14ac0abb151791fcdc085c8ea178002310fde74f6efb083289dd607af4d`):

```json
{"schema":"tholdem.hand.v2","handNumber":1,"seed":424242,"smallBlind":5,"bigBlind":10,"startingStack":1000,"startingStacks":[1000,600,300],"buttonSeat":0,"seats":3,"holeCards":[["9c","Td"],["6h","7c"],["5d","4s"]],"board":["Th","Kh","8d","4d","8c"],"actions":[{"street":0,"seat":2,"kind":"AllIn","amount":0,"potAfter":315},{"street":0,"seat":0,"kind":"AllIn","amount":0,"potAfter":1310},{"street":0,"seat":1,"kind":"AllIn","amount":0,"potAfter":0}],"pots":[{"amount":900,"winners":[0]},{"amount":600,"winners":[0]},{"amount":400,"winners":[0]}],"showdown":true,"winners":[0],"potAwarded":1900,"finalStacks":[1900,0,0]}
```

```powershell
WIP\Wip.Tholdem.Host\bin\Release\net10.0\Wip.Tholdem.Host.exe replay <scratch>\round1b-3seat-unequal-hand.json
```

```text
hand: 1
board: Th Kh 8d 4d 8c
seat 0: 9c Td
seat 1: 6h 7c
actions:
  preflop seat=2 kind=AllIn amount=0 potAfter=315
  preflop seat=0 kind=AllIn amount=0 potAfter=1310
  preflop seat=1 kind=AllIn amount=0 potAfter=0
showdown: true
winners: 0
pot awarded: 1900
final stacks: 1900 / 0 / 0
replayed stacks: 1900 / 0 / 0  pot: 0
integrity: unanchored
replay_exit=0
```

### 6.9 Grep proof

```text
MultiwayHoldemEngineTests.cs:366: private static TableState StartWithStacks(ulong seed, long[] startingStacks, int buttonSeat = 0)
MultiwayHoldemEngineTests.cs:375:             startingStacks: startingStacks);
MultiwayHoldemEngineTests.cs:383:         var state = StartWithStacks(424242, [1000, 600, 300]);
MultiwayHoldemEngineTests.cs:426:         var state = StartWithStacks(777, [1000, 600, 300]);
MultiwayHoldemEngineTests.cs:451:         var state = StartWithStacks(1600, [1000, 300, 200, 100]);
MultiwayHoldemEngineTests.cs:478:         var state = StartWithStacks(seed, [1000, 600, 300]);
MultiwayHoldemEngineTests.cs:501:         var state = StartWithStacks(4100, [1000, 1000, 600, 600, 600, 600]);
MultiwayHoldemEngineTests.cs:525:         var state = StartWithStacks(1010, [1000, 600, 300], buttonSeat: 2);
MultiwayHoldemEngineTests.cs:721:         var state = StartWithStacks(seed, [1000, 1000, 1000]);
--- negative check across tests ---
no ResolveShowdown or CraftRiverState references in Wip.Tholdem.Tests
```

The helper at line 366 is the only path the side-pot tests use to start a hand; it calls
`HoldemHandEngine.Start(..., startingStacks: startingStacks)` at line 375. There is no
`ResolveShowdown` call and no crafted-state helper left in the test project.

## 7. Existing tests changed and why

| Test | Change | Justification |
|---|---|---|
| `MultiwayHoldemEngineTests` side-pot section (7 tests) | replaced `ResolveShowdown`-on-crafted-state tests with `Start(..., startingStacks: …)` + action scripts | required by the objective; the old route could not be reached legally, and the new tests assert the same or stronger facts (exact layer amounts, folded dead money, uncalled excess, odd chip, six seats) |
| `MultiwayHoldemEngineTests.Apply_GivenThreeHundredRandomMultiwayHands…` | renamed and now uses deterministic per-seat stacks with `sum(StartingStacks)` conservation | required by the objective; this is a strengthening (per-seat stacks plus the existing brute-force and award assertions) |
| `MultiwayHistoryTests.From_GivenFourSeatFold_ExpectedV2RoundTripAndReplay` | v2 prefix now includes `"startingStacks":[1000,1000,1000,1000]` | deliberate v2 schema addition; the field is required by the contract and no v1 bytes change |
| `MultiwayHistoryTests.From_GivenTwoSeatHand_ExpectedV1CanonicalJsonAndBaselineHashUnchanged` | added `DoesNotContain("\"startingStacks\"")` | strengthening only; the v1 hash assertion is unchanged |
| `MultiwayHistoryTests` | added two unequal-stack v2 tests | new coverage for `startingStacks` round-trip and `Replay` |
| `MultiwayTestSupport` | removed `CraftRiverState`/`SplitCards`; added `ExpectedFinalStacks` | the crafted route is unused; no test synthesizes a table state |

No assertion was weakened, skipped or deleted. The only changed expected literal is the v2 field
order in the 4-seat fold test, caused by the required new field.

## 8. Public-API reachability / limits

- **All required side-pot facts were reached through the public API.** The 3-seat 1000/600/300
  all-in, the folded-dead-money hand, the 4-seat uncalled excess, the odd-chip split and the 6-seat
  two-level hand are all `Start(seed, seats, …, startingStacks)` plus scripted `Apply` calls.
- `ResolveShowdown` is still `internal` because it is product code called by `RunOutBoard`; it is
  not a test seam any more. No test calls it.
- The old `CraftRiverState` helper was deleted; no remaining side-pot state needs it. The test-only
  scratch probe used to emit the v2 replay file was also removed after use, keeping the repository
  project count at 48.
- **Not verified:** `dotnet test` (prohibited and unusable here), CI (cannot be triggered or
  observed from this machine), and any behaviour outside the listed paths. The supplementary runner
  is local evidence, not a replacement for CI.
- The two-seat CLI oracle and all recorded v1 hashes are unchanged; the equal-stack code path is
  untouched apart from the new `startingStacks is null` branch that fills the same array.

## 9. Test count bookkeeping

`docs/plan/test-waivers.json` records `"Wip.Tholdem.Tests": 246` (was `241`). The other project
totals are untouched.

Artifacts (all under `WIP/Wip.Tholdem.Tests/bin/Release/net10.0/scratch/`):
`round1b-red-build.txt`, `round1b-green-build.txt`, `round1b-full-run.txt`,
`round1b-final-test-build.txt`, `round1b-final-full-run.txt`,
`round1b-mutation-build.txt`, `round1b-mutation-run.txt`, `round1b-restored-build.txt`,
`round1b-restored-run.txt`, `round1b-cli-oracle.txt`, `round1b-v2-replay.txt`,
`round1b-solution-build.txt`, `round1b-final-solution-build.txt`, `round1b-architecture.txt`,
`round1b-final-architecture.txt`, `round1b-coverage.txt`, `round1b-final-coverage.txt`,
`round1b-harnesssync.txt`, `round1b-harnesssync-final.txt`, `round1b-final2-harnesssync.txt`,
`round1b-final2-full-run.txt`, `round1b-grep-startwithstacks.txt`,
`round1b-final-grep-startwithstacks.txt`, `round1b-3seat-unequal-hand.json`.
