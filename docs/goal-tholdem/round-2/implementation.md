# Round 2 — Heads-up no-limit hand engine, canonical history, and observation boundary

- **Date**: 2026-11-06
- **Branch**: `WIP`
- **HEAD at verification**: `dad2a72c12e8ed41e92d633652c35172e8b532a1`
- **Scope delivered**: immutable `TableState`, `PokerAction` / `LegalActionSet` / legality engine, heads-up blind and street order, min-raise and short all-in rules, showdown / split pot / fold award, chip conservation, `HandRecord` canonical JSON + SHA-256 + replay, and the hidden-information-safe `PlayerObservation`.
- **Not created**: `Wip.Tholdem.Agents`, `Wip.Tholdem.Host`, any new project, or any change to `Wip.slnx` / the architecture tests / `tools/run-wip-tests.ps1`.

## Files created

| Path | Purpose |
|---|---|
| `WIP/Wip.Tholdem/Engine/Street.cs` | `Street` enum (Preflop 0 … Complete 5) |
| `WIP/Wip.Tholdem/Engine/ActionKind.cs` | `ActionKind` enum |
| `WIP/Wip.Tholdem/Engine/PokerAction.cs` | `readonly record struct` with the six factories; bet/raise carry the total street commitment after the action |
| `WIP/Wip.Tholdem/Engine/IllegalActionException.cs` | `InvalidOperationException` subclass with the stable `Reason` token |
| `WIP/Wip.Tholdem/Engine/LegalActionSet.cs` | legality projection with booleans, call/min/max, and the canonical action menu |
| `WIP/Wip.Tholdem/Engine/TableState.cs` | immutable record; public read-only projection; hole cards, seed, deck and deck position are `internal`; internal `ActionLogEntry` |
| `WIP/Wip.Tholdem/Engine/HeadsUpHandEngine.cs` | `Start`, `GetLegalActions`, `Apply`, `Replay`, internal `SplitPot`, dealing / street / showdown / award |
| `WIP/Wip.Tholdem/History/HandRecord.cs` | `HandRecord`, `HandActionRecord`, `From`, `ToCanonicalJson`, `Hash` |
| `WIP/Wip.Tholdem/History/HandRecordCanonicalWriter.cs` | fixed-order, whitespace-free, culture-free JSON writer |
| `WIP/Wip.Tholdem/History/PlayerObservation.cs` | per-seat observation and its canonical JSON; no `HandRecord` reference and no hidden-card object |
| `WIP/Wip.Tholdem.Tests/EngineTestSupport.cs` | deterministic test helpers (check-down, all-in, random legal policy, seven-card assembly) |
| `WIP/Wip.Tholdem.Tests/HeadsUpHandEngineTests.cs` | 24 engine facts |
| `WIP/Wip.Tholdem.Tests/HandRecordTests.cs` | 3 history facts |
| `WIP/Wip.Tholdem.Tests/PlayerObservationTests.cs` | 3 observation facts |
| `docs/goal-tholdem/round-2/implementation.md` | this report |

## Files modified

| Path | Change |
|---|---|
| `WIP/Wip.Tholdem.Tests/HandEvaluatorTests.cs` | TODO-002 strengthening: the two tie tests now assert `Tiebreakers == [14,13,12,11,9]`, `HandCategory.HighCard`, and the exact `Score == 974009` for both hands |
| `docs/plan/test-waivers.json` | `expectedTotals["Wip.Tholdem.Tests"]` 22 → 52 |

No file under `WIP/Wip.Tholdem/Cards/` or `WIP/Wip.Tholdem/Hands/` was changed.

## Implementation notes

