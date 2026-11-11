# Implementation search ablation — equal-budget proposer ablation

Round 7, anchor 3: the reusable equal-budget proposer ablation. It compares four proposal arms at
identical candidate budgets across starting genomes and opponent tasks, and records the promotion
gate verdict for every arm and task. A null outcome is a valid result.

## Files

- `WIP/Wip.Tholdem.Agents/Evaluation/SearchAblation.cs` (new)
- `WIP/Wip.Tholdem.Tests/Evaluation/SearchAblationTests.cs` (new)
- `docs/goal-tholdem-enhancement/round-7/implementation-search-ablation.md` (new, this file)

No other file was edited. The evaluator, gate, verifier, plans, pool, genomes and every existing
test are unchanged.

## What the runner does

`SearchAblation.Run` validates the options before any simulation: 1–8 tasks with unique non-empty
ids and resolving compositions, a four-seat game, `CandidatesPerArm >= 4` and divisible by 4,
1–500 blocks, 1–100 hands per match, a finite non-negative margin and tolerance, confidence in
(0, 1), and a non-null pool. Refusals carry stable prefixes (`tasks-out-of-range`,
`task-id-duplicate`, `composition-mismatch`, `seats-unsupported`, ...).

Per task it builds three pre-registered plans with `EvaluationPlanV2.Create`, the supplied pool and
the task composition:

- selection plan: `SelectionBlocks` block seeds, `PlannedCandidates = CandidatesPerArm`,
  `SelectionHandsPerMatch`;
- holdout plan: `HoldoutBlocks` block seeds, `PlannedCandidates = CandidatesPerArm`,
  `HoldoutHandsPerMatch`;
- anchor plan: the same holdout block seeds as the holdout plan, anchor composition (`three-tag`
  when the task uses `primary-mixed`, else `primary-mixed`) and the same settings.

Base seeds are derived once per task and purpose from the run seed, so the selection and holdout
blocks cannot share a concrete seed; `SeedDomain.ValidateDisjoint` proves that for every plan. The
training and audit domains use one seed each.

### Seed-domain role mapping

`PopulationMatchEvaluator` always reads its deal seeds from `plan.Holdout`
(`EvaluationPlanV2.SeedBlocks => Holdout.Count`; the evaluator seeds each block from
`plan.Holdout.Seeds`). The runner therefore places the selection evaluation block in the selection
plan's `Holdout` role, and the holdout/anchor plans share the holdout evaluation block. Every plan
still declares the four required purposes: the `Selection`-purpose slot records a block of
`SelectionBlocks` seeds from the selection base seed, and training/audit are tiny. The observed
tiny-task means confirm the separation: Grid's best selection mean is 93.75 but its holdout mean is
29.375, so selection and holdout are not the same deals.

### Arms

Each arm evaluates exactly `CandidatesPerArm` candidates against the task start on the selection
plan (focal rotations, the supplied pool), via `PopulationMatchEvaluator.Evaluate` with distinct
challenger ids (`candidate-<arm>-<taskId>-<index>`), the incumbent id `task-start-<taskId>` and
64-hex `StrategyArtifact` policy hashes:

- **NoSearch**: the (clamped) start genome repeated `CandidatesPerArm` times.
- **Random**: one xorshift64* stream seeded from
  `SeedDerivation.Derive(options.Seed, taskIndex + 1, 100 + (int)arm, 0, 0)`; each candidate draws
  raise, call and bluff uniformly in [0, 1) and sizing uniformly in [0.25, 3), preserving the
  start's prompt and skills.
- **Grid**: candidate `i` nudges parameter `i % 4` by +0.1 when `i < 4` and by -0.1 when `i >= 4`.
- **Greedy**: `CandidatesPerArm / 4` rounds of four +0.1 neighbours (one per parameter) from the
  current best; the current best becomes the best of that round's four. At 4 candidates this is one
  round; at 8 it is the design's two rounds. Exactly `CandidatesPerArm` evaluations happen.

Every generated genome is clamped into the genome domain (raise/call/bluff in [0, 1], sizing in
[0.25, 3]).

The best candidate is the highest selection mean, ties to the lowest index. That genome is
re-evaluated against the start on the holdout plan and the anchor plan (challenger
`search-best-<arm>-<taskId>`), and `PopulationPromotionGate.Decide(holdout, anchor, holdoutPlan,
anchorPlan)` supplies the verdict and reason. The result records the best selection mean, holdout
mean, holdout lower bound, 64-hex best policy hash, verdict and reason.

### Budget

