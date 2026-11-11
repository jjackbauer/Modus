# Round 9 design — equity-aware policy and pot-relative sizing (D4 / POL-005, POL-006)

**Objective:** add a new data-only policy family that decides from an `EquityEstimate` and explicit
thresholds instead of comparing a hand-category score to pot odds, and size bets/raises as a fraction
of the pot with legal clamping. The legacy `StrategyAgent` stays untouched as the v1 control.

## 1. `Learning/PotRelativeSizing.cs` (new)

~~~csharp
public static class PotRelativeSizing
{
    public static long Target(
        long pot, long amountToCall, long ownStreetCommitment, double potFraction,
        long minRaiseTo, long maxRaiseTo);
}
~~~

Validate `pot >= 0`, `amountToCall >= 0`, `ownStreetCommitment >= 0`, `potFraction > 0 and <= 2`,
`minRaiseTo > 0`, `maxRaiseTo >= minRaiseTo`. Compute
`raw = ownStreetCommitment + amountToCall + potFraction * (pot + amountToCall)`; round half away from
zero to a long and clamp to `[minRaiseTo, maxRaiseTo]`. No clock/culture.

## 2. `Learning/EquityPolicy.cs` (new)

~~~csharp
public sealed record EquityPolicy(
    int EquitySamples = 400,
    double ValueBetThreshold = 0.62,
    double RaiseEquityThreshold = 0.70,
    double BluffFrequency = 0.08,
    double ValueBetPotFraction = 0.65,
    double UncertaintyFold = 1.0);   // 1.0 = conservative fold on an Uncertain interval
~~~

The agent clamps `EquitySamples` to [50, 5000] and thresholds/fractions to their domains at
construction; it does not mutate the record.

## 3. `Learning/EquityPolicyAgent.cs` (new, `IPokerAgent`)

Per decision:
1. Count live opponents = seats in `observation.Seats` with `!Folded` (all-in seats still count for
   showdown equity). If zero, return the first legal action.
2. `seed = SeedDerivation.DeriveAgentSeed(observation.ToCanonicalJson(), context.HandNumber,
   context.Seat, context.ActionIndex, 3UL)`; `equity = MultiwayEquity.Estimate(observation.OwnHoleCards,
   observation.Board, liveOpponents, seed, policy.EquitySamples)` with uniform ranges.
3. `potOdds = AmountToCall / (double)(Pot + AmountToCall)` (0 when no call).
4. Facing a bet (`AmountToCall > 0`):
   - `decision = EquityThresholdPolicy.DecideCall(equity, potOdds)`;
   - Call when the decision is Call and the interval lower bound is above pot odds; if
     `ExpectedPotShare >= RaiseEquityThreshold` and the interval lower bound also clears it, raise
     with `ValueBetPotFraction`;
   - Fold on Fold, and fold on Uncertain (conservative, `UncertaintyFold = 1.0`) when fold is legal;
   - otherwise check/call the first legal action.
5. No bet: bet/raise when `ExpectedPotShare >= ValueBetThreshold`, or bluff when a seeded unit drawn
   from `stream 4` is below `BluffFrequency`; target via `PotRelativeSizing.Target`, else check.
6. Every returned action is checked for legality: kind present in `LegalActionKinds`, bet/raise amount
   in `[MinRaiseTo, MaxRaiseTo]`; fall back to the first legal action otherwise. Same observation and
   context always produce the same action.

## 4. Tests

`WIP/Wip.Tholdem.Tests/Learning/PotRelativeSizingTests.cs` (five):
1. `Target_GivenPotFractions_ReturnsDistinctTargets`: pot 200, call 0, own 0, fractions .25/.5/.75/1
   -> 50/100/150/200.
2. `Target_GivenMinRaiseAboveTarget_ClampsToMin`: pot 20, .25 -> 10.
3. `Target_GivenMaxBelowTarget_ClampsToMax`: pot 1000, 1.0, max 300 -> 300.
4. `Target_GivenCallAndOwnCommitment_AddsBoth`: pot 100, call 50, own 20, .5 -> 145.
5. `Target_GivenInvalidFraction_Throws`: 0, -0.1, 2.1.

`WIP/Wip.Tholdem.Tests/Learning/EquityPolicyAgentTests.cs` (four):
1. `ActAsync_GivenFourSeatMatch_OnlyLegalActions`: 12 hands, seat 0 `EquityPolicyAgent(EquitySamples:150)`,
   three `TightAggressiveAgent`; wrap seat 0 in `RecordingAgent`; simulator must complete and every
   recorded action is legal.
2. `ActAsync_GivenSameObservation_IsDeterministic`: replay every recorded turn through two fresh agents
   and assert the same action as the recorded one.
3. `ActAsync_GivenTwelveHands_ProducesAggressiveAndPassiveActions`: distinct kinds include at least
   one of Bet/Raise and at least one of Check/Call/Fold.
4. `ActAsync_GivenNoLiveOpponents_ReturnsFirstLegal`: construct a synthetic `PlayerObservation` with
   every seat folded (tests have friend access) and assert the action is the first legal kind.

## 5. Forbidden

Do not change `StrategyAgent`, `HandStrength`, the evaluator, the gate, the runtime tools, or any
existing test. Do not claim this policy is stronger than the champion; it is a new policy family with
legality and determinism evidence only. No clock/culture/GUID/ambient randomness.
