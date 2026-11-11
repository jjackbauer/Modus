# Round 10 design — observation truthfulness: totals and action history (OBS-003, OBS-004)

**Objective:** expose per-seat total commitments and a normalized public action history at the
decision boundary, so a policy can compute side-pot layers and preflop situations, **without changing
the canonical observation bytes** (and therefore without changing any agent seed or hand hash).

## 1. `History/PlayerObservation.cs` (edit, additive only)

~~~csharp
public sealed record PublicAction(
    Street Street, int Seat, ActionKind Kind, long AmountTo, long PotAfter);

public sealed record SeatPublicState(
    int Seat, long Stack, long StreetCommitment, bool Folded, bool AllIn)
{
    /// <summary>Total chips this seat has committed across all streets.</summary>
    public long TotalCommitted { get; init; }
}

public sealed record PlayerObservation
{
    // existing members unchanged
    public long OwnTotalCommitted { get; internal init; }
    public IReadOnlyList<PublicAction> ActionHistory { get; internal init; }
}
~~~

- `ForSeat` sets `SeatPublicState.TotalCommitted = state.TotalCommitted[seat]`,
  `OwnTotalCommitted = state.TotalCommitted[actingSeat]`, and maps `state.ActionLog` in order to
  `PublicAction((Street)entry.Street, entry.Seat, entry.Kind, entry.Amount, entry.PotAfter)`.
- `ToCanonicalJson` is **not changed**: no `totalCommitted`, `ownTotalCommitted` or `actionHistory`
  member is emitted. The new data is for policies/tools, not for the agent-seed schema.
- The parameterless constructor initializes `ActionHistory` to an empty array.

## 2. Tests — `WIP/Wip.Tholdem.Tests/ObservationTruthfulnessTests.cs` (four)

1. `ForSeat_GivenFourSeatHand_ExposesTotalCommitmentsThatSumToPot`: start a 4-seat hand, apply a
   short legal action sequence, call `ForSeat` for the acting seat and assert
   `OwnTotalCommitted == state.TotalCommitted[seat]`, every `Seats[i].TotalCommitted ==
   state.TotalCommitted[i]`, and their sum equals `state.Pot`.
2. `ForSeat_GivenActionSequence_HistoryMatchesTheActionLog`: assert `ActionHistory.Count ==
   state.ActionLog.Count` and each entry matches seat, street, kind, amount and pot-after.
3. `ToCanonicalJson_GivenFourSeatObservation_DoesNotSerializeTheNewFields`: the canonical JSON does
   not contain `totalCommitted`, `ownTotalCommitted` or `actionHistory`.
4. `ForSeat_GivenTwoSeatObservation_CanonicalBytesStillMatchTheRecordedFixture`: seed 42, one hand,
   seat 0; pin the exact canonical JSON literal (compute it from the current build and paste it into
   the test), proving the two-seat schema is byte-identical.

## 3. Forbidden

Do not emit the new fields in `ToCanonicalJson`; do not change any existing canonical member,
`HandRecord`, `MatchSimulator`, the engine, the evaluator, the gate, or any existing test. No clock,
culture, GUID or ambient randomness. No test weakening.
