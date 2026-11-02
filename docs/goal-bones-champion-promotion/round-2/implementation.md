# Bones Champion Selector — Round 2 Implementation

Pure Bones champion selector implemented in two new files, proven by real xUnit facts executed through a reflection verifier. No `dotnet test` was run. No existing test or source file was edited.

Working directory for every command: `C:\Users\ricar\dev\Modus`.

## Exact commands

```powershell
dotnet build WIP/Wip.Bones.Agents/Wip.Bones.Agents.csproj --no-restore --nologo -v minimal -m:1 -nodeReuse:false
dotnet build WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj --no-restore --nologo -v minimal -m:1 -nodeReuse:false
$env:TEMP = 'C:\Users\ricar\dev\Modus\.tmp\champion-goal\temp'
$env:TMP  = 'C:\Users\ricar\dev\Modus\.tmp\champion-goal\temp'
dotnet run --project .tmp/champion-goal/verify-selector/VerifySelector.csproj -m:1 -nodeReuse:false
```

The two builds and the verifier run were executed three times: against the stub, after the first implementation body, and after the retained-ID fix. `C:\Users\ricar\dev\Modus\.tmp\champion-goal\temp` was created before the first verifier run.

## Test-first proof: stub with `NotImplementedException`

`BonesChampionSelector.Select` was written with `throw new NotImplementedException();`, both projects built successfully, and the verifier produced eight real invocation failures with exit code 1:

```
FAILED BonesChampionSelector_GivenCandidateBelowMinGames_ExpectedSkipped
  inner: System.NotImplementedException: The method or operation is not implemented.
FAILED BonesChampionSelector_GivenChallengerWinsSelectionAndConfirmation_ExpectedPromoted
  inner: System.NotImplementedException: The method or operation is not implemented.
FAILED BonesChampionSelector_GivenChallengerWinsSelectionButLosesConfirmation_ExpectedRetainsIncumbent
  inner: System.NotImplementedException: The method or operation is not implemented.
FAILED BonesChampionSelector_GivenNoIncumbent_ExpectedHighestLcbSelected
  inner: System.NotImplementedException: The method or operation is not implemented.
FAILED BonesChampionSelector_GivenObservedSeat1GapFixture_ExpectedV2PromotedOverV3
  inner: System.NotImplementedException: The method or operation is not implemented.
FAILED BonesChampionSelector_GivenOneWinInOneGameAndTwelveWinsInTwenty_ExpectedTwentyGameCandidateRanksHigher
  inner: System.NotImplementedException: The method or operation is not implemented.
FAILED BonesChampionSelector_GivenSameResultsInDifferentOrder_ExpectedSameSelection
  inner: System.NotImplementedException: The method or operation is not implemented.
FAILED BonesChampionSelector_GivenTiedOverallLcb_ExpectedTargetSeatTieBreak
  inner: System.NotImplementedException: The method or operation is not implemented.
FAIL 8
[exit=1]
```

## Implementation summary

- `WilsonLowerBound`: Wilson score interval lower bound with default `z = 1.96`; returns 0 for `games <= 0`; clamps `wins` to `0..games`; clamps the result at 0.
- `Select`: filters `Games >= minGames`, returns `Skipped` / `NoEligibleCandidates` when empty, ranks deterministically by overall LCB, target-seat LCB (seat games `Games / 4`, seat wins `round(rate * Games / 4.0)`), mean score differential, then ordinal `StrategyId` (no `System.HashCode`).
- Incumbent handling: no incumbent promotes the top eligible candidate; incumbent-ranked-first returns `Retained` / `IncumbentStillBest`; insufficient LCB margin returns `Retained` / `LcbBelowIncumbent`; missing confirmation returns `Retained` / `ConfirmationMissing`; failed confirmation returns `Retained` / `ConfirmationFailed`; otherwise `Promoted`.
- `Retained` outcomes always report the incumbent in both `StrategyId` and `IncumbentId`.

## Intermediate failing run (genuine defect caught by the facts)

After the first full body, the verifier showed one failure, exit code 1:

```
FAILED BonesChampionSelector_GivenChallengerWinsSelectionButLosesConfirmation_ExpectedRetainsIncumbent
  inner: Xunit.Sdk.EqualException: Assert.Equal() Failure: Strings differ
           ↓ (pos 0)
Expected: "incumbent-strategy"
Actual:   "challenger-strategy"
           ↑ (pos 0)
FAIL 1
[exit=1]
```

Cause: `ConfirmationFailed` (and the other `Retained` branches) returned the challenger as `StrategyId`. Fix: `Retained` branches now return `incumbentId` as `StrategyId`. No assertion was weakened or changed.

## Passing proof

After the fix, both builds succeeded (agents: 0 errors, 1 pre-existing CA2264 warning outside the new files; tests: 0 warnings, 0 errors) and the verifier printed:

```
ok BonesChampionSelector_GivenCandidateBelowMinGames_ExpectedSkipped
ok BonesChampionSelector_GivenChallengerWinsSelectionAndConfirmation_ExpectedPromoted
ok BonesChampionSelector_GivenChallengerWinsSelectionButLosesConfirmation_ExpectedRetainsIncumbent
ok BonesChampionSelector_GivenNoIncumbent_ExpectedHighestLcbSelected
ok BonesChampionSelector_GivenObservedSeat1GapFixture_ExpectedV2PromotedOverV3
ok BonesChampionSelector_GivenOneWinInOneGameAndTwelveWinsInTwenty_ExpectedTwentyGameCandidateRanksHigher
ok BonesChampionSelector_GivenSameResultsInDifferentOrder_ExpectedSameSelection
ok BonesChampionSelector_GivenTiedOverallLcb_ExpectedTargetSeatTieBreak
PASS 8
[exit=0]
```

The orchestrator independently reproduced `PASS 8` for the same eight facts with `TEMP`/`TMP` set to `C:\Users\ricar\dev\Modus\.tmp\champion-goal\temp`.

## Files changed

- `WIP/Wip.Bones.Agents/Champions/BonesChampionSelection.cs` (new): enum, two records, selector.
- `WIP/Wip.Bones.Tests/Champions/BonesChampionSelectorTests.cs` (new): eight facts in `Wip.Bones.Tests.Champions`.
- `.tmp/champion-goal/verify-selector/VerifySelector.csproj` (new): net10.0 console referencing `WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj`.
- `.tmp/champion-goal/verify-selector/Program.cs` (new): reflection over `typeof(Wip.Bones.Tests.Champions.BonesChampionSelectorTests)`, invokes every public instance method beginning `BonesChampionSelector_`, reports `PASS n` / `FAIL n`, exit 1 on failure.

Build outputs were regenerated under the affected projects `bin/` and `obj/` directories; no source or test outside the four files above was modified.

## Limitations

- The verifier invokes the xUnit fact methods directly by reflection. xUnit assertions and exceptions are real, but runner discovery, traits, and parallelization are not exercised.
- The confirmation stage applies the same `minGames >=` eligibility filter as the selection stage. The prompt did not specify this; it was chosen for consistency.
- If the incumbent is absent from the eligible selection results, its LCB is treated as 0. This path is not covered by the eight required facts.
- Only the eight required facts exist; no end-to-end wiring into the existing promotion pipeline was added or tested.
- `dotnet test` was intentionally not run, so no whole-suite regression claim is made.
- Pre-existing repository warnings (CA2264 in `Wip.Bones.Agents/Knowledge/BonesStrategyPool.cs`, xUnit1031 and xUnit2013 in older test files) were emitted by the builds; none originate from the new files.