`SearchBudget.SelectionHandsPerArmPerTask` counts focal-seat hands exactly as the evaluator counts
them: `CandidatesPerArm × SelectionBlocks × 4 rotations × SelectionHandsPerMatch`.
`TotalHands` adds the best candidate's holdout and anchor evaluations for every arm and task:
`tasks × 4 × (selection hands + 2 × HoldoutBlocks × 4 × HoldoutHandsPerMatch)`.

For the tiny test task (4 candidates, 2 selection blocks × 2 hands, 2 holdout blocks × 2 hands):
`SelectionHandsPerArmPerTask = 4 × 2 × 4 × 2 = 64` and
`TotalHands = 1 × 4 × (64 + 2 × 16) = 384`.

### Outcome and canonical report

`SearchAblation.ClassifyOutcome` (public, called by `Run`) classifies the run-level outcome with
Greedy as the proposal process under test. A run is `benefit` only when Greedy has

1. at least two `VerifiedImprovement` results,
2. zero `Unsafe` results, and
3. strictly more `VerifiedImprovement` results than each of NoSearch, Random and Grid.

Every other pattern is `no-benefit`, which is a valid null result. The earlier any-arm rule was
replaced after the four-task run showed a single Random win alongside three Random `Unsafe` results
(see the correction section below).

The canonical JSON has the fixed key order `schema`, `seed`, `budget`, `taskIds`, `candidates`,
`results`, `outcome` and embeds the four genome parameters for every candidate.
`ReportHash = StrategyArtifact.ComputeHash(ToCanonicalJson())`. There is no clock, GUID, culture or
ambient randomness.

## Test-first red (initial stub runner, 5 tests)

The test file and a compiling stub runner (records, report properties and `Run` throwing
`NotImplementedException`) were created first. Build:

```
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
```

The stub build succeeded with 0 errors. Then:

```
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Evaluation.SearchAblationTests
```

Observed red:

```
FAIL SearchAblationTests.Run_GivenCandidates_AreInsideGenomeDomain: NotImplementedException: search-ablation stub
FAIL SearchAblationTests.Run_GivenInvalidOptions_Throws: ThrowsAnyException: Assert.ThrowsAny() Failure: Exception type was not compatible
Expected: typeof(System.ArgumentException)
Actual:   typeof(System.NotImplementedException)
FAIL SearchAblationTests.Run_GivenNoSearchArm_SelectionMeansAreZero: NotImplementedException: search-ablation stub
FAIL SearchAblationTests.Run_GivenOneTask_EveryArmEvaluatesTheSameCandidateCount: NotImplementedException: search-ablation stub
FAIL SearchAblationTests.Run_GivenSameOptions_IsByteIdentical: NotImplementedException: search-ablation stub
TOTAL 5 PASSED 0 FAILED 5 SKIPPED 0
```

## Green (initial implementation, 5 tests)

Build: `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release` — succeeded,
0 errors (the test project's pre-existing warnings are in unrelated files; none are in the new
files).

Filtered run (same command as the red run):

```
NoSearch selection means: 0, 0, 0, 0
Random selection means: 204.375, 1711.875, 1494.375, 704.375
Grid selection means: 0, 93.75, 73.75, 0
Greedy selection means: 0, 93.75, 73.75, 0
NoSearch best=0 holdout=0 lower=0 verdict=Rejected reason=no-improvement
Random best=1711.875 holdout=421.875 lower=-128.13989316155255 verdict=Unsafe reason=unsafe-anchor-regression
Grid best=93.75 holdout=29.375 lower=8.550382664261932 verdict=Unsafe reason=unsafe-anchor-regression
Greedy best=93.75 holdout=29.375 lower=8.550382664261932 verdict=Unsafe reason=unsafe-anchor-regression
outcome=no-benefit
TOTAL 5 PASSED 5 FAILED 0 SKIPPED 0
```

Full suite:

```
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 522 PASSED 522 FAILED 0 SKIPPED 0
```

522 = 517 pre-existing tests + 5 new tests; no pre-existing test regressed.

## Observed per-arm candidate selection means (tiny task)

| arm | index 0 | index 1 | index 2 | index 3 | best |
|---|---|---|---|---|---|
| NoSearch | 0 | 0 | 0 | 0 | 0 |
| Random | 204.375 | 1711.875 | 1494.375 | 704.375 | 1711.875 |
| Grid | 0 | 93.75 | 73.75 | 0 | 93.75 |
| Greedy | 0 | 93.75 | 73.75 | 0 | 93.75 |

At four candidates Grid and Greedy intentionally coincide: Greedy's first and only round is exactly
the Grid positive-nudge round. All four arms spent 64 selection focal hands and the run reports 384
total hands.

Observed verdicts: NoSearch `Rejected / no-improvement`; Random, Grid and Greedy
`Unsafe / unsafe-anchor-regression`; run outcome `no-benefit`. The tiny two-block plan produces a
null result, which is valid.