- `Start(42)` posts the 5/10 blinds, gives stacks `[995, 990]`, street commitments `[5, 10]`, pot `15`, `CurrentBet == 10`, `LastRaiseSize == 10`, and the button acting first. The action menu preflop is Fold, Call, Raise-to-min, AllIn.
- The big blind gets the option after a small-blind limp: `Call` (seat 0) → BB has `CallAmount == 0`, `CanCheck == true`, `CanRaise == true`, `CanBet == false`; `Check` → flop, pot 20, non-button acts first.
- A raise that meets the minimum resets `LastRaiseSize` to `newCurrentBet - oldCurrentBet`; a short all-in changes `CurrentBet` but leaves `LastRaiseSize`. `Bet` is only legal at `CurrentBet == 0`; the big-blind option uses `Raise`. Min bet postflop is the big blind.
- Uncalled chips are returned only when provably uncalled: both players all-in, or the shorter contributor is all-in. This keeps the showdown contested for an exactly matched pot and preserves chip conservation.
- The action log records `PotAfter` after the action and any immediate refund/showdown award; replay verifies street, seat, `PotAfter`, the non-bet amount rule, the final board, the private hole cards, stacks, winners, showdown flag and awarded pot.
- Canonical JSON order: `handNumber, seed, smallBlind, bigBlind, startingStack, buttonSeat, holeCardsSeat0, holeCardsSeat1, board, actions[{street,seat,kind,amount,potAfter}], showdown, winners, potAwarded, finalStacks`. Numbers use `CultureInfo.InvariantCulture`; the hash is lowercase SHA-256 of the UTF-8 bytes and never appears in the document.
- `PlayerObservation.ForSeat` copies the board, own hole cards and winners into fresh arrays and exposes exactly the contracted 17 properties.
- Stable illegal-action tokens: `raise-below-min`, `raise-above-stack`, `fold-not-facing-bet`, `check-facing-bet`, `call-not-facing-bet`, `hand-complete`, `amount-not-allowed`, `bet-facing-bet`, `raise-not-facing-bet`, `all-in-not-available`, `invalid-state`, `unknown-action`.

## Test-first evidence (red)

The tests were written first against signature-only members whose bodies were temporarily `NotImplementedException`. Command:

```text
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

Observed output (complete):

```text
FAIL HandRecordTests.From_GivenCompleteHand_ExpectedFieldsMatchFinalState: NotImplementedException: The method or operation is not implemented.
FAIL HandRecordTests.From_GivenIncompleteState_ExpectedInvalidOperationException: NotImplementedException: The method or operation is not implemented.
FAIL HandRecordTests.ToCanonicalJson_GivenRecord_ExpectedStableWhitespaceFreeJsonAndIndependentHash: NotImplementedException: The method or operation is not implemented.
FAIL HeadsUpHandEngineTests.Apply_GivenAction_ExpectedInputStateUnchanged: NotImplementedException: The method or operation is not implemented.
FAIL HeadsUpHandEngineTests.Apply_GivenBoardPlays_ExpectedSplitPotAndTwoWinners: NotImplementedException: The method or operation is not implemented.
FAIL HeadsUpHandEngineTests.Apply_GivenBothAllInPreflop_ExpectedFullBoardAndShowdown: NotImplementedException: The method or operation is not implemented.
FAIL HeadsUpHandEngineTests.Apply_GivenCallWhenNothingToCall_ExpectedCallNotFacingBet: NotImplementedException: The method or operation is not implemented.
FAIL HeadsUpHandEngineTests.Apply_GivenCheckCheckFlop_ExpectedTurnWithFourBoardCards: NotImplementedException: The method or operation is not implemented.
FAIL HeadsUpHandEngineTests.Apply_GivenCheckCheckRiver_ExpectedShowdownComplete: NotImplementedException: The method or operation is not implemented.
FAIL HeadsUpHandEngineTests.Apply_GivenCheckCheckTurn_ExpectedRiverWithFiveBoardCards: NotImplementedException: The method or operation is not implemented.
FAIL HeadsUpHandEngineTests.Apply_GivenCompleteHand_ExpectedHandComplete: NotImplementedException: The method or operation is not implemented.
FAIL HeadsUpHandEngineTests.Apply_GivenFiveHundredSeedsAndRandomPolicy_ExpectedChipConservationAndCompletion: NotImplementedException: The method or operation is not implemented.
FAIL HeadsUpHandEngineTests.Apply_GivenFoldFacingBet_ExpectedOpponentWinsWithoutShowdown: NotImplementedException: The method or operation is not implemented.
FAIL HeadsUpHandEngineTests.Apply_GivenFoldWhenCheckingIsLegal_ExpectedFoldNotFacingBet: NotImplementedException: The method or operation is not implemented.
FAIL HeadsUpHandEngineTests.Apply_GivenNonZeroAmountOnNonBettingAction_ExpectedAmountNotAllowed: NotImplementedException: The method or operation is not implemented.
FAIL HeadsUpHandEngineTests.Apply_GivenPostflopBets_ExpectedBigBlindMinimumAndStackCap: NotImplementedException: The method or operation is not implemented.
FAIL HeadsUpHandEngineTests.Apply_GivenRaiseBelowMinimum_ExpectedRaiseBelowMin: NotImplementedException: The method or operation is not implemented.
FAIL HeadsUpHandEngineTests.Apply_GivenRaiseOfThirtyPreflop_ExpectedNextMinimumFifty: NotImplementedException: The method or operation is not implemented.
FAIL HeadsUpHandEngineTests.Apply_GivenSameSeedAndPolicy_ExpectedIdenticalJsonAndHashAndDifferentSeedDiffers: NotImplementedException: The method or operation is not implemented.
FAIL HeadsUpHandEngineTests.Apply_GivenSeatZeroStrongerHand_ExpectedSeatZeroWinsPot: NotImplementedException: The method or operation is not implemented.
FAIL HeadsUpHandEngineTests.Apply_GivenSmallBlindCallAndBigBlindCheck_ExpectedFlopAndPotTwenty: NotImplementedException: The method or operation is not implemented.
FAIL HeadsUpHandEngineTests.Apply_GivenStackBelowMinimumRaise_ExpectedAllInLegalAndRaiseRejected: NotImplementedException: The method or operation is not implemented.
FAIL HeadsUpHandEngineTests.GetLegalActions_GivenSmallBlindFacingBigBlind_ExpectedFoldCallRaiseAllIn: NotImplementedException: The method or operation is not implemented.
FAIL HeadsUpHandEngineTests.Replay_GivenRecord_ExpectedSameFinalStacksWinnersAndHash: NotImplementedException: The method or operation is not implemented.
FAIL HeadsUpHandEngineTests.Replay_GivenTamperedAction_ExpectedInvalidOperationException: NotImplementedException: The method or operation is not implemented.
FAIL HeadsUpHandEngineTests.SplitPot_GivenOddPot_ExpectedExtraChipToNonButton: NotImplementedException: The method or operation is not implemented.
FAIL HeadsUpHandEngineTests.Start_GivenDefaultSetup_ExpectedBlindsHoleCardsAndConservation: NotImplementedException: The method or operation is not implemented.
FAIL PlayerObservationTests.ForSeat_GivenActingSeat_ExpectedLegalActionKindsMatchEngine: NotImplementedException: The method or operation is not implemented.
FAIL PlayerObservationTests.ForSeat_GivenHiddenInformation_ExpectedNoSeedDeckOrOpponentHoleCards: NotImplementedException: The method or operation is not implemented.
TOTAL 52 PASSED 23 FAILED 29 SKIPPED 0
```

29 of the 30 new facts failed. The 23 passes were the 22 pre-existing round-1 facts plus `PlayerObservationTests.ForSeat_GivenPublicSurface_...`, which is reflection-only and does not call a skeleton.

## Test-case count

**52 test cases**: 52 `[Fact]` methods, 0 `[Theory]`, 0 `[InlineData]`.

| Suite | Facts |
|---|---|
| `CardTests` | 4 |
| `SeededDeckTests` | 6 |
| `HandEvaluatorTests` | 12 |
| `HeadsUpHandEngineTests` | 24 |
| `HandRecordTests` | 3 |
| `PlayerObservationTests` | 3 |
| **Total** | **52** |

`docs/plan/test-waivers.json` parses and `expectedTotals["Wip.Tholdem.Tests"] == 52`.

## Mutation experiment

After the green run, `HeadsUpHandEngine.ResolveShowdown` was temporarily replaced with a stub that always awards the whole pot and the winner list to seat 0:

```csharp
        _ = seat0Rank;
        _ = seat1Rank;
        stacks[0] += pot;
        winners = new[] { 0 };
```

- Original file SHA-256: `0744D1E75589C9F4B4F7EFCE9B43CCB2C341EBBAE08634EB3167E98E44B2C375`
- Mutated file SHA-256: `37AD5B2AAF6F697BB73C0B1D3FC479B98863B941E34B8F56BD251F90BC3827D8`
- Build: `Build succeeded. 0 Warning(s) 0 Error(s)`
- Observed mutant run:

```text
FAIL HeadsUpHandEngineTests.Apply_GivenBoardPlays_ExpectedSplitPotAndTwoWinners: EqualException: Assert.Equal() Failure: Collections differ
              ↓ (pos 1)
