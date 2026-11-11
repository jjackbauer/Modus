# Round 12 implementation — preflop situations and layered call EV in the equity policy

Status: complete. `EquityPolicyAgent` now derives its calling threshold from the side-pot-aware
layered call EV instead of the raw pot-odds scalar, and adds a preflop raise margin from the
classified preflop situation. The observation-to-EV bridge and the classifier are new production
code with seven new tests. The legacy `StrategyAgent` is untouched.

## Files

| File | Change |
| --- | --- |
| `WIP/Wip.Tholdem.Agents/Equity/CallEvObservation.cs` | New: projects `PlayerObservation` into `CallEvInput`, counts caller-eligible layers, repeats the uniform equity and delegates to `CallEvCalculator`. |
| `WIP/Wip.Tholdem.Agents/Learning/PreflopSituation.cs` | New: `PreflopSituation`, `PreflopContext` and `PreflopSituationClassifier`. |
| `WIP/Wip.Tholdem.Agents/Learning/EquityPolicy.cs` | Additive: `double PreflopRaiseMargin = 0.03` as the last positional parameter, with the documented [0, 1] domain. |
| `WIP/Wip.Tholdem.Agents/Learning/EquityPolicyAgent.cs` | Bet branch: build `callEv`, classify the preflop situation, add the margin to the break-even threshold and to `baseline + RaiseEdge`; the no-bet branch is unchanged. Constructor clamps the new field. |
| `WIP/Wip.Tholdem.Tests/Equity/CallEvObservationTests.cs` | New: the three design tests. |
| `WIP/Wip.Tholdem.Tests/Learning/PreflopSituationTests.cs` | New: the four design tests. |
| `docs/goal-tholdem-enhancement/round-12/implementation-preflop-call-ev.md` | New: this evidence record. |

No other file was edited. `StrategyAgent`, `HandStrength`, the evaluator, the gate, the tools and
every existing test are unchanged.

## Contract as implemented

```csharp
public static class CallEvObservation
{
    public static CallEvResult Compute(PlayerObservation observation, double uniformLayerEquity);
}

public enum PreflopSituation { NotPreflop, Unopened, FacingLimp, FacingOpen, FacingThreeBet }

public sealed record PreflopContext(
    PreflopSituation Situation, int Raises, int Calls, bool IsBlindDefense);

public static class PreflopSituationClassifier
{
    public static PreflopContext Classify(PlayerObservation observation);
}

public sealed record EquityPolicy(
    int EquitySamples = 400,
    double ValueEdge = 0.10,
    double RaiseEdge = 0.18,
    double BluffFrequency = 0.08,
    double ValueBetPotFraction = 0.65,
    double PreflopRaiseMargin = 0.03);
```

`CallEvObservation` excludes the observing seat from the opponent list, keeps every other public
seat (folded seats included), mirrors the calculator's effective-call cap
`min(AmountToCall, max(0, liveMax - OwnTotalCommitted))`, takes the distinct positive levels of
`OwnAfter` and every opponent total, counts a level as caller-eligible when `OwnAfter >= level`,
repeats the validated uniform equity once per caller-eligible layer and delegates to
`CallEvCalculator.Compute`. The uniform equity is validated as finite and in [0, 1]. Per-layer
equity is one overall pot share repeated for each caller-eligible layer; the estimator exposes no
per-layer information, so this is explicitly a later refinement, not a measured range model.

`PreflopSituationClassifier` counts `Raise` and `AllIn` actions as raises and `Call` actions as
calls. Two or more raises is `FacingThreeBet`, exactly one is `FacingOpen`, no raise with at least
one call is `FacingLimp`, otherwise `Unopened`. `IsBlindDefense` is true only for
`FacingOpen` when the observing seat is the button or `(ButtonSeat + 1) % SeatCount` (the engine
posts the small blind on the button). Outside preflop the classifier returns `NotPreflop` with zero
counts and blind defense false, so the margin formula produces zero postflop.

