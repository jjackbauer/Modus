# Round 2 implementation — paired population evaluator and report

Status: complete. Scope: this implementation adds the two production files and the test file from
the design's sections 3, 4 and 5, plus this evidence document. No existing file was modified.

## Files

| File | Change |
| --- | --- |
| `WIP/Wip.Tholdem.Agents/Evaluation/PopulationEvaluationReport.cs` | New: report records, fixed-key-order canonical JSON, content hash and seed fingerprint. |
| `WIP/Wip.Tholdem.Agents/Evaluation/PopulationMatchEvaluator.cs` | New: validation, focal-seat assignments, paired simulation, estimates, tail and net chips. |
| `WIP/Wip.Tholdem.Tests/Evaluation/PopulationEvaluatorTests.cs` | New: the six evaluator tests. |

## Test-first: observed red output

The production surface was stubbed with `throw new NotImplementedException()` so the new test class
compiled, then the class was run. Observed output:

```text
FAIL PopulationEvaluatorTests.Evaluate_GivenCompositionMismatch_Throws: ThrowsException: Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(System.ArgumentException)
Actual:   typeof(System.NotImplementedException)
FAIL PopulationEvaluatorTests.Evaluate_GivenFocalRotations_ReportsEveryFocalSeat: NotImplementedException: The method or operation is not implemented.
FAIL PopulationEvaluatorTests.Evaluate_GivenKnownDominatedControl_ReportsPositiveLowerBound: NotImplementedException: The method or operation is not implemented.
FAIL PopulationEvaluatorTests.Evaluate_GivenPolicyVersusItself_ReportsExactlyZero: NotImplementedException: The method or operation is not implemented.
FAIL PopulationEvaluatorTests.Evaluate_GivenPoolMismatch_Throws: ThrowsException: Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(System.ArgumentException)
Actual:   typeof(System.NotImplementedException)
FAIL PopulationEvaluatorTests.Evaluate_GivenSameInputs_IsByteIdentical: NotImplementedException: The method or operation is not implemented.
TOTAL 6 PASSED 0 FAILED 6 SKIPPED 0
```

Command:

```text
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Evaluation.PopulationEvaluatorTests
```

## Implementation notes

### Report (section 3)

- Records: `EvaluationUnit`, `CompositionEstimate`, `SeatEstimate`, `TailRisk`,
  `PopulationEvaluationReport`, and the `SeatPermutationMode` enum.
- Canonical JSON schema is `tholdem.population-report.v2` and the top-level key order is
  `schema, challengerId, incumbentId, challengerPolicyHash, incumbentPolicyHash, planHash,
  gameConfigHash, opponentPoolId, opponentPoolHash, evaluatorVersion, seedBlocks,
  handsPerSeatMatch, seatPermutationMode, rotationsPerBlock, estimate, units, perComposition,
  perSeat, tail, challengerNetChips, incumbentNetChips`.
- Estimate JSON key order is `meanBbPer100, lowerBoundBbPer100, upperBoundBbPer100,
  confidenceLevel, method, unitCount, standardError, normalLowerBoundBbPer100,
  bootstrapLowerBoundBbPer100`.
- Unit keys are `compositionId, blockIndex, seedFingerprint, hands, meanBbPer100`;
  composition keys are `compositionId, estimate, challengerNetChips, incumbentNetChips`;
  seat keys are `seat, estimate, hands`; tail keys are `worstBlockBbPer100,
  p05BlockBbPer100, worstHandBbPer100`.
- `ReportHash = StrategyArtifact.ComputeHash(ToCanonicalJson())`.
- `SeedFingerprint(blockSeed)` is the first 16 lowercase hex characters of SHA-256 over the
  invariant decimal spelling of the seed.
- The report contains no clock, GUID, ambient randomness or culture-dependent formatting; numbers
  use `CanonicalJsonWriter`, and the only culture use is
  `CultureInfo.InvariantCulture` for the seed's decimal spelling.

### Evaluator (section 4)

- Validation order and stable codes: `agent-id-invalid` for empty or identical ids;
  `policy-hash-invalid` for anything other than 64 lowercase hex characters;
  `seats-unsupported` unless the plan game declares exactly four seats; `pool-mismatch`
  when the pool id differs from `Plan.OpponentPoolId`; `composition-mismatch` when a plan
  composition does not resolve in the pool or does not name exactly three opponents.
- For composition `ci` and block `b`: `blockSeed = SeedDerivation.Derive(Holdout.Seeds[b],
  0xE7A1 + ci, 1, 0, 0)` and `options = Plan.Game.ToSimulationOptions(blockSeed,
  Plan.HandsPerSeatMatch)`.
