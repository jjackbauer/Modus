# Round 6 implementation — confidence-gated call decisions (EQ-006)

Status: complete. Scope: design decision EQ-006. Only the two scoped code files were created, plus
this document. No existing file, test or project setting was changed.

## Files

| File | Change |
| --- | --- |
| `WIP/Wip.Tholdem.Agents/Equity/EquityThresholdPolicy.cs` | New: `EquityDecision`, sealed `EquityCallDecision`, reason constants and `DecideCall`. |
| `WIP/Wip.Tholdem.Tests/Equity/EquityThresholdTests.cs` | New: the four contracted tests. |
| `docs/goal-tholdem-enhancement/round-6/implementation-equity-threshold.md` | New: this evidence record. |

## Contract as implemented

- `public enum EquityDecision { Call, Fold, Uncertain }` in
  `Wip.Tholdem.Agents.Equity`.
- `public sealed record EquityCallDecision(EquityDecision Decision, string Reason,
  double LowerBound, double UpperBound, double BreakEvenEquity)`.
- `public static class EquityThresholdPolicy` with
  `CallReason = "call-interval-above-threshold"`,
  `FoldReason = "fold-interval-below-threshold"` and
  `UncertainReason = "uncertain-interval-straddles-threshold"`.
- `DecideCall(EquityEstimate equity, double breakEvenEquity)` first calls
  `ArgumentNullException.ThrowIfNull(equity)`, then rejects a threshold outside the closed
  interval [0, 1] with `ArgumentOutOfRangeException`. The guard is written as
  `!(breakEvenEquity >= 0.0 && breakEvenEquity <= 1.0)`, so `NaN` is rejected as well.
- Decision uses `equity.ExpectedPotShareInterval` and strict comparisons only:
  `Low > breakEven` -> Call; `High < breakEven` -> Fold; everything else, including
  `Low == breakEven` or `High == breakEven`, -> Uncertain.
- Every returned record carries the interval bounds exactly as estimated and the supplied
  break-even threshold. No clock, culture, randomness or mutable state is used.

## Tests added

All four tests are in `Wip.Tholdem.Tests.Equity.EquityThresholdTests`. A private helper builds an
`EquityEstimate` with eleven positional arguments (samples 1000, three opponents, method
`"test"`, standard error `(high - low) / 3.92`) and sets `ExpectedPotShareInterval` to the exact
low/high bounds under test.

| Test | Interval | Threshold | Asserted outcome |
| --- | --- | --- | --- |
| `DecideCall_GivenIntervalAboveThreshold_ReturnsCall` | (0.60, 0.70) | 0.50 | Call / `CallReason` |
| `DecideCall_GivenIntervalBelowThreshold_ReturnsFold` | (0.20, 0.30) | 0.50 | Fold / `FoldReason` |
| `DecideCall_GivenIntervalStraddles_ReturnsUncertain` | (0.45, 0.55) | 0.50 | Uncertain / `UncertainReason` |
| `DecideCall_GivenBoundaryEquality_IsUncertainAndInvalidThresholdThrows` | (0.50, 0.60) | 0.50, then 1.5 | Uncertain / `UncertainReason`; `ArgumentOutOfRangeException` |

Each test also asserts the returned lower bound, upper bound and break-even value. The fourth test
pins the boundary rule: a lower bound exactly equal to the threshold is not a call.

## Test-first: observed red output

The tests were written first and `EquityThresholdPolicy.DecideCall` was stubbed with
`throw new NotImplementedException()` so the test assembly compiled. The stub build succeeded:

```text
Build succeeded.
    14 Warning(s)
    0 Error(s)
```

The 14 warnings are pre-existing in unrelated Host and Learning/Training files. The filtered run
exited 1 and observed:

```text
FAIL EquityThresholdTests.DecideCall_GivenBoundaryEquality_IsUncertainAndInvalidThresholdThrows: NotImplementedException: The method or operation is not implemented.
FAIL EquityThresholdTests.DecideCall_GivenIntervalAboveThreshold_ReturnsCall: NotImplementedException: The method or operation is not implemented.
FAIL EquityThresholdTests.DecideCall_GivenIntervalBelowThreshold_ReturnsFold: NotImplementedException: The method or operation is not implemented.
FAIL EquityThresholdTests.DecideCall_GivenIntervalStraddles_ReturnsUncertain: NotImplementedException: The method or operation is not implemented.
TOTAL 4 PASSED 0 FAILED 4 SKIPPED 0
```

The test file was not changed after this red run; the implementation replaced the stub.

## Observed green output

Required build after the implementation:

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

New test class (exit 0):

```text
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Equity.EquityThresholdTests
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0
```

Full suite (exit 0). The expected count is the 513 pre-existing tests plus the four new tests:

```text
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 517 PASSED 517 FAILED 0 SKIPPED 0
```

## No stub remains

A search for `NotImplementedException` under `WIP/Wip.Tholdem.Agents/Equity` returned no
matches. Both new C# files are UTF-8 without BOM, contain no null bytes, and the existing suite is
unchanged and green.

## Commands used

```text
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Equity.EquityThresholdTests
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```