## Correction: Greedy outcome classification (follow-up)

The first implementation classified a run as `benefit` when any non-NoSearch arm beat NoSearch on
verified tasks. The real four-task experiment exposed why that is not a proposal-process claim: the
run was labelled `benefit` solely because Random won one task, while Random was `Unsafe` on three
others and Greedy won none. The rule was corrected before acceptance:

- the proposal arm under test is Greedy;
- `benefit` requires (a) at least two Greedy `VerifiedImprovement` results, (b) zero Greedy
  `Unsafe` results, and (c) strictly more Greedy verified results than each of NoSearch, Random and
  Grid;
- every other pattern is `no-benefit`.

The rule lives in `public static string ClassifyOutcome(IReadOnlyList<SearchArmResult>)` and `Run`
calls it. `Run` remains a pure function of its options.

Three synthetic tests cover the rule directly (no simulation):

- `ClassifyOutcome_GivenTwoGreedyImprovementsAndControlsBelow_ReturnsBenefit`
- `ClassifyOutcome_GivenOneGreedyImprovement_ReturnsNoBenefit`
- `ClassifyOutcome_GivenGreedyImprovementButGreedyUnsafe_ReturnsNoBenefit`

Red with the compiling `ClassifyOutcome` stub (same build and filtered-run commands):

```
FAIL SearchAblationTests.ClassifyOutcome_GivenGreedyImprovementButGreedyUnsafe_ReturnsNoBenefit: NotImplementedException: search-ablation outcome stub
FAIL SearchAblationTests.ClassifyOutcome_GivenOneGreedyImprovement_ReturnsNoBenefit: NotImplementedException: search-ablation outcome stub
FAIL SearchAblationTests.ClassifyOutcome_GivenTwoGreedyImprovementsAndControlsBelow_ReturnsBenefit: NotImplementedException: search-ablation outcome stub
FAIL SearchAblationTests.Run_GivenCandidates_AreInsideGenomeDomain: NotImplementedException: search-ablation outcome stub
FAIL SearchAblationTests.Run_GivenNoSearchArm_SelectionMeansAreZero: NotImplementedException: search-ablation outcome stub
FAIL SearchAblationTests.Run_GivenOneTask_EveryArmEvaluatesTheSameCandidateCount: NotImplementedException: search-ablation outcome stub
FAIL SearchAblationTests.Run_GivenSameOptions_IsByteIdentical: NotImplementedException: search-ablation outcome stub
TOTAL 8 PASSED 1 FAILED 7 SKIPPED 0
```

The one passing test is `Run_GivenInvalidOptions_Throws`: validation refuses the options before the
outcome classifier runs.

Green after implementing `ClassifyOutcome`:

```
NoSearch selection means: 0, 0, 0, 0
Random selection means: 204.375, 1711.875, 1494.375, 704.375
Grid selection means: 0, 93.75, 73.75, 0
Greedy selection means: 0, 93.75, 73.75, 0
NoSearch best=0 holdout=0 lower=0 verdict=Rejected reason=no-improvement
Random best=1711.875 holdout=421.875 lower=-128.13989316155255 verdict=Unsafe reason=unsafe-anchor-regression
Grid best=93.75 holdout=29.375 lower=8.550382664261932 verdict=Unsafe reason=unsafe-anchor-regression
Greedy best=93.75 holdout=29.375 lower=8.550382664261932 verdict=Unsafe reason=unsafe-anchor-regression
outcome=no-benefit
TOTAL 8 PASSED 8 FAILED 0 SKIPPED 0
```

Full suite:

```
TOTAL 525 PASSED 525 FAILED 0 SKIPPED 0
```

525 = 517 pre-existing tests + 8 new tests. The tiny task still reports `no-benefit`: Greedy has
zero verified improvements and one `Unsafe` result, so the Greedy-only rule cannot fire.

## Stub removal

Observed searches over the two new source files for
`NotImplementedException|NotImplemented|stub|Stub|TODO|FIXME`: no matches. The initial `Run` and
`ToCanonicalJson` stubs and the follow-up `ClassifyOutcome` stub were all replaced by the
implementation above; no `NotImplementedException` remains. Searches for
`DateTime|DateTimeOffset|Guid|Stopwatch|new Random|Environment\.|Thread|Task\.Run` over
`SearchAblation.cs`: no matches.

## Commands used

```
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Evaluation.SearchAblationTests
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

`dotnet test` was not run (VSTest's testhost is unavailable in this session; the local RunXunit runner
executes the same compiled test assembly). The experiment harness and its result are the next slice;
this slice is the tested, reusable runner.