`EquityPolicyAgent` facing a bet:

1. `callEv = CallEvObservation.Compute(observation, equity.ExpectedPotShare)`;
2. `margin = preflop.Raises == 0 ? 0 : Policy.PreflopRaiseMargin * (preflop.Raises >= 2 ? 2 : 1)`;
3. `threshold = Math.Clamp(callEv.BreakEvenEquity + margin, 0, 1)`;
4. `decision = EquityThresholdPolicy.DecideCall(equity, threshold)`;
5. call when the interval lower bound clears `threshold`; raise when it also clears
   `baseline + Policy.RaiseEdge + margin` (both the expected share and the lower bound, as before)
   and raising is legal; fold on `Fold`/`Uncertain`; otherwise call or first legal.

The no-bet branch (value bet, seeded bluff, check) is byte-for-byte unchanged.

## Test-first: observed red output

The tests and compile-only stubs were written first: the bridge threw `NotImplementedException`,
the classifier returned `NotPreflop` unconditionally, and `PreflopRaiseMargin` existed on the
policy record. The test assembly built with 0 errors, and the two filtered runs exited 1:

```text
FAIL CallEvObservationTests.Compute_GivenNoAmountToCall_IsZeroEv: NotImplementedException: The method or operation is not implemented.
FAIL CallEvObservationTests.Compute_GivenSidePotObservation_ReturnsLayersAndEffectiveCall: NotImplementedException: The method or operation is not implemented.
FAIL CallEvObservationTests.Compute_GivenUniformEquityOutOfRange_Throws: ThrowsException: Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(System.ArgumentOutOfRangeException)
Actual:   typeof(System.NotImplementedException)
TOTAL 3 PASSED 0 FAILED 3 SKIPPED 0

FAIL PreflopSituationTests.Classify_GivenEmptyHistory_IsUnopened: EqualException: Assert.Equal() Failure: Values differ
Expected: Unopened
Actual:   NotPreflop
FAIL PreflopSituationTests.Classify_GivenOneCall_IsFacingLimp: EqualException: Assert.Equal() Failure: Values differ
Expected: FacingLimp
Actual:   NotPreflop
FAIL PreflopSituationTests.Classify_GivenOneRaise_IsFacingOpenAndBlindDefenseForButton: EqualException: Assert.Equal() Failure: Values differ
Expected: FacingOpen
Actual:   NotPreflop
FAIL PreflopSituationTests.Classify_GivenTwoRaises_IsFacingThreeBet: EqualException: Assert.Equal() Failure: Values differ
Expected: FacingThreeBet
Actual:   NotPreflop
TOTAL 4 PASSED 0 FAILED 4 SKIPPED 0
```

All seven tests were red; no new test was green before the implementation.

## Observed green

Commands were run from the repository root after the final test strengthening:

```text
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
Build succeeded.
    12 Warning(s)
    0 Error(s)

dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Equity.CallEvObservationTests
TOTAL 3 PASSED 3 FAILED 0 SKIPPED 0

dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Learning.PreflopSituationTests
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0

dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Learning.EquityPolicyAgentTests
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0

dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 556 PASSED 556 FAILED 0 SKIPPED 0
```

A full, non-incremental rebuild reported 16 warnings and 0 errors; no warning names a touched file.
The warnings are pre-existing diagnostics in untouched test/host files.

The observed 556 total is the round-11 recorded 549-test baseline plus the seven new tests. The four
existing `EquityPolicyAgentTests` (legality, determinism, action distribution, zero-live-opponent
guard) still pass unchanged.

## Side-pot bridge values

The passing fixture builds own seat 0 total 20, live seats 1 and 2 with totals 50 and 100, folded
seat 3 with total 30, pot 200, call 80, stack 500 and uniform equity 0.25. The asserted and observed
result is:

| Level | Amount | Caller eligible | Equity |
| --- | --- | --- | --- |
| 30 | 120 | yes | 0.25 |
| 50 | 60 | yes | 0.25 |
| 100 | 100 | yes | 0.25 |

| Quantity | Observed value |
| --- | --- |
| Effective call | 80 |
| Contestable pot | 280 (120 + 60 + 100) |
| Call EV | -10.0 (0.25 * 280 - 80) |
| Fold EV | 0.0 |
| Break-even equity | 80 / 280 = 0.2857142857142857 |

The no-call fixture (call 0, same totals) returns effective call 0, a single caller-eligible layer
at level 20 worth 80 chips, contestable pot 80, call EV 20.0 (0.25 * 80) and break-even 0.0. The
out-of-range fixture observes exact `ArgumentOutOfRangeException` for -0.01, 1.01 and NaN.

## Observed preflop classifications

From the four passing classifier fixtures:

| Fixture | Situation | Raises | Calls | Blind defense |
| --- | --- | --- | --- | --- |
| Empty history, seat 2 | `Unopened` | 0 | 0 | false |
| One call, seat 2 | `FacingLimp` | 0 | 1 | false |
| One raise, button seat 0 | `FacingOpen` | 1 | 0 | true |
| One raise, big-blind seat 1 | `FacingOpen` | 1 | 0 | true |
| One raise, under-the-gun seat 2 | `FacingOpen` | 1 | 0 | false |
| Raise + all-in, seat 2 | `FacingThreeBet` | 2 | 0 | false |

The real-match observation used a temporary `[Fact]` probe inside the new classifier test file,
executed the 12-hand seed 20261109 equity-policy match from `EquityPolicyAgentTests`, printed the
lines below and was removed before the final build. It is not part of the delivered tests:

```text
PROBE situations NotPreflop=10 Unopened=3 FacingLimp=9
PROBE preflop-only Unopened=3 FacingLimp=9
```

So all 22 recorded seat-0 decisions classified as 10 postflop, 3 unopened preflop pots and 9 facing
a limp. Seat 0 never faced an open or a three-bet in this 12-hand sample; those two branches are
evidenced by the synthetic fixtures above, not by this match.

## Post-change action distribution

The same temporary probe observed the seat-0 actions in the unchanged `EquityPolicyAgentTests`
match (seed 20261109, 12 hands, four seats, 150 equity samples, three tight-aggressive opponents):

```text
PROBE actions hands=12 turns=22 Fold=10 Check=9 Call=1 Bet=1 Raise=1
```

The distribution still contains aggressive and passive kinds, so the existing
`ActAsync_GivenTwelveHands_ProducesAggressiveAndPassiveActions` assertion holds unchanged. The
observed post-change distribution is Fold 10, Check 9, Call 1, Bet 1, Raise 1 across 22 turns. This
is a distribution record, not a strength claim: no evaluator comparison was run in this slice.

## No stub remains

Observed after the final build:

- `grep NotImplementedException WIP/Wip.Tholdem.Agents/Equity/CallEvObservation.cs` -> 0 matches.
- `grep NotImplementedException WIP/Wip.Tholdem.Agents/Learning` -> no matches.
- `grep potOdds WIP/Wip.Tholdem.Agents/Learning` -> matches only in `StrategyAgent.cs` (the
  deliberately untouched v1 control); `EquityPolicyAgent.cs` has none.

Both new types contain production implementations that delegate to `CallEvCalculator` or derive the
situation from the public action history; no placeholder return or exception remains.

## Non-claims and limits

The per-layer equity is a single uniform pot share repeated across caller-eligible layers. The
break-even threshold is now measured from the side-pot-aware effective call and contestable pot, and
the preflop margin is a fixed policy parameter, but this slice does not demonstrate a stronger
policy, a higher win rate or improved evaluation results. The earlier evaluator and promotion tests
are unchanged and were not re-baselined here.