- `FocalRotations` builds 4 assignments: each focal seat once, with the composition's opponents
  in composition order in the remaining seats. `FullFactorial` builds 24: each focal seat
  crossed with all 6 opponent orders (duplicate ids still produce 6 orders).
- Every assignment builds fresh challenger and incumbent tables (the challenger/incumbent at the
  focal seat, fresh pool agents for the opponents), runs `MatchSimulator.RunDetailed` twice with
  the same options, and compares the two `HandSeeds` sequences; a difference throws
  `InvalidOperationException("paired-deck-mismatch")`.
- Per hand, `diff = (A.FinalStacks[focal] - start[focal]) - (B.FinalStacks[focal] -
  start[focal])`, with `start[focal] = Plan.Game.StartingStacks?[focal] ??
  Plan.Game.StartingStack`.
- A block unit mean is the mean of its assignments' bb/100 values; equivalently
  `sum(focal chip diffs) / (assignments * hands) / BigBlind * 100`. Its `Hands` is
  `assignments * Plan.HandsPerSeatMatch` (80 for 4 rotations x 20 hands).
- `Estimate` averages the composition block means into one block mean (n = `SeedBlocks`);
  `PerComposition` uses each composition's block means (n = `SeedBlocks`); `PerSeat` uses one
  unit per block per focal seat (one composition seat assignment with focal rotations, the mean of
  the six opponent orders with full factorial), so every `UnitCount` is `SeedBlocks`.
  `SeatEstimate.Hands` is the total contributing hand count:
  `blocks * compositions * ordersPerFocalSeat * handsPerSeatMatch`.
- All three estimate families call `BlockStatistics.Compute(units, Plan.ConfidenceLevel,
  bootstrapSeed)` with the documented constant
  `bootstrapSeed = SeedDerivation.Derive(Holdout.Seeds[0], 0xB007, 1, 0, 0)`.
- `Tail` is the minimum overall block mean, the linearly interpolated 5th percentile of the
  overall block means, and the minimum single-hand focal diff / BigBlind * 100.
- `ChallengerNetChips` / `IncumbentNetChips` are the sums of focal net chips over every
  challenger / incumbent hand. `RotationsPerBlock` is 4 or 24.

## Dominated control observation

The control is the round-two test-only pair from the design: 96 blocks, 20 hands, composition
`three-tag`, selection pool. The orientation follows the pilot evidence supplied for this round
(the fold-only policy was the stronger side against the call-any policy in the 96-block pilot), so
fold-only is the challenger and call-any the incumbent. On the evaluator's fixed seed family
(holdout domain base seed 500, 96 blocks) the observed report estimate is:

```text
mean  = 97.51041666666667 bb/100
lower = 57.95538331320324 bb/100
upper = 139.69557291666663 bb/100
```

Both assertions (`MeanBbPer100 > 0` and `LowerBoundBbPer100 > 0`) pass without weakening.

## Observed green output

Build (clean rebuild; the required incremental `-c Release` command was also run and exited 0,
reporting `0 Warning(s)` / `0 Error(s)` because every project was current):

```text
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release -t:Rebuild
...
Build succeeded.
    16 Warning(s)
    0 Error(s)
```

None of the 16 warnings is from the three new files; they are the pre-existing nullable-reference
and xUnit-analyzer warnings in the Learning/Training tests.

New test class:

```text
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Evaluation.PopulationEvaluatorTests
dominated-control mean=97.51041666666667 lower=57.95538331320324 upper=139.69557291666663 units=96 blocks=96
TOTAL 6 PASSED 6 FAILED 0 SKIPPED 0
```

Full suite:

```text
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 484 PASSED 484 FAILED 0 SKIPPED 0
```

484 = the pre-existing 478 tests plus the 6 new tests. Both suite commands exited 0.

## No stub remains

```text
Select-String -Path WIP/Wip.Tholdem.Agents/Evaluation/PopulationEvaluationReport.cs,
  WIP/Wip.Tholdem.Agents/Evaluation/PopulationMatchEvaluator.cs,
  WIP/Wip.Tholdem.Tests/Evaluation/PopulationEvaluatorTests.cs -Pattern NotImplementedException
```

Observed: no matches. The three new files are UTF-8 without BOM and contain no null bytes. The
canonical key order is asserted by `Evaluate_GivenPolicyVersusItself_ReportsExactlyZero`, and the
byte identity test re-derives `ReportHash` from the canonical JSON.
