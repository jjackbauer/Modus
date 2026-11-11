# Round 2 implementation — frozen opponent pool and block statistics

Status: complete. Scope: this implementation adds the two production files and the two test files
from the design's sections 1 and 2, plus this evidence document. No existing file was modified.

## Files

| File | Change |
| --- | --- |
| `WIP/Wip.Tholdem.Agents/Evaluation/OpponentPool.cs` | New: five-archetype frozen selection and holdout pools. |
| `WIP/Wip.Tholdem.Agents/Evaluation/BlockStatistics.cs` | New: pure block-level normal + bootstrap estimate. |
| `WIP/Wip.Tholdem.Tests/Evaluation/OpponentPoolTests.cs` | New: six pool tests. |
| `WIP/Wip.Tholdem.Tests/Evaluation/BlockStatisticsTests.cs` | New: four statistics tests. |

## Test-first: observed red output

The production members were stubbed with `throw new NotImplementedException()` so the tests
compiled, then the two new test classes were run. Observed output:

```text
FAIL OpponentPoolTests.Archetypes_GivenSameSeededTable_ShowDistinctActionFrequencies: NotImplementedException: The method or operation is not implemented.
FAIL OpponentPoolTests.Compositions_GivenEveryComposition_ReferencesOnlyOwnPoolOpponents: NotImplementedException: The method or operation is not implemented.
FAIL OpponentPoolTests.Create_GivenOpponentId_ReturnsAgentWithThatName: NotImplementedException: The method or operation is not implemented.
FAIL OpponentPoolTests.HoldoutV1_GivenSelectionPool_HasNoSharedOpponentIds: NotImplementedException: The method or operation is not implemented.
FAIL OpponentPoolTests.PoolHash_GivenSamePool_IsStable_AndHoldoutDiffers: NotImplementedException: The method or operation is not implemented.
FAIL OpponentPoolTests.SelectionV1_GivenPool_HasFiveDistinctArchetypesAndIds: NotImplementedException: The method or operation is not implemented.
TOTAL 6 PASSED 0 FAILED 6 SKIPPED 0
```

```text
FAIL BlockStatisticsTests.Compute_GivenConstantUnits_ReturnsZeroWidthInterval: NotImplementedException: The method or operation is not implemented.
FAIL BlockStatisticsTests.Compute_GivenSameInputs_IsDeterministic: NotImplementedException: The method or operation is not implemented.
FAIL BlockStatisticsTests.Compute_GivenSyntheticNormalDatasets_CoverageWithinCalibrationBand: NotImplementedException: The method or operation is not implemented.
FAIL BlockStatisticsTests.NormalQuantile_Given975_IsWithin1e9Of1960: NotImplementedException: The method or operation is not implemented.
TOTAL 4 PASSED 0 FAILED 4 SKIPPED 0
```

## Implementation notes

### Opponent pool (section 1)

- Five selection opponents, in enum order: `tight-passive` (0.88, 0.55, 0.00, 1.0),
  `tight-aggressive` (0.72, 0.45, 0.12, 1.0), `loose-aggressive` (0.45, 0.30, 0.40, 1.6),
  `calling-station` (0.97, 0.05, 0.00, 1.0), `adversarial-raiser` (0.30, 0.40, 0.30, 2.4).
- Five holdout opponents with disjoint ids and different genomes:
  `holdout-tp` (0.84, 0.58, 0.02, 1.1), `holdout-tag` (0.76, 0.48, 0.09, 0.9),
  `holdout-lag` (0.49, 0.33, 0.36, 1.5), `holdout-station` (0.95, 0.08, 0.01, 1.05),
  `holdout-adv` (0.34, 0.43, 0.27, 2.2).
- Four stable composition ids in both pools: `primary-mixed`, `three-tag`,
  `three-station`, `pressure`; every composition holds three own-pool ids, including the
  repeated-opponent compositions.
- Canonical JSON uses the design's fixed key order and `CanonicalJsonWriter` number/string
  formatting. `PoolHash = StrategyArtifact.ComputeHash(ToCanonicalJson())`, computed once in the
  constructor. `Create` returns `new StrategyAgent(spec.Genome, spec.OpponentId)`.

