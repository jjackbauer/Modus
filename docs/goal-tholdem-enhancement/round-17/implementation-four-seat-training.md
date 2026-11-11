# Round 17 — four-seat training/promotion loop (implementer)

Scope: the additive four-seat training/promotion path specified in
`docs/goal-tholdem-enhancement/round-17/design.md`, section 1 and the four tests in section 2.
Only the three repository files listed below were created. The v1 `TrainingLoop`, `TrainingOptions`,
`TrainCommand`, evaluator, gate, verifier, persistence stores and every pre-existing test are
untouched. The design's section-3 `train4` CLI (`Host/Commands/Train4Command.cs` and the
`TholdemCli` edit) is outside this task's explicit file scope and is not implemented here; the
library entry point below is the additive loop the CLI would call.

## Files

| File | Change |
|---|---|
| `WIP/Wip.Tholdem.Agents/Evaluation/FourSeatTraining.cs` | New: options, iteration/run records and the loop. |
| `WIP/Wip.Tholdem.Tests/Evaluation/FourSeatTrainingTests.cs` | New: four facts. |
| `docs/goal-tholdem-enhancement/round-17/implementation-four-seat-training.md` | New: this record. |

## Public surface

The records and `Run` match the design. The only public constants are `DirectoryName` and
`ChampionFileName`; outcome/verdict/reason spellings are private constants.

Defaults exactly as designed: `Game = GameConfiguration.FourSeatCash()`,
`Pool = FrozenOpponentPool.SelectionV1`, `Compositions = ["primary-mixed"]`,
`StartingGenome = new StrategyGenome()`, and the default proposer is a fresh
`DeterministicStrategyProposer` driven by `SessionAnalysis(0, 0, 0, 1, 0, null)` (neutral bb/100,
aggression 1.0). The anchor composition is `"three-tag"` unless the primary list is exactly
`["three-tag"]`, in which case it is `"primary-mixed"`.

Refusals happen before any directory or file is written:

- `ArgumentOutOfRangeException`: negative iterations, non-positive selection/holdout block or hand
  counts, non-finite/negative margin, confidence outside (0, 1), non-finite/negative anchor
  tolerance.
- `ArgumentException`: empty data directory, non-four-seat game, empty/duplicate/unknown primary
  composition, unknown anchor composition.

## Iteration

1. The champion starts as `StrategyArtifact.Create(startingGenome, 0, null)`; its artifact is
   persisted so the id is resolvable on disk.
2. The challenger genome is `Proposer(champion.Genome, iteration)` or the default deterministic
   proposer, then `StrategyArtifact.Create(..., iteration, champion)` clamps it and derives the
   version/parent/hash-addressed id. The challenger artifact is persisted before evaluation.
3. Fresh per-iteration seed domains are derived from `SeedDerivation.Derive(seed, iteration, 0,
   (int)purpose, 0x53454154)`: training (count 1), selection (`SelectionBlocks`), holdout
   (`HoldoutBlocks`), audit (count 1). They are disjoint by purpose salt and iteration.
4. The population evaluator always consumes `plan.Holdout` as its evaluation domain, so the
   selection plan carries a Holdout-purpose domain built from the selection base seed and
   `SelectionBlocks`, while the manifest's Selection-purpose domain records the same base and size.
   The holdout and anchor plans carry the strictly disjoint Holdout-purpose block. This is the same
   contract-preserving construction used by `SearchAblation`; all four required purposes are
   declared in every plan.
5. Selection is challenger vs champion with `SeatPermutationMode.FocalRotations` and
   `SelectionHandsPerMatch`; its canonical `PopulationEvaluationReport` is written to
   `reports/iter-<n>-selection.json`.
6. If `selectionReport.Estimate.MeanBbPer100 <= 0` the row is `rejected-selection` with verdict
   `Rejected` and reason `selection-mean-not-positive`; no holdout or anchor simulation is run and
   no holdout/decision file is written.