Expected: [0, 1]
Actual:   [0]
TOTAL 52 PASSED 51 FAILED 1 SKIPPED 0
```

- Restore: `RESTORED_SHA256=0744D1E75589C9F4B4F7EFCE9B43CCB2C341EBBAE08634EB3167E98E44B2C375`, `RESTORED_MATCHES_ORIGINAL=True`, `MUTANT_MARKER_ABSENT`, `SPLIT_BRANCH_RESTORED`, `REPLAY_AMOUNT_CHECK_PRESENT`.
- Forced rebuild: `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --no-incremental` → `Build succeeded. 0 Warning(s) 0 Error(s)`.
- Restored green run:

```text
TOTAL 52 PASSED 52 FAILED 0 SKIPPED 0
```

The mutant was never committed and no mutated file remains in the repository. The restore backup (which holds the original content) is under `%TEMP%\tholdem-mutation-final`; the repository file is the restored hash above.

## Verification (final observed runs)

### 1. Full solution build

```text
dotnet build Wip.slnx --configuration Release
```

Observed: exit code `0`.

```text
Build succeeded.
    126 Warning(s)
    0 Error(s)

Time Elapsed 00:01:26.44
```

The 126 warnings are pre-existing warnings in other WIP projects. The Tholdem projects alone build with `0 Warning(s), 0 Error(s)`.

### 2. Tholdem suite

```text
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

Observed: `TOTAL 52 PASSED 52 FAILED 0 SKIPPED 0`, exit code `0`.

### 3. Architecture suite

```text
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- tests/Wip.Architecture.Tests/bin/Release/net10.0/Wip.Architecture.Tests.dll
```

Observed: `TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0`, exit code `0`.

### 4. Solution coverage

```text
powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-solution-coverage.ps1
```

Observed: exit code `0`.

```text
solution=Wip.slnx
wip_project_count=45
solution_wip_project_count=45
missing_from_solution=0
solution_paths_not_on_disk=0
solution_coverage=ok
```

### 5. HarnessSync check

```text
dotnet run --project tools/HarnessSync -- --check
```

Observed: no stdout, exit code `0`.

### 6. Source audit

```text
SOURCE_FILES=18
BOM_OR_UTF16_FILES=0
FORBIDDEN_PATTERN_HITS=0
```

The forbidden-pattern scan covered `NotImplementedException`, `TODO`, `System.Random`, `DateTime.`, and `Guid.NewGuid`. `PUBLIC_HIDDEN_LEAKS` over `TableState.cs` (`public … Seed|Deck|HoleCards`) printed `none`.

## Anything that could not be made to pass

1. **Literal initial stacks `[1000,1000]`.** The dispatch's required-coverage item 1 says the initial state has stacks `[1000,1000]` and pot 15. That is inconsistent with the same contract's blind rule (blinds are posted street commitments and pot contributions), with `CurrentBet == 10`/`StreetCommitted == [5,10]`, and with the chip-conservation invariant `sum(Stacks) + Pot == 2000`: `1000 + 1000 + 15 = 2015`. The engine and the test use the conservation-consistent `[995, 990]` and assert the 2000 total. If `[1000,1000]` is required literally, the blind-post and conservation rules must change instead.
2. **Odd-chip split through legal play.** In heads-up play every showdown pot is `2 × matched contribution` after uncalled chips are returned, so it is always even; no legal action sequence can exercise the odd-chip branch. The odd-chip rule is implemented in the internal `SplitPot` used by `ResolveShowdown` and is tested directly with concrete odd values (`SplitPot(21, 0) == [10,11]`, `SplitPot(21, 1) == [11,10]`, `SplitPot(1, 0) == [0,1]`). The legal split-pot test uses seed 206, where the board plays for both seats, and asserts two winners and exactly equal `[1000,1000]` stacks.

No other required behavior failed. All six verification commands above were executed and observed.

## Environment / working-tree notes

- `dotnet test` was not run; the local VSTest testhost limitation is documented. The compiled test assembly was executed with the supplementary runner.
- CI was not triggered or observed from this session.
- The working tree contains unrelated concurrent changes in `WIP/Wip.Bones.Agents/*`, `Wip.slnx`, `tools/run-wip-tests.ps1` and `tests/Wip.Architecture.Tests/*` from other sessions/round 1. This round did not touch them; the only tracked-file edits made here are `docs/plan/test-waivers.json` and the untracked round-1/round-2 Tholdem trees.