### Block statistics (section 2)

- `Compute` is a pure function of `(unitMeans, confidenceLevel, bootstrapSeed, bootstrapSamples)`:
  no clock, no culture, no ambient randomness.
- Normal interval: sample standard deviation, `mean +/- z * sd/sqrt(n)` with
  `z = NormalQuantile(0.5 + confidence/2)`.
- Bootstrap interval: resample whole units with the assembly-internal
  `Wip.Tholdem.Agents.Randomness.Xorshift64Star`, sort the resampled means, then use
  linearly interpolated `alpha/2` and `1 - alpha/2` percentiles.
- Reported `LowerBound` is the minimum of the normal and bootstrap lowers; `UpperBound` is
  the maximum of the two uppers. Empty units and a non-positive unit count throw
  `ArgumentException`; one unit or all-equal units produce a zero-width interval at the mean.
  `Method` is `block-normal+bootstrap`.

#### NormalQuantile accuracy

The design sketch names Acklam. In this target framework `Math.Erfc` does not exist
(CS0117), so the erf-based Halley refinement cannot be used. Unrefined Acklam was measured at
`1.959963986120195` for 0.975: 1.58e-9 from the reference, which misses the required 1e-9.
The implementation therefore uses Wichura's AS241 (PPND16) rational approximation, which is an
inverse-normal approximation accurate to double precision:

- `NormalQuantile(0.975) = 1.9599639845400536`
- absolute error against `1.959963984540054`: 4.4e-16, well inside 1e-9.

#### Calibration observation

100 deterministic datasets of 24 standard normals (local xorshift64* + Box-Muller), 500 bootstrap
samples each, true mean 0: **91 of 100 conservative intervals contained 0** (0.91), inside the
required [0.90, 0.99] band.

## Pinned pool hashes

```text
SelectionV1.PoolHash = f5c1f058e45f21ba360fe9af349e6dc0de960e32130730c70c2ec289a7b44fc1
HoldoutV1.PoolHash   = 963e4523440ab31c7551863cb9a1350462c4a2ada17ad952cc1a499ca3842efb
```

`SelectionV1.PoolHash` is pinned as a literal in
`PoolHash_GivenSamePool_IsStable_AndHoldoutDiffers`. Both hashes are 64 lowercase hex
characters and differ; the test also recomputes the selection hash from `ToCanonicalJson()`.

## Observed archetype action frequencies

Each archetype was wrapped in the existing `Wip.Tholdem.Tests.Learning.RecordingAgent`, placed at
seat 0, and run for 60 hands with the pool's tight-aggressive opponent in the other three seats,
using the same seed (20261110) for all five runs. Observed counts from `Turns`:

| Archetype | Call actions | Aggressive actions (Bet + Raise + AllIn) |
| --- | ---: | ---: |
| TightPassive | 68 | 50 |
| TightAggressive | 85 | 62 |
| LooseAggressive | 72 | 100 |
| CallingStation | 179 | 0 |
| AdversarialRaiser | 0 | 123 |

Assertions satisfied: station calls 179 > TAG calls 85; LAG aggressive 100 > TAG aggressive 62;
adversarial aggressive 123 > TAG aggressive 62; tight-passive aggressive 50 < TAG aggressive 62.

## Observed green output

Build:

```text
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
...
Build succeeded.
    12 Warning(s)
    0 Error(s)
```

New test classes:

```text
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Evaluation.OpponentPoolTests
TOTAL 6 PASSED 6 FAILED 0 SKIPPED 0

dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Evaluation.BlockStatisticsTests
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0
```

Full suite:

```text
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 478 PASSED 478 FAILED 0 SKIPPED 0
```

478 = the pre-existing 468 tests plus the 10 new tests. Every suite command exited 0.

## No stub remains

```text
Select-String -Path WIP/Wip.Tholdem.Agents/Evaluation/OpponentPool.cs,
  WIP/Wip.Tholdem.Agents/Evaluation/BlockStatistics.cs -Pattern NotImplementedException
```

Observed: no matches. Both production files are UTF-8 without BOM; the new test files are too.