7. Otherwise the holdout report (primary compositions) and the anchor report (anchor composition)
   are evaluated against the holdout seed block, then `PopulationPromotionGate.Decide(holdout,
   anchor, holdoutPlan, anchorPlan)` is called. The holdout report and a canonical decision document
   are written. `PlannedCandidates` is 1, so the gate applies the unadjusted confidence and its
   margin/anchor tolerance come from `MarginBbPer100`/`AnchorToleranceBbPer100`.
8. Only `PromotionVerdict.VerifiedImprovement` replaces the in-memory champion and writes
   `four-seat/active-champion.json`; every other verdict yields `rejected-holdout`. The champion is
   never read back from a v1 pointer.

The iteration row's `ChampionId` is the pre-iteration incumbent, matching the v1 ledger convention;
on promotion `FinalChampionId` advances to the challenger id.

## Run hash

`RunHash = StrategyArtifact.ComputeHash(canonical summary)` where the summary is
`{"schema":"tholdem.four-seat-training-run.v1","seed":...,"iterations":<count>,"finalChampionId":"...","rows":[...]}`
and each row is `iteration, championId, challengerId, outcome, selectionMeanBbPer100, verdict,
reason`. Canonical numbers use `CanonicalJsonWriter.Number` (invariant `R` spelling); there is no
clock, GUID, culture or ambient randomness anywhere in the run.

## Persistence layout

```text
<data>/four-seat/
  strategies/<artifact-id>.json          champion and every challenger, canonical artifact JSON
  reports/iter-<n>-selection.json        always after a selection evaluation
  reports/iter-<n>-holdout.json          only when selection mean > 0
  reports/iter-<n>-decision.json         only when selection mean > 0
  active-champion.json                   only after VerifiedImprovement
```

All writes use `UTF8Encoding(encoderShouldEmitUTF8Identifier: false)`. No file outside
`<data>/four-seat/` is created, and no v1 `active-champion.json`, `ledger.json`,
`strategies/` or `evaluations/` path is read or written.

## Red (observed before implementation)

