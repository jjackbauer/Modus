# Round 7 design — equal-budget proposer ablation (anchor 3)

**Objective:** measure whether any proposal process finds a better four-seat policy than its equal-budget
controls, across multiple starting genomes and opponent tasks. A null result is a valid outcome.

## 1. `Evaluation/SearchAblation.cs` (new)

~~~csharp
public enum SearchArm { NoSearch = 0, Random = 1, Grid = 2, Greedy = 3 }

public sealed record SearchTask(string TaskId, StrategyGenome Start, string CompositionId);

public sealed record SearchAblationOptions(
    GameConfiguration Game,
    FrozenOpponentPool Pool,
    IReadOnlyList<SearchTask> Tasks,
    int CandidatesPerArm,          // >= 4 and a multiple of 4
    int SelectionBlocks,
    int SelectionHandsPerMatch,
    int HoldoutBlocks,
    int HoldoutHandsPerMatch,
    double MinimumPracticalEffectBbPer100,
    double ConfidenceLevel,
    double AnchorRegressionToleranceBbPer100,
    ulong Seed);

public sealed record SearchBudget(
    int CandidatesPerArm, int SelectionBlocks, int SelectionHandsPerMatch,
    int HoldoutBlocks, int HoldoutHandsPerMatch, int RotationsPerBlock,
    long SelectionHandsPerArmPerTask, long TotalHands);

public sealed record SearchCandidate(
    string Arm, string TaskId, int Index, StrategyGenome Genome, double SelectionMeanBbPer100);

public sealed record SearchArmResult(
    string Arm, string TaskId, string BestPolicyHash,
    double BestSelectionMeanBbPer100, double HoldoutMeanBbPer100, double HoldoutLowerBoundBbPer100,
    string Verdict, string Reason);

public sealed record SearchAblationReport(
    string Schema, ulong Seed, SearchBudget Budget, IReadOnlyList<string> TaskIds,
    IReadOnlyList<SearchCandidate> Candidates, IReadOnlyList<SearchArmResult> Results,
    string Outcome)  // "benefit" | "no-benefit"
{
    public string ReportHash { get; }
    public string ToCanonicalJson();
}

public static class SearchAblation { public static SearchAblationReport Run(SearchAblationOptions options); }
~~~

## 2. Algorithm

Validate: 1–8 tasks with unique non-empty ids; every composition resolves in the pool; game seats 4;
CandidatesPerArm >= 4 and % 4 == 0; blocks/hands >= 1 and bounded (blocks <= 500, hands <= 100);
margin >= 0; confidence in (0,1); tolerance >= 0; pool id equals the plans we will build.

Per task build three plans with the pool and the task composition:
- selection plan: `Selection` domain of SelectionBlocks seeds, `PlannedCandidates = CandidatesPerArm`,
  SelectionHandsPerMatch;
- holdout plan: `Holdout` domain of HoldoutBlocks seeds, `PlannedCandidates = CandidatesPerArm`,
  HoldoutHandsPerMatch;
- anchor plan: same holdout domain, anchor composition (`three-tag` when the task uses
  `primary-mixed`, else `primary-mixed`), same settings.
Separate base seeds per task and purpose so domains are disjoint; training/audit domains are tiny.

For each task:
1. Start agent `new StrategyAgent(task.Start, "task-start-"+taskId)`; start policy hash =
   `StrategyArtifact.Create(task.Start, 0, null).Hash`.
2. For each arm, generate and evaluate exactly CandidatesPerArm candidates against the start on the
   selection plan (focal rotations, pool, selection plan):
   - NoSearch: the start genome, repeated CandidatesPerArm times;
   - Random: deterministic xorshift sample of raise/call/bluff uniform [0,1], sizing [0.25,3], seeded
     from `SeedDerivation.Derive(options.Seed, taskIndex+1, 100+(int)arm, 0, 0)`;
   - Grid: point i nudges parameter `i % 4` by +0.1 for i < 4, by -0.1 for i >= 4;
   - Greedy: rounds of four +0.1 neighbours (one per parameter) from the current best; after each
     round move to the best of the four. Exactly CandidatesPerArm evaluations.
   Record every evaluated genome and its selection mean (`Estimate.MeanBbPer100`).
3. Best = highest selection mean, ties to the lowest index.
4. Holdout: evaluate best vs start on the holdout plan; anchor: best vs start on the anchor plan;
   call `PopulationPromotionGate.Decide(holdout, anchor, holdoutPlan, anchorPlan)` and record verdict
   and reason.
5. Outcome = "benefit" only if a non-NoSearch arm has more VerifiedImprovement tasks than NoSearch;
   otherwise "no-benefit".

All evaluations use `PopulationMatchEvaluator.Evaluate` with distinct ids and 64-hex policy hashes.
Canonical JSON: schema `tholdem.search-ablation.v1`, seed, budget, taskIds, candidates (arm, taskId,
index, genome parameters, selectionMeanBbPer100), results (arm, taskId, bestPolicyHash,
bestSelectionMeanBbPer100, holdoutMeanBbPer100, holdoutLowerBoundBbPer100, verdict, reason), outcome.
`ReportHash = StrategyArtifact.ComputeHash(ToCanonicalJson())`. No clock, GUID, culture or ambient
randomness.

## 3. Tests — `WIP/Wip.Tholdem.Tests/Evaluation/SearchAblationTests.cs`

Use one task, a tiny game plan (2 selection blocks x 2 hands, 2 holdout blocks x 2 hands),
CandidatesPerArm = 4, margin 5, confidence 0.95, tolerance 5.
1. `Run_GivenOneTask_EveryArmEvaluatesTheSameCandidateCount` — 4 candidates per arm, budget fields
   match the arms.
2. `Run_GivenNoSearchArm_SelectionMeansAreZero` — every NoSearch candidate mean 0.0.
3. `Run_GivenCandidates_AreInsideGenomeDomain` — raise/call/bluff in [0,1], sizing in [0.25,3].
4. `Run_GivenSameOptions_IsByteIdentical` — two runs, equal JSON and hash.
5. `Run_GivenInvalidOptions_Throws` — CandidatesPerArm 2, empty tasks, unknown composition.

## 4. Forbidden

Do not change the evaluator, gate, verifier, plans, pool, genomes or any existing test. No clock,
GUID, culture or ambient randomness. No test weakening. The experiment harness and its result are
the next slice; this slice must be a reusable, tested runner.
