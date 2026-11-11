# Round 9 implementation — equity-aware policy and pot-relative sizing (D4 / POL-005, POL-006)

Status: complete. Scope: the three new production files, the two new test files and this document.
No existing file, test, evaluator, gate, runtime tool or project setting was changed, and
`StrategyAgent` is untouched as the v1 control. This slice adds a new policy family; it makes no
strength claim against the champion.

## Files

| File | Change |
| --- | --- |
| `WIP/Wip.Tholdem.Agents/Learning/PotRelativeSizing.cs` | New: validated pot-relative target with half-away rounding and clamp. |
| `WIP/Wip.Tholdem.Agents/Learning/EquityPolicy.cs` | New: data-only policy record. |
| `WIP/Wip.Tholdem.Agents/Learning/EquityPolicyAgent.cs` | New: `IPokerAgent` deciding from seeded multiway equity and the policy edges. |
| `WIP/Wip.Tholdem.Tests/Learning/PotRelativeSizingTests.cs` | New: five sizing tests. |
| `WIP/Wip.Tholdem.Tests/Learning/EquityPolicyAgentTests.cs` | New: four four-seat agent tests. |
| `docs/goal-tholdem-enhancement/round-9/implementation-equity-policy.md` | New: this evidence record. |

## Contract as implemented

### `PotRelativeSizing.Target`

```csharp
public static long Target(
    long pot, long amountToCall, long ownStreetCommitment, double potFraction,
    long minRaiseTo, long maxRaiseTo);
```

Validation runs before any arithmetic: `pot >= 0`, `amountToCall >= 0`,
`ownStreetCommitment >= 0`, `potFraction > 0 and <= 2`, `minRaiseTo > 0`,
`maxRaiseTo >= minRaiseTo`. Each violation throws `ArgumentOutOfRangeException`. The raw target is
`ownStreetCommitment + amountToCall + potFraction * (pot + amountToCall)`, computed in double so a
legal long pot and call pair cannot overflow the intermediate sum. It is rounded half away from zero
with `Math.Round(raw, MidpointRounding.AwayFromZero)` and then clamped to
`[minRaiseTo, maxRaiseTo]`. No clock, culture or ambient randomness is read.

### `EquityPolicy`

```csharp
public sealed record EquityPolicy(
    int EquitySamples = 400,
    double ValueEdge = 0.10,
    double RaiseEdge = 0.18,
    double BluffFrequency = 0.08,
    double ValueBetPotFraction = 0.65);
```

The agent never mutates the record; it stores a clamped copy in its `Policy` property. Domains:
`EquitySamples` [50, 5000], `ValueEdge` and `RaiseEdge` [0, 1], `BluffFrequency` [0, 1],
`ValueBetPotFraction` [0.01, 2]. The 0.01 floor exists because zero is outside the sizer's domain;
the target is clamped to `MinRaiseTo` anyway.

Two delegated contract corrections are applied:

1. `UncertaintyFold` is omitted. An uncertain interval is always an intended fold, so the field
   would be inert; the behaviour is fixed rather than configurable.
2. The two equity thresholds are opponent-count-relative, not fixed absolute values. Expected pot
   share scales like `1/(liveOpponents+1)`, so a fixed 0.62 value-bet threshold is unreachable with
   three opponents. The agent computes `baseline = 1.0/(liveOpponents+1)`, value-bets at
   `baseline + ValueEdge`, and raises at `baseline + RaiseEdge`. `ValueBetThreshold` and
   `RaiseEquityThreshold` therefore do not exist.

### `EquityPolicyAgent`

Constructor: `EquityPolicyAgent(EquityPolicy? policy = null, int? EquitySamples = null,
string? name = null)`. `EquitySamples` overrides the record's sample count before clamping, which
keeps the contracted call `EquityPolicyAgent(EquitySamples: 150)`. Default name is
`"equity-policy"`.

Per decision, in order:

1. Live opponents are the seats in `observation.Seats` whose `Seat` is not the acting seat and
   whose `Folded` flag is false; an all-in seat still counts because it is live at showdown. With
   zero live opponents (or more than the sampler's five-opponent ceiling) the agent returns the
   first legal action without estimating equity.
2. `canonical = observation.ToCanonicalJson()`;
   `seed = SeedDerivation.DeriveAgentSeed(canonical, context.HandNumber, context.Seat,
   context.ActionIndex, 3UL)`;
   `equity = MultiwayEquity.Estimate(observation.OwnHoleCards, observation.Board, liveOpponents,
   seed, Policy.EquitySamples)` with the uniform (unconditioned) range model.
3. `potOdds = AmountToCall / (double)(Pot + AmountToCall)`.
4. Facing a bet: `decision = EquityThresholdPolicy.DecideCall(equity, potOdds)`. When the decision
   is Call and the interval lower bound is above pot odds, a raise is taken first if
   `ExpectedPotShare >= baseline + RaiseEdge` and the interval lower bound also reaches that
   threshold (and a bet or raise kind is legal); otherwise the agent calls. Fold and Uncertain fold
   when fold is legal. Anything else falls back to check/call and then to the first legal action.
5. With no bet: value-bet when `ExpectedPotShare >= baseline + ValueEdge`, or bluff when the unit
   drawn from stream 4 is below `BluffFrequency`; the target comes from
   `PotRelativeSizing.Target(Pot, AmountToCall, OwnStreetCommitment, ValueBetPotFraction,
   MinRaiseTo, MaxRaiseTo)`. Otherwise check.
6. Every returned action is passed through a legality check: the kind must be listed in
   `LegalActionKinds`, and a bet or raise amount must be in `[MinRaiseTo, MaxRaiseTo]`. A violation
   falls back to the first legal action.

A bound equal to an equity threshold counts as clearing it, consistent with the expected-share
comparison; `EquityThresholdPolicy.DecideCall` keeps its own strict pot-odds comparison. The bluff
unit is `(seed(stream 4) mod 1_000_000) / 1_000_000.0`, the same derivation the baselines use.

### Determinism and inputs

The agent reads only `PlayerObservation` and `AgentTurnContext`. It uses no clock, culture, GUID,
ambient randomness, I/O or mutable static state. Both seeds derive from the canonical observation
and public turn coordinates, so the same observation and context always produce the same action,
and no value is a function of the session seed.

## Tests added

Exactly five sizing tests and four agent tests, all in the namespaces shown above.

| Test | Fixture and asserted result |
| --- | --- |
| `Target_GivenPotFractions_ReturnsDistinctTargets` | pot 200, call 0, own 0, bounds [1, 1000]; fractions 0.25/0.5/0.75/1 return 50/100/150/200. |
| `Target_GivenMinRaiseAboveTarget_ClampsToMin` | pot 20, fraction 0.25, min 10 returns 10. |
| `Target_GivenMaxBelowTarget_ClampsToMax` | pot 1000, fraction 1.0, max 300 returns 300. |
| `Target_GivenCallAndOwnCommitment_AddsBoth` | pot 100, call 50, own 20, fraction 0.5 returns 145. |
| `Target_GivenInvalidFraction_Throws` | fractions 0, -0.1 and 2.1 each throw `ArgumentOutOfRangeException`. |
| `ActAsync_GivenFourSeatMatch_OnlyLegalActions` | 12 hands, seat 0 `EquityPolicyAgent(EquitySamples: 150)` wrapped in `RecordingAgent`, three `TightAggressiveAgent`; the simulator completes and all 22 recorded actions pass the shared legality assertion. |
| `ActAsync_GivenSameObservation_IsDeterministic` | every recorded turn is replayed through two fresh agents; both return the recorded action and it is legal. |
| `ActAsync_GivenTwelveHands_ProducesAggressiveAndPassiveActions` | the same 12-hand match yields at least one Bet/Raise and at least one Check/Call/Fold. |
| `ActAsync_GivenNoLiveOpponents_ReturnsFirstLegal` | synthetic `PlayerObservation` with four seats, every seat folded, legal kinds [Check, Bet]; the action is Check with amount 0. |

Friend access permits the synthetic observation: `Wip.Tholdem` already declares
`InternalsVisibleTo("Wip.Tholdem.Tests")`, so the internal constructor and internal init setters of
`PlayerObservation` are usable from the test assembly. No public-engine substitution was needed.

## Test-first: observed red output

The tests were written first and `PotRelativeSizing.Target` and `EquityPolicyAgent.ActAsync` were
stubbed with `throw new NotImplementedException(...)` so the test assembly compiled. The stub build
succeeded:

```text
Build succeeded.
    14 Warning(s)
    0 Error(s)
```

All fourteen warnings are pre-existing in `Wip.Tholdem.Host` and unrelated test files; none is in a
new file. The filtered runs exited 1 and observed:

```text
FAIL PotRelativeSizingTests.Target_GivenCallAndOwnCommitment_AddsBoth: NotImplementedException: pot-relative-sizing-not-implemented
FAIL PotRelativeSizingTests.Target_GivenInvalidFraction_Throws: ThrowsException: Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(System.ArgumentOutOfRangeException)
Actual:   typeof(System.NotImplementedException)
FAIL PotRelativeSizingTests.Target_GivenMaxBelowTarget_ClampsToMax: NotImplementedException: pot-relative-sizing-not-implemented
FAIL PotRelativeSizingTests.Target_GivenMinRaiseAboveTarget_ClampsToMin: NotImplementedException: pot-relative-sizing-not-implemented
FAIL PotRelativeSizingTests.Target_GivenPotFractions_ReturnsDistinctTargets: NotImplementedException: pot-relative-sizing-not-implemented
TOTAL 5 PASSED 0 FAILED 5 SKIPPED 0
```

```text
FAIL EquityPolicyAgentTests.ActAsync_GivenFourSeatMatch_OnlyLegalActions: IllegalAgentActionException: Agent 'equity-policy' produced an illegal action in hand 1: the agent threw NotImplementedException: equity-policy-not-implemented
FAIL EquityPolicyAgentTests.ActAsync_GivenNoLiveOpponents_ReturnsFirstLegal: NotImplementedException: equity-policy-not-implemented
FAIL EquityPolicyAgentTests.ActAsync_GivenSameObservation_IsDeterministic: IllegalAgentActionException: Agent 'equity-policy' produced an illegal action in hand 1: the agent threw NotImplementedException: equity-policy-not-implemented
FAIL EquityPolicyAgentTests.ActAsync_GivenTwelveHands_ProducesAggressiveAndPassiveActions: IllegalAgentActionException: Agent 'equity-policy' produced an illegal action in hand 1: the agent threw NotImplementedException: equity-policy-not-implemented
TOTAL 4 PASSED 0 FAILED 4 SKIPPED 0
```

## Observed green output

A full rebuild after the implementation reported the same fourteen pre-existing warnings and no
error; an immediately repeated build was up to date and reported `0 Warning(s)`, `0 Error(s)`.
No warning points at a new file.

New test classes (both exit 0):

```text
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Learning.PotRelativeSizingTests
TOTAL 5 PASSED 5 FAILED 0 SKIPPED 0
```

```text
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Learning.EquityPolicyAgentTests
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0
```

Full suite (exit 0). The observed count is the 531 pre-existing tests plus the nine new tests:

```text
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 540 PASSED 540 FAILED 0 SKIPPED 0
```

## Observed action-kind distribution, 12-hand four-seat match

Fixture: seed 20261109, 12 hands, seat 0 `EquityPolicyAgent(EquitySamples: 150)` with the default
policy, seats 1-3 `TightAggressiveAgent`, default `SimulationOptions` stacks and blinds. An
out-of-repository throwaway console probe (deleted afterwards) recorded 22 seat-0 decisions across
the 12 hands:

| Kind | Count |
| --- | --- |
| Bet | 1 |
| Call | 1 |
| Check | 9 |
| Fold | 10 |
| Raise | 1 |
| AllIn | 0 |

Both required groups are present: aggressive (Bet and Raise, one each) and passive (Check, Call,
Fold). The distribution is descriptive only. The sample count is deliberately small for test speed,
and this record makes no claim that the new policy is stronger than the champion or any baseline.

## No stub remains

A content search for `NotImplementedException`, `TODO` and `FIXME` across the five new C# files
returned zero matches. All five are UTF-8 without BOM with no null bytes (verified byte-wise), and
`git status --porcelain` shows them only as untracked (`??`) entries: no tracked file was modified
by this slice.

## Deviations from the design text

Only the two delegated corrections above (omit the inert `UncertaintyFold`, make the equity edges
opponent-count-relative and rename them) and the explicit 0.01 floor for
`ValueBetPotFraction`. The validation rules, raw formula, half-away rounding, clamp,
live-opponent count, stream 3 equity seed, stream 4 bluff draw and per-action legality check match
the design.

## Commands used

```text
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Learning.PotRelativeSizingTests
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Learning.EquityPolicyAgentTests
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```