Stub: the three records plus `Run` throwing `NotImplementedException("stub:
FourSeatTraining.Run")`.

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
```

Observed: `Build succeeded.`, `14 Warning(s)`, `0 Error(s)` (all warnings in pre-existing files).

```powershell
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Evaluation.FourSeatTrainingTests
```

Observed (exit code 1):

```text
FAIL FourSeatTrainingTests.Run_GivenInertChallenger_RejectsAtSelectionAndLeavesChampion: NotImplementedException: stub: FourSeatTraining.Run
FAIL FourSeatTrainingTests.Run_GivenInvalidOptions_Throws: ThrowsException: Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(System.ArgumentOutOfRangeException)
Actual:   typeof(System.NotImplementedException)
FAIL FourSeatTrainingTests.Run_GivenOneIteration_WritesOnlyFourSeatArtifacts: NotImplementedException: stub: FourSeatTraining.Run
FAIL FourSeatTrainingTests.Run_GivenSameOptions_IsByteIdentical: NotImplementedException: stub: FourSeatTraining.Run
TOTAL 4 PASSED 0 FAILED 4 SKIPPED 0
```

## Green (observed after implementation)

Build command as above: `Build succeeded.`, `14 Warning(s)`, `0 Error(s)`; no warning names either
new file.

Targeted class (exit 0):

```text
observed four-seat inert: outcome=rejected-selection selectionMeanBbPer100=0 verdict=Rejected reason=selection-mean-not-positive finalChampionId=tholdem-strategy-v1-f100e793 challengerId=tholdem-strategy-v2-dcd95518 runHash=621eb6c4c5fbf8dfa34c265695828526e03b3f1839e48a2bc5fd723f247d7d13
observed four-seat layout: four-seat/reports/iter-1-selection.json, four-seat/strategies/tholdem-strategy-v1-f100e793.json, four-seat/strategies/tholdem-strategy-v2-b1b55149.json
observed four-seat repeat: runHash=4064f00ceb491c168af0ccb6b6e5511c52ee719723190f329c21c5587884578e files=3
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0
```

The inert scenario is seed 17, one iteration, one selection block/hand and one holdout block/hand:
the prompt-only challenger is behaviourally identical, so the paired selection estimate is exactly
0 bb/100. It is rejected at selection with the champion unchanged
(`tholdem-strategy-v1-f100e793`), only two strategies and the selection report exist, and no
holdout, decision or `four-seat/active-champion.json` file is written. The repeat scenario uses the
same options but the default proposer and reproduces the same `RunHash` across two directories with
all file bytes equal.

Full suite (exit 0):

```text
TOTAL 574 PASSED 574 FAILED 0 SKIPPED 0
```

The new class reports 4 passed, so the pre-existing suite contributes 570 facts, matching the
recorded round-14 baseline.

### Holdout-path smoke check (scratch, not part of the suite)

The four designed facts all stop at the selection stage, so a scratch file-based app outside the
repository ran the library with seed `202617`, 10 iterations, 8 selection blocks x 4 hands and 16
holdout blocks x 4 hands. It was deleted after the observation. Excerpt:

```text
iter=1 outcome=rejected-selection selection=-21.171874999999996 verdict=Rejected reason=selection-mean-not-positive
iter=2 outcome=rejected-selection selection=-2.34375 verdict=Rejected reason=selection-mean-not-positive
iter=3 outcome=rejected-holdout selection=220.859375 verdict=Rejected reason=no-improvement
iter=4 outcome=rejected-selection selection=-1.5625 verdict=Rejected reason=selection-mean-not-positive
iter=5 outcome=rejected-holdout selection=287.65625 verdict=Unsafe reason=unsafe-anchor-regression
...
runHash=37d30f84082518a0213f20402bc448c3d3ccdf11edf3fcd7cae74903f0a991d9
pointerExists=False
holdoutReports=2
decisionReports=2
totalFiles=25
```

Both positive-selection iterations reached the holdout + anchor + gate path and wrote their holdout
and decision reports. No verdict was `VerifiedImprovement`, so no pointer was written; the file count
is 11 strategies + 10 selection reports + 2 holdout reports + 2 decision reports.

## Tests added

`WIP/Wip.Tholdem.Tests/Evaluation/FourSeatTrainingTests.cs`:

1. `Run_GivenInertChallenger_RejectsAtSelectionAndLeavesChampion` — prompt-only proposer gives a
   selection mean of 0; outcome `rejected-selection`; champion id unchanged; both strategy files and
   the selection report exist; no holdout/decision/pointer file; no v1 root pointer.
2. `Run_GivenSameOptions_IsByteIdentical` — two data directories, equal `RunHash`, equal relative
   file sets and byte-equal file contents.
3. `Run_GivenInvalidOptions_Throws` — iterations -1 and selection blocks 0 throw
   `ArgumentOutOfRangeException`; an unknown composition throws `ArgumentException`; no
   `four-seat/` directory is created.
4. `Run_GivenOneIteration_WritesOnlyFourSeatArtifacts` — every file under the data directory is under
   `four-seat/`, none carries a UTF-8 BOM, both subdirectories exist, and no v1 pointer, ledger or
   v1 `strategies`/`evaluations` directory exists.

## No v1 pointer

The new code references only `four-seat/strategies`, `four-seat/reports` and the isolated
`four-seat/active-champion.json`. Tests 1 and 4 assert that `<data>/active-champion.json`,
`<data>/ledger.json`, `<data>/strategies` and `<data>/evaluations` do not exist after a run. The
pointer is written only on `VerifiedImprovement`, and the inert run observed above wrote none.

## Commands run

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Evaluation.FourSeatTrainingTests
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

No `dotnet test` was run. No repository file outside the three files in the Files table was
created or edited; the smoke-check app lived in the OS temp directory and was deleted.
