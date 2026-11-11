# Round 5 implementation — typed hand-category score and N-opponent equity tool

Status: complete. Scope: design sections 2 and 4, plus the three `HandCategoryScoreTests` and four
`EquityToolOpponentsTests` in section 5. No existing test, `MonteCarloEquity`, `EquityResult`,
`HandStrength` or tool-catalog contract was changed.

## Files

| File | Change |
| --- | --- |
| `WIP/Wip.Tholdem.Agents/Equity/HandCategoryScore.cs` | New: `readonly record struct` wrapper over the internal `HandStrength.Estimate`. |
| `WIP/Wip.Tholdem.Agents/Runtime/EquityTool.cs` | Edit: appended `Opponents` to the request and `ExpectedPotShare`/`Opponents` to the result; dispatch to legacy or multiway estimator. |
| `WIP/Wip.Tholdem.Agents/Runtime/TholdemToolCatalog.cs` | Edit: optional `opponents` integer (minimum 1) in the equity schema and updated description. |
| `WIP/Wip.Tholdem.Tests/Equity/HandCategoryScoreTests.cs` | New: the three contracted tests. |
| `WIP/Wip.Tholdem.Tests/Equity/EquityToolOpponentsTests.cs` | New: the four contracted tests. |

## Test-first: observed red output

The two test classes were written first. To compile them, `HandCategoryScore.FromHeuristic` was
stubbed with `throw new NotImplementedException(...)`, the request/result records gained the new
members, and `ExecuteAsync` and the catalog were left at their old behavior. The stubbed build
succeeded with 0 errors and the equity class run produced:

```text
FAIL EquityToolOpponentsTests.EquityTool_GivenCatalog_DeclaresOptionalOpponentsInteger: TrueException: Assert.True() Failure
Expected: True
Actual:   False
FAIL EquityToolOpponentsTests.EquityTool_GivenDefaultOpponents_MatchesLegacyEstimate: EqualException: Assert.Equal() Failure: Values differ
Expected: 0.84175
Actual:   0
FAIL EquityToolOpponentsTests.EquityTool_GivenSixOpponents_Throws: ThrowsException: Assert.Throws() Failure: No exception was thrown
Expected: typeof(System.ArgumentOutOfRangeException)
FAIL EquityToolOpponentsTests.EquityTool_GivenThreeOpponents_ReturnsPotShareAndCounts: EqualException: Assert.Equal() Failure: Values differ
Expected: 3
Actual:   1
FAIL HandCategoryScoreTests.FromHeuristic_GivenAcesPreflop_ReturnsUnitIntervalScore: NotImplementedException: HandCategoryScore.FromHeuristic is not implemented yet.
FAIL HandCategoryScoreTests.FromHeuristic_GivenFullHouse_ReturnsHighScore: NotImplementedException: HandCategoryScore.FromHeuristic is not implemented yet.
TOTAL 12 PASSED 6 FAILED 6 SKIPPED 0
```

Command:

```text
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Equity
```

Six of the seven new tests were red. The reflection test passed during the red run because the stub
type already had no conversion operators; it remains the guard for the implemented type. The
`Expected: 0.84175 / Actual: 0` failure is the legacy heads-up path returning the old five fields
with `ExpectedPotShare` still at its default, and the `Expected: 3 / Actual: 1` failure is the
three-opponent request still using the single-opponent estimator.

## Implementation notes

- `HandCategoryScore` is exactly `public readonly record struct HandCategoryScore(double Score)`.
  `FromHeuristic` returns `new(HandStrength.Estimate(hole, board))`; the internal heuristic is in
  the same assembly. There is no implicit or explicit conversion to `double` or
  `EquityEstimate`, and the XML documentation states that the score is a relative rank score, never
  showdown equity or a probability.
- `EquityToolRequest(HoleCards, Board, Samples = 2000, Seed = null, Opponents = 1)` appends
  `Opponents` after `Seed`, so positional callers using the first four members still compile.
- `EquityToolResult(Equity, Samples, Wins, Ties, Losses, ExpectedPotShare = 0, Opponents = 1)`
  appends the two optional members at the end.
- `ExecuteAsync` rejects `request.Opponents` outside 1..5 with `ArgumentOutOfRangeException`
  before parsing or sampling. For `Opponents == 1` it calls
  `MonteCarloEquity.Estimate(holeCards, board, seed, samples)` and maps the five legacy values
  unchanged, with `ExpectedPotShare = legacy.Equity` and `Opponents = 1`. For 2..5 it calls
  `MultiwayEquity.Estimate(holeCards, board, opponents, seed, samples)` and maps
  `Equity = ExpectedPotShare = estimate.ExpectedPotShare`, `Samples`, `Wins`, `Ties`,
  `Losses` and `Opponents = estimate.OpponentCount`. Sampling keeps the existing request budget
  clamp.
- `TholdemToolCatalog` keeps the same four definitions, names, required fields and permission
  descriptors. The equity definition description now says "one to five random opponent hands" and
  its properties include `opponents` as `Integer(1)`; it is not added to `required`.
- The new tool test also cross-checks the three-opponent result against the same-seed
  `MultiwayEquity.Estimate` call, so the mapping of equity and counts is directly observed rather
  than inferred from ranges.

## Observed green output

Required build:

```text
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

New and existing equity tests (filter selects `Tests.Equity`):

```text
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Equity
TOTAL 12 PASSED 12 FAILED 0 SKIPPED 0
```

Full suite:

```text
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 507 PASSED 507 FAILED 0 SKIPPED 0
```

507 = the 500 pre-existing tests plus the 7 new tests (the 5 pre-existing
`MultiwayEquityTests` are included in the 500). Both runs exited 0, so the existing
`ToolContractTests` catalog assertions and the runtime heads-up tests also passed with the new
optional schema property and result members.

## No stub remains

```text
Select-String -Path WIP/Wip.Tholdem.Agents/Equity/HandCategoryScore.cs,
  WIP/Wip.Tholdem.Agents/Runtime/EquityTool.cs,
  WIP/Wip.Tholdem.Agents/Runtime/TholdemToolCatalog.cs -Pattern NotImplementedException
```

Observed: no matches. All five scoped source/test files are UTF-8 without BOM (checked first bytes
`75 73 69`, no `EF BB BF`) and contain no null bytes. Scoped `git status --porcelain` shows only
the two intended tracked edits (`EquityTool.cs`, `TholdemToolCatalog.cs`), the new
`HandCategoryScore.cs`, the new `WIP/Wip.Tholdem.Tests/Equity/` tests and
`docs/goal-tholdem-enhancement/round-5/`.
