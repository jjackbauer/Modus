# Round 8 implementation — contribution- and side-pot-aware call EV (EQ-005)

Status: complete. Scope: the new calculator, the six contracted tests and this document. No existing
file, test or project setting was changed. The calculator is not wired into the strategy agent or
the equity tool in this slice.

## Files

| File | Change |
| --- | --- |
| `WIP/Wip.Tholdem.Agents/Equity/CallEvCalculator.cs` | New: `CallEvOpponent`, `CallEvInput`, `PotLayer`, `CallEvResult` and `CallEvCalculator.Compute`. |
| `WIP/Wip.Tholdem.Tests/Equity/CallEvCalculatorTests.cs` | New: the six contracted tests. |
| `docs/goal-tholdem-enhancement/round-8/implementation-call-ev.md` | New: this evidence record. |

## Contract as implemented

- `public sealed record CallEvOpponent(int Seat, long TotalCommitted, bool Folded)`.
- `public sealed record CallEvInput(long Pot, long OwnTotalCommitted, long OwnStackBehind,
  long AmountToCall, IReadOnlyList<CallEvOpponent> Opponents, IReadOnlyList<double> LayerEquities,
  long FutureCost = 0)`.
- `public sealed record PotLayer(int Level, long Amount, IReadOnlyList<int> EligibleSeats,
  bool CallerEligible, double Equity)`.
- `public sealed record CallEvResult(long EffectiveCall, long ContestablePot, double CallEv,
  double FoldEv, double BreakEvenEquity, IReadOnlyList<PotLayer> Layers)`.
- `public static CallEvResult CallEvCalculator.Compute(CallEvInput input)` in
  `Wip.Tholdem.Agents.Equity`.

Validation rejects, before any layer is built:

| Condition | Exception |
| --- | --- |
| null input, null opponent or equity list | `ArgumentNullException` |
| negative pot, own total, stack, call, future cost or opponent total | `ArgumentOutOfRangeException` |
| `AmountToCall > OwnStackBehind` | `ArgumentException` |
| no live (non-folded) opponent, empty opponent list | `ArgumentException` |
| duplicate opponent seat, null opponent entry | `ArgumentException` |
| equity value not finite or outside [0,1] | `ArgumentOutOfRangeException` |
| equity count not equal to the caller-eligible layer count | `ArgumentException` |

Layer construction, exactly as specified:

- `LiveMax = max TotalCommitted among non-folded opponents`;
  `EffectiveCall = min(AmountToCall, max(0, LiveMax - OwnTotalCommitted))`;
  `OwnAfter = OwnTotalCommitted + EffectiveCall`. Chips of the call above `LiveMax` are not
  committed (test 4 observes 30 of a 50 call returned).
- Levels are the distinct positive values of `{OwnAfter} union {opponent totals, folded included}`,
  in ascending order. Each layer `(previous, level]` has
  `Amount = (level - previous) * contributors`, where a contributor is the caller when
  `OwnAfter >= level` plus every opponent (folded included) with
  `TotalCommitted >= level`.
- `EligibleSeats` are the non-folded opponent seats with `TotalCommitted >= level`, sorted
  ascending by seat. `CallerEligible = OwnAfter >= level`.
- Caller-supplied equities are assigned in ascending level order to the caller-eligible layers
  only; every non-eligible layer carries `Equity = 0`. The count must match exactly.
- `ContestablePot` is the sum of `Amount` over caller-eligible layers;
  `CallEv = sum(Equity * Amount over caller-eligible layers) - EffectiveCall - FutureCost`;
  `FoldEv = 0`.
- `Pot` is validated as non-negative. Layer amounts are derived from actual commitments, not from
  the scalar pot, so folded dead money and side-pot eligibility cannot be lost.

### Break-even correction

The design's Rules line wrote
`(EffectiveCall + FutureCost) / (ContestablePot + EffectiveCall + FutureCost)`, which double-counts
the call because `ContestablePot` already contains it. The delegated correction is used:

`BreakEvenEquity = (EffectiveCall + FutureCost) / ContestablePot`, clamped to [0,1], and zero when
`ContestablePot` is zero.

This is the only deviation from the design's Rules text; the layer and EV rules are unchanged. The
design's own test 1 expectation, 60/160 = 0.375, already only holds under the corrected formula.

### Determinism

No clock, culture, GUID, ambient randomness, I/O or mutable static state is read. The method is pure
arithmetic over its arguments; `double.IsFinite` guards every supplied equity and the layer
`Level` is converted to the contracted `int` with a checked cast, so an out-of-range level fails
loudly instead of truncating.

## Tests added

All six are in `Wip.Tholdem.Tests.Equity.CallEvCalculatorTests` and assert exact values.

