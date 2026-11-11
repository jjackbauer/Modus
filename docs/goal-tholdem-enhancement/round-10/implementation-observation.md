# Round 10 implementation — observation truthfulness: totals and action history (OBS-003, OBS-004)

Status: complete. The additive projection is implemented, the four new tests pass, and the full
suite is 544/544. The canonical observation bytes are byte-identical to the recorded pre-change
baseline. The only touch outside the original three-file scope is an explicitly approved additive
update to the pinned public-surface name array, recorded under "Approved additive surface update"
below.

## Files

| File | Change |
| --- | --- |
| `WIP/Wip.Tholdem/History/PlayerObservation.cs` | Additive: `PublicAction` record, `SeatPublicState.TotalCommitted`, `PlayerObservation.OwnTotalCommitted` and `ActionHistory`, and the mapping in `ForSeat`. `ToCanonicalJson` untouched. |
| `WIP/Wip.Tholdem.Tests/ObservationTruthfulnessTests.cs` | New: four tests. |
| `WIP/Wip.Tholdem.Tests/PlayerObservationTests.cs` | Approved exception: the pinned public-surface name array gains exactly `ActionHistory` and `OwnTotalCommitted`; no other assertion changes. |
| `docs/goal-tholdem-enhancement/round-10/implementation-observation.md` | New: this evidence record. |

No existing production behaviour, canonical member, project setting, engine, evaluator or hash was
changed.

## Contract as implemented

```csharp
public sealed record PublicAction(
    Street Street, int Seat, ActionKind Kind, long AmountTo, long PotAfter);

public sealed record SeatPublicState(
    int Seat, long Stack, long StreetCommitment, bool Folded, bool AllIn)
{
    /// <summary>Total chips this seat has committed across all streets.</summary>
    public long TotalCommitted { get; init; }
}
```

`PlayerObservation` gains `public long OwnTotalCommitted { get; internal init; }` and
`public IReadOnlyList<PublicAction> ActionHistory { get; internal init; }`. The parameterless
constructor initializes `ActionHistory` to `Array.Empty<PublicAction>()`.

`ForSeat` sets each `SeatPublicState.TotalCommitted = state.TotalCommitted[candidate]`,
`OwnTotalCommitted = state.TotalCommitted[seat]`, and maps `state.ActionLog` in order to
`PublicAction((Street)entry.Street, entry.Seat, entry.Kind, entry.Amount, entry.PotAfter)`.
The positional `SeatPublicState` constructor is unchanged, so every existing call site compiles
without modification.

`ToCanonicalJson` is not changed. It still emits exactly the old members
(`handNumber`, `street`, `buttonSeat`, `actingSeat`, `board`, `ownHoleCards`, `ownStack`,
`opponentStack`, `pot`, `ownStreetCommitment`, `opponentStreetCommitment`, `amountToCall`,
`minRaiseTo`, `maxRaiseTo`, `legalActionKinds`, `isComplete`, `winners`, plus
`seatCount`/`seat`/`seats` only when `SeatCount != 2`). The new members are deliberately not
referenced by the writer.

## Tests added

| Test | Observed assertion |
| --- | --- |
| `ForSeat_GivenFourSeatHand_ExposesTotalCommitmentsThatSumToPot` | Seed 20261110, four seats: call/call/call/check to the flop, then bet 10/call. The flop state has pot 60; `OwnTotalCommitted == state.TotalCommitted[0]`, every `Seats[i].TotalCommitted == state.TotalCommitted[i]`, and their sum equals `state.Pot`. |
| `ForSeat_GivenActionSequence_HistoryMatchesTheActionLog` | The same six-action state: `ActionHistory.Count == state.ActionLog.Count == 6`, and every entry matches street, seat, kind, amount-to and pot-after in the recorded order. |
| `ToCanonicalJson_GivenFourSeatObservation_DoesNotSerializeTheNewFields` | The four-seat canonical JSON contains `"seats":[` but none of `totalCommitted`, `ownTotalCommitted` or `actionHistory` (ordinal-ignore-case). |
| `ForSeat_GivenTwoSeatObservation_CanonicalBytesStillMatchTheRecordedFixture` | Seed 42, one hand, seat 0: the exact pre-change literal below; additionally `OwnTotalCommitted == 5`, `Seats[0].TotalCommitted == 5`, `Seats[1].TotalCommitted == 10`, empty `ActionHistory`. |

## Test-first: observed red output

The properties were first added with default values only (the new `PublicAction` type and init
properties existed, but `ForSeat` did not populate them) so the test assembly compiled. The build
succeeded with 14 pre-existing warnings and 0 errors. The filtered run exited 1:

```text
FAIL ObservationTruthfulnessTests.ForSeat_GivenActionSequence_HistoryMatchesTheActionLog: EqualException: Assert.Equal() Failure: Values differ
Expected: 6
Actual:   0
FAIL ObservationTruthfulnessTests.ForSeat_GivenFourSeatHand_ExposesTotalCommitmentsThatSumToPot: EqualException: Assert.Equal() Failure: Values differ
Expected: 10
Actual:   0
FAIL ObservationTruthfulnessTests.ForSeat_GivenTwoSeatObservation_CanonicalBytesStillMatchTheRecordedFixture: EqualException: Assert.Equal() Failure: Values differ
Expected: 5
Actual:   0
TOTAL 4 PASSED 1 FAILED 3 SKIPPED 0
```

