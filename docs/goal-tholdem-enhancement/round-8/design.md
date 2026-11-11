# Round 8 design — contribution- and side-pot-aware call EV (EQ-005)

**Objective:** replace the single unqualified pot-odds scalar with a layered call-EV calculation that
uses actual contributions, contested pot layers, folded dead money and side-pot eligibility. Per-layer
showdown equities are supplied by the caller (for example from `MultiwayEquity` against the eligible
opponents of that layer); the calculator does not invent them.

## API — `Equity/CallEvCalculator.cs` (new)

~~~csharp
public sealed record CallEvOpponent(int Seat, long TotalCommitted, bool Folded);

public sealed record CallEvInput(
    long Pot,
    long OwnTotalCommitted,
    long OwnStackBehind,
    long AmountToCall,
    IReadOnlyList<CallEvOpponent> Opponents,
    IReadOnlyList<double> LayerEquities,   // one per caller-eligible layer, in ascending level order
    long FutureCost = 0);

public sealed record PotLayer(
    int Level, long Amount, IReadOnlyList<int> EligibleSeats, bool CallerEligible, double Equity);

public sealed record CallEvResult(
    long EffectiveCall, long ContestablePot, double CallEv, double FoldEv,
    double BreakEvenEquity, IReadOnlyList<PotLayer> Layers);

public static class CallEvCalculator
{
    public static CallEvResult Compute(CallEvInput input);
}
~~~

## Rules

- Validate: pot, totals, stack, call and future cost >= 0; `AmountToCall <= OwnStackBehind`; at
  least one live (non-folded) opponent; unique opponent seats; each layer equity finite in [0,1].
- Live max = max total committed among non-folded opponents. `EffectiveCall = min(AmountToCall,
  max(0, LiveMax - OwnTotalCommitted))`; chips above the largest live commitment are returned.
- `OwnAfter = OwnTotalCommitted + EffectiveCall`. Pot layers use the distinct positive levels of
  `{OwnAfter} union {opponent totals}`. For each layer `(previous, level]`:
  `Amount = (level - previous) * contributors`, where contributors are every player (including
  folded seats) whose total is at least `level`; `EligibleSeats` are the non-folded opponents with
  total >= level; `CallerEligible = OwnAfter >= level`. The first `LayerEquities` value is assigned to
  the lowest caller-eligible layer, in ascending level order; non-eligible layers carry 0.
- `ContestablePot = sum(Amount over caller-eligible layers)`;
  `CallEv = sum(Equity * Amount over caller-eligible layers) - EffectiveCall - FutureCost`;
  `FoldEv = 0`;
  `BreakEvenEquity = (EffectiveCall + FutureCost) / (ContestablePot + EffectiveCall + FutureCost)`,
  clamped to [0,1] (zero when the denominator is zero).
- The layer equity count must equal the number of caller-eligible layers. No clock, culture, GUID or
  ambient randomness.

## Tests — `WIP/Wip.Tholdem.Tests/Equity/CallEvCalculatorTests.cs` (six)

1. `Compute_GivenHeadsUpCall_ReturnsLayeredEvAndBreakEven`: pot 100, own 20, opponent 80 live, call
   60, equity [0.4] -> effective 60, contestable 160, CallEv 4.0, break-even 0.375.
2. `Compute_GivenShortAllInSidePot_UsesEligibility`: pot 170, own 20, A 50 all-in, B 100, call 80,
   equities [0.3, 0.4] -> two caller-eligible layers (150 and 100), CallEv 5.0, break-even 80/330.
3. `Compute_GivenFoldedDeadMoney_IncludesIt`: pot 130, own 20, folded 30, live 80, call 60,
   equities [0.25, 0.4] -> layers 90 and 100, CallEv 2.5.
4. `Compute_GivenCallAboveCover_ReturnsExcess`: pot 180, own 80, live 100, call 50 -> effective 20,
   not 50; CallEv = equity * 200 - 20.
5. `Compute_GivenFutureCost_SubtractsIt`: same as (2) with FutureCost 10 -> CallEv -5.0.
6. `Compute_GivenInvalidInputs_Throws`: equity count mismatch, equity outside [0,1], call above stack,
   no live opponent, duplicate seats.

## Forbidden

Do not change the evaluator, gate, equity estimator, range model, tool or any existing test. No
test weakening. No claim that this integrates with the strategy agent yet; the integration is a later
slice.