| Test | Fixture | Observed asserted result |
| --- | --- | --- |
| `Compute_GivenHeadsUpCall_ReturnsLayeredEvAndBreakEven` | pot 100, own 20, live 80, call 60, equity [0.4] | effective 60, contestable 160, CallEv 4.0, break-even 0.375; one layer 80/160, eligible [1] |
| `Compute_GivenShortAllInSidePot_UsesEligibility` | pot 170, own 20, seat 1 = 50 live, seat 2 = 100 live, call 80, equities [0.3, 0.4] | effective 80, contestable 250, CallEv 5.0, break-even 0.32; layers 50/150 eligible [1,2] equity 0.3 and 100/100 eligible [2] equity 0.4 |
| `Compute_GivenFoldedDeadMoney_IncludesIt` | pot 130, own 20, seat 1 = 30 folded, seat 2 = 80 live, call 60, equities [0.25, 0.4] | effective 60, contestable 190, CallEv 2.5, break-even 60/190 = 0.3157894736842105; layers 30/90 and 80/100, folded seat never eligible |
| `Compute_GivenCallAboveCover_ReturnsExcess` | pot 180, own 80, live 100, call 50, equity [0.5] | effective 20 (30 returned), contestable 200, CallEv 80.0, break-even 0.10; one layer 100/200 |
| `Compute_GivenFutureCost_SubtractsIt` | side-pot fixture with `FutureCost: 10` | effective 80, contestable 250, CallEv -5.0, break-even 90/250 = 0.36 |
| `Compute_GivenInvalidInputs_Throws` | valid heads-up input mutated once per case | null input, equity count mismatch, equities 1.5, -0.1 and NaN, call above stack, all-folded opponents, duplicate seats, empty opponents, negative pot and negative future cost all throw |

## Test-first: observed red output

The tests were written first and `Compute` was stubbed with
`throw new NotImplementedException("call-ev-not-implemented")` so the test assembly compiled. The
stub build succeeded:

```text
Build succeeded.
    15 Warning(s)
    0 Error(s)
```

Fourteen warnings are pre-existing in `Wip.Tholdem.Host` and unrelated Learning tests; the
fifteenth was xUnit2000 in the new test file pointing at one `Assert.Equal` whose arguments were
swapped. That argument order was corrected before the implementation; no expected value changed.

The filtered run exited 1 and observed:

```text
FAIL CallEvCalculatorTests.Compute_GivenCallAboveCover_ReturnsExcess: NotImplementedException: call-ev-not-implemented
FAIL CallEvCalculatorTests.Compute_GivenFoldedDeadMoney_IncludesIt: NotImplementedException: call-ev-not-implemented
FAIL CallEvCalculatorTests.Compute_GivenFutureCost_SubtractsIt: NotImplementedException: call-ev-not-implemented
FAIL CallEvCalculatorTests.Compute_GivenHeadsUpCall_ReturnsLayeredEvAndBreakEven: NotImplementedException: call-ev-not-implemented
FAIL CallEvCalculatorTests.Compute_GivenInvalidInputs_Throws: ThrowsException: Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(System.ArgumentNullException)
Actual:   typeof(System.NotImplementedException)
FAIL CallEvCalculatorTests.Compute_GivenShortAllInSidePot_UsesEligibility: NotImplementedException: call-ev-not-implemented
TOTAL 6 PASSED 0 FAILED 6 SKIPPED 0
```

## Observed green output

Required build after the implementation (warnings are the pre-existing fourteen; none is in the new
files):

```text
Build succeeded.
    14 Warning(s)
    0 Error(s)
```

An immediately repeated build is up to date and reports `0 Warning(s)`, `0 Error(s)`.

New test class (exit 0):

```text
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Equity.CallEvCalculatorTests
TOTAL 6 PASSED 6 FAILED 0 SKIPPED 0
```

Full suite (exit 0). The observed count is the 525 pre-existing tests plus the six new tests:

```text
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 531 PASSED 531 FAILED 0 SKIPPED 0
```

## Side-pot fixture: observed effective call, contestable pot and EV

The fixture is pot 170, own total 20, stack 500, call 80, seat 1 total 50 live, seat 2 total 100
live, layer equities [0.3, 0.4]. A throwaway console program compiled against the Release
`Wip.Tholdem.Agents` project, run outside the repository and deleted afterwards, printed:

```text
EffectiveCall=80
ContestablePot=250
CallEv=5
FoldEv=0
BreakEvenEquity=0.32
Layer=50 Amount=150 Eligible=[1,2] CallerEligible=True Equity=0.3
Layer=100 Amount=100 Eligible=[2] CallerEligible=True Equity=0.4
```

The same values are pinned by the green
`Compute_GivenShortAllInSidePot_UsesEligibility` test. Arithmetic: `OwnAfter = 20 + 80 = 100`,
`LiveMax = 100`; layer (0, 50] has three contributors (caller, seat 1, seat 2) and amount 150;
layer (50, 100] has two contributors (caller, seat 2) and amount 100; contestable pot 250;
`CallEv = 0.3*150 + 0.4*100 - 80 = 5.0`; break-even `80/250 = 0.32`.

## No stub remains

A content search for `NotImplementedException` under `WIP/Wip.Tholdem.Agents/Equity` and
`WIP/Wip.Tholdem.Tests/Equity` returned zero matches. The implementation file contains no stub
marker. Both new C# files are UTF-8 without BOM (checked by reading the first bytes; no EF BB BF
prefix, no null bytes). `git status --short` for the three scoped paths shows only untracked
(`??`) entries: no tracked file was modified by this slice.

## Commands used

```text
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Equity.CallEvCalculatorTests
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```
