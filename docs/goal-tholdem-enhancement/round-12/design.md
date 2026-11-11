# Round 12 design — preflop situations and call-EV integration into the equity policy

**Objective:** use the new observation totals/action history to (a) classify the preflop situation,
(b) compute the layered call EV at decision time, and (c) make `EquityPolicyAgent` call from that
break-even instead of a raw pot-odds scalar. Legacy `StrategyAgent` stays untouched.

## 1. `Equity/CallEvObservation.cs` (new)

~~~csharp
public static class CallEvObservation
{
    public static CallEvResult Compute(PlayerObservation observation, double uniformLayerEquity);
}
~~~

Build `CallEvInput` from the observation: `Pot`, `OwnTotalCommitted`, `OwnStackBehind`,
`AmountToCall`, one `CallEvOpponent` per other seat (`Seat`, `TotalCommitted`, `Folded`). Count the
caller-eligible layers from the distinct positive levels of `OwnAfter = OwnTotalCommitted + effective
call` and every opponent total (a level is caller-eligible when `OwnAfter >= level`), and repeat the
supplied uniform equity for each caller-eligible layer (the current estimator exposes one overall pot
share; per-layer equities are a later refinement and must be labelled as such). Validate the equity in
[0,1]. Delegate to `CallEvCalculator.Compute`.

## 2. `Learning/PreflopSituation.cs` (new)

~~~csharp
public enum PreflopSituation { NotPreflop, Unopened, FacingLimp, FacingOpen, FacingThreeBet }

public sealed record PreflopContext(
    PreflopSituation Situation, int Raises, int Calls, bool IsBlindDefense);

public static class PreflopSituationClassifier
{
    public static PreflopContext Classify(PlayerObservation observation);
}
~~~

Classify from the public action history: when the street is not preflop -> `NotPreflop`; two or more
raises/all-ins -> `FacingThreeBet`; exactly one -> `FacingOpen`; no raise but at least one call ->
`FacingLimp`; otherwise `Unopened`. `IsBlindDefense` is true when the acting seat is the button (the
engine posts the small blind there) or the big-blind seat `(ButtonSeat + 1) % SeatCount` and the
situation is `FacingOpen`.

## 3. Policy changes

- `EquityPolicy` gains `double PreflopRaiseMargin = 0.03` (clamped [0, 1]); it is the extra break-even
margin facing one preflop raise, doubled facing a three-bet, zero otherwise. No inert fields.
- `EquityPolicyAgent` facing a bet:
  1. `callEv = CallEvObservation.Compute(observation, equity.ExpectedPotShare)`;
  2. `margin = preflopRaises == 0 ? 0 : PreflopRaiseMargin * (preflopRaises >= 2 ? 2 : 1)`;
  3. threshold = clamp(callEv.BreakEvenEquity + margin, 0, 1);
  4. `decision = EquityThresholdPolicy.DecideCall(equity, threshold)`;
  5. Call when the interval lower bound clears the threshold; raise when it also clears
     `baseline + RaiseEdge + margin` and raising is legal; fold on Fold/Uncertain; otherwise call or
     first legal.
- The no-bet branch is unchanged. `CallEvCalculator` supplies the effective call and contestable pot,
so a short all-in opponent no longer makes the agent call more than it can contest.

## 4. Tests

`WIP/Wip.Tholdem.Tests/Equity/CallEvObservationTests.cs` (three):
1. `Compute_GivenSidePotObservation_ReturnsLayersAndEffectiveCall`: synthetic 4-seat observation
   (own total 20, seat1 50, seat2 100, folded seat3 30, pot 200, call 80, stack 500, uniform 0.25) ->
   effective 80, contestable 280, EV -10.0, break-even 80/280.
2. `Compute_GivenNoAmountToCall_IsZeroEv`: call 0 -> effective 0, EV equals equity × contestable pot.
3. `Compute_GivenUniformEquityOutOfRange_Throws`.

`WIP/Wip.Tholdem.Tests/Learning/PreflopSituationTests.cs` (four):
1. `Classify_GivenEmptyHistory_IsUnopened`.
2. `Classify_GivenOneCall_IsFacingLimp`.
3. `Classify_GivenOneRaise_IsFacingOpenAndBlindDefenseForButton`.
4. `Classify_GivenTwoRaises_IsFacingThreeBet`.

`EquityPolicyAgentTests` stay as they are (legality, determinism, action distribution). If the
distribution assertion no longer holds with the call-EV path, report the observed distribution; do not
loosen the assertion.

## 5. Forbidden

Do not change `StrategyAgent`, `HandStrength`, the evaluator, the gate, the tools, or any existing
test. No clock/culture/GUID/ambient randomness. Do not claim the policy is stronger; this slice only
makes its preflop/call decisions use the measured data.