The three red tests are the pinned two-seat test and the two total/history tests, as required. Two
details are stated exactly as observed to avoid overclaiming:

- In the red run, the pinned literal assertion inside test 4 passed; the test failed at the new
  two-seat commitment assertion. That is expected: the canonical writer was never changed, so the
  literal could not regress. Test 4 still fails before implementation, and it now guards both the
  byte pin and the two-seat `TotalCommitted` projection.
- `ToCanonicalJson_GivenFourSeatObservation_DoesNotSerializeTheNewFields` passed in the red run as
  well, because the stub added no serialization. It is an invariant guard, not a driver of the new
  behaviour.

## Observed green output

After implementing the mapping, the filtered run exited 0:

```text
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.ObservationTruthfulnessTests
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0
```

The Release build of the test project succeeded with 0 errors.

## Full suite: observed 544 total, 544 passed, 0 failed

```text
TOTAL 544 PASSED 544 FAILED 0 SKIPPED 0
EXITCODE=0
```

544 is exactly the 540 pre-existing tests plus the four new tests. All canonical-observation, hash,
replay, zero-sum, engine, evaluator and history tests pass unchanged. The pinned two-seat surface
guard also passes after the approved additive name-array update described next.

## Approved additive surface update

Before the update, the full suite was observed at `TOTAL 544 PASSED 543 FAILED 1` with the single
failure:

```text
FAIL PlayerObservationTests.ForSeat_GivenPublicSurface_ExpectedDocumentedPropertiesAndNoForbiddenNames: EqualException: Assert.Equal() Failure: Collections differ
```

That existing guard enumerates every public property of `PlayerObservation` by reflection and
compares the sorted names to a fixed array. Adding the two contract-required public properties
necessarily changes the list, so a recorded exception authorized only the surface-name array to be
updated: `ActionHistory` after `ActingSeat`, and `OwnTotalCommitted` after
`OwnStreetCommitment`. No production change was involved, and no other assertion in that test or in
any other existing test was touched. The guard still pins the complete 22-name surface and still
rejects `Seed`, `Deck`, `OpponentHole` and `HandRecord`; the canonical-observation golden is
unchanged. This is an intentional additive API contract update required by OBS-003/004, not a
weakening. The observed post-update rebuild and full suite are recorded above.

## Pinned two-seat literal

Seed 42, one hand, `HeadsUpHandEngine.Start(42)`, `PlayerObservation.ForSeat(start, 0)`, UTF-8,
327 bytes, no trailing newline:

```json
{"handNumber":1,"street":0,"buttonSeat":0,"actingSeat":0,"board":[],"ownHoleCards":["7d","9c"],"ownStack":995,"opponentStack":990,"pot":15,"ownStreetCommitment":5,"opponentStreetCommitment":10,"amountToCall":5,"minRaiseTo":20,"maxRaiseTo":1000,"legalActionKinds":["Fold","Call","Raise","AllIn"],"isComplete":false,"winners":[]}
```

SHA-256: `a67a4235d5f97ca5441363c90765a527edd7ab4ac236ad326f6f03d3ca9e03e1`
Prefix: `a67a4235d5f97ca5`

The identical literal is already pinned by `MultiwayObservationTests.TwoSeatStartSeat0`; the new
round-10 test re-pins it together with the two-seat commitment projection.

## Canonical JSON contains none of the new field names — observed

1. The new-field absence test above passes for a four-seat flop observation
   (`TOTAL 4 PASSED 4`).
2. The pinned two-seat literal contains no `totalCommitted`, `ownTotalCommitted` or
   `actionHistory` text.
3. The pre-existing `ObservationProbe` was rebuilt against the changed assembly and its output
   compared byte-for-byte with `.tmp/tholdem-verify/ObservationProbe/baseline-observation.txt`
   (the pre-change capture). All seven recorded lines — five two-seat observation snapshots
   (start, flop, complete) and the hand-record JSON plus hash — are identical:

```text
OBSERVATION_BYTES_IDENTICAL_TO_BASELINE=True
HAS_NEW_FIELD_NAMES=False
LINE_COUNT=7
RECORD_HASH c953464b065b991bcda5c550c3ff5167b7fef6ccbd417636e822cf1c58a842c5
```

The hand-record hash is the same value recorded in the baseline, so no agent seed or hand hash
changes.

## No stubs remain

Content searches for `NotImplementedException`, `TODO` and `FIXME` across all touched C# files
return zero matches. All three touched C# files and this document are UTF-8 without BOM (C# first
bytes `75 73 69`; this document begins `23 20 52`; no `EF BB BF`).

## Commands used

```text
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.ObservationTruthfulnessTests
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

The byte-identity probe used the existing project and baseline:

```text
dotnet build .tmp/tholdem-verify/ObservationProbe/ObservationProbe.csproj -c Release
dotnet .tmp/tholdem-verify/ObservationProbe/bin/Release/net10.0/ObservationProbe.dll
```
