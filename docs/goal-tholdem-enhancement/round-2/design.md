# Round 2 design contract — opponent population and paired four-seat evaluator

**Depends on Round 1:** `GameConfiguration`, `MatchSimulation`, `SeedDomain`, `EvaluationPlanV2`.
**Namespace:** `Wip.Tholdem.Agents.Evaluation`.
**Objective:** a frozen, versioned five-archetype opponent population, a paired focal-seat evaluator
over balanced seat assignments, and a canonical report with block-level uncertainty.

## 1. `Evaluation/OpponentPool.cs` (new)

```csharp
public enum OpponentArchetype { TightPassive, TightAggressive, LooseAggressive, CallingStation, AdversarialRaiser }

public sealed record OpponentSpec(
    string OpponentId, int Version, OpponentArchetype Archetype, string DisplayName, StrategyGenome Genome);

public sealed record OpponentComposition(string CompositionId, IReadOnlyList<string> OpponentIds);

public sealed class FrozenOpponentPool
{
    public string PoolId { get; }              // "tholdem.opponents.v1" / "tholdem.opponents.holdout.v1"
    public string Version { get; }             // "1.0.0" / "1.0.0-holdout"
    public IReadOnlyList<OpponentSpec> Opponents { get; }
    public IReadOnlyList<OpponentComposition> Compositions { get; }
    public OpponentSpec Get(string opponentId);
    public OpponentComposition GetComposition(string compositionId);
    public IPokerAgent Create(string opponentId);   // StrategyAgent(spec.Genome, spec.OpponentId)
    public string ToCanonicalJson();
    public string PoolHash { get; }
    public static FrozenOpponentPool SelectionV1 { get; }
    public static FrozenOpponentPool HoldoutV1 { get; }
}
```

Five archetypes in each pool. Selection genomes (frozen):
tight-passive (0.88, 0.55, 0.00, 1.0), tight-aggressive (0.72, 0.45, 0.12, 1.0),
loose-aggressive (0.45, 0.30, 0.40, 1.6), calling-station (0.97, 0.05, 0.00, 1.0),
adversarial-raiser (0.30, 0.40, 0.30, 2.4). Field order is
`(RaiseThreshold, CallThreshold, BluffFrequency, SizingMultiplier)` of `StrategyGenome`.

Holdout genomes are **different values** for every archetype and use IDs `holdout-<archetype>`:
holdout-tp (0.84, 0.58, 0.02, 1.1), holdout-tag (0.76, 0.48, 0.09, 0.9),
holdout-lag (0.49, 0.33, 0.36, 1.5), holdout-station (0.95, 0.08, 0.01, 1.05),
holdout-adv (0.34, 0.43, 0.27, 2.2). No opponent id may appear in both pools. The two pool
hashes must differ.

Composition ids are stable across pools (so a plan's composition list works with either pool):
- `primary-mixed` → [tight-aggressive, loose-aggressive, calling-station]
- `three-tag` → [tight-aggressive ×3]
- `three-station` → [calling-station ×3]
- `pressure` → [loose-aggressive, adversarial-raiser, tight-aggressive]

Canonical JSON: `{"schema":"tholdem.opponent-pool.v1","poolId":"...","version":"...","opponents":[{"id":"...","version":1,"archetype":"...","displayName":"...","genome":{"raiseThreshold":...,"callThreshold":...,"bluffFrequency":...,"sizingMultiplier":...}}],"compositions":[{"id":"...","opponents":["..."]}]}`.
`PoolHash = StrategyArtifact.ComputeHash(ToCanonicalJson())`.

## 2. `Evaluation/BlockStatistics.cs` (new)

```csharp
public sealed record PopulationEstimate(
    double MeanBbPer100, double LowerBoundBbPer100, double UpperBoundBbPer100,
    double ConfidenceLevel, string Method, int UnitCount, double StandardError,
    double NormalLowerBoundBbPer100, double BootstrapLowerBoundBbPer100);

public static class BlockStatistics
{
    public const int DefaultBootstrapSamples = 2000;
    public static double NormalQuantile(double probability);   // Acklam; 0.975 -> 1.959963984540054
    public static PopulationEstimate Compute(
        IReadOnlyList<double> unitMeans, double confidenceLevel, ulong bootstrapSeed,
        int bootstrapSamples = DefaultBootstrapSamples);
}
```

Normal interval: `mean ± z * sd/sqrt(n)` with sample sd. Bootstrap: resample `n` units with
replacement `bootstrapSamples` times with `Xorshift64Star` (internal, same assembly), sort the
resampled means, take the `alpha/2` and `1-alpha/2` percentiles. Report both; `LowerBound` is the
**minimum** of the two lower bounds and `UpperBound` the maximum of the two uppers (conservative).
`n == 0` → throw; `n == 1` → zero-width interval at the mean. All-equal values → zero width.
Method string: `"block-normal+bootstrap" `. The result is a pure function of (unitMeans, confidence,
bootstrapSeed, samples); no clock.

## 3. `Evaluation/PopulationEvaluationReport.cs` (new)

```csharp
public enum SeatPermutationMode { FocalRotations = 0, FullFactorial = 1 }

public sealed record EvaluationUnit(string CompositionId, int BlockIndex, string SeedFingerprint, int Hands, double MeanBbPer100);
public sealed record CompositionEstimate(string CompositionId, PopulationEstimate Estimate, long ChallengerNetChips, long IncumbentNetChips);
public sealed record SeatEstimate(int Seat, PopulationEstimate Estimate, int Hands);
public sealed record TailRisk(double WorstBlockBbPer100, double P05BlockBbPer100, double WorstHandBbPer100);

public sealed record PopulationEvaluationReport(
    string ChallengerId, string IncumbentId, string ChallengerPolicyHash, string IncumbentPolicyHash,
    string PlanHash, string GameConfigHash, string OpponentPoolId, string OpponentPoolHash, string EvaluatorVersion,
    int SeedBlocks, int HandsPerSeatMatch, string SeatPermutationMode, int RotationsPerBlock,
    PopulationEstimate Estimate, IReadOnlyList<EvaluationUnit> Units,
    IReadOnlyList<CompositionEstimate> PerComposition, IReadOnlyList<SeatEstimate> PerSeat,
    TailRisk Tail, long ChallengerNetChips, long IncumbentNetChips)
{
    public string ReportHash { get; }
    public string ToCanonicalJson();
}
```

Canonical JSON has a fixed key order, embeds every unit and every estimate, and contains no clock.
`SeedFingerprint` is the first 16 hex chars of SHA-256 over the UTF-8 decimal `blockSeed`. Report
schema id: `tholdem.population-report.v2`.

## 4. `Evaluation/PopulationMatchEvaluator.cs` (new)

```csharp
public sealed record PopulationEvaluationRequest(
    EvaluationPlanV2 Plan, FrozenOpponentPool Pool, SeatPermutationMode SeatMode = SeatPermutationMode.FocalRotations);

public static class PopulationMatchEvaluator
{
    public const string EvaluatorVersion = "tholdem.population-evaluator.v2.0.0";
    public static PopulationEvaluationReport Evaluate(
        PopulationEvaluationRequest request,
        IPokerAgent challenger, string challengerId, string challengerPolicyHash,
        IPokerAgent incumbent, string incumbentId, string incumbentPolicyHash,
        CancellationToken cancellationToken = default);
}
```

Validation: `request.Pool.PoolId == request.Plan.OpponentPoolId`; every plan composition id resolves in
the pool (a plan may deliberately use a subset of the pool's menu); policy hashes are 64 lowercase
hex chars; challenger/incumbent ids non-empty and distinct. Any violation → `ArgumentException` with a stable reason string in the message
(`pool-mismatch`, `composition-mismatch`, `policy-hash-invalid`, `agent-id-invalid`).

Algorithm, for every composition `c` at index `ci` and block `b` in `0..Plan.SeedBlocks-1`:
1. `blockSeed = SeedDerivation.Derive(Plan.Holdout.Seeds[b], 0xE7A1 + ci, 1, 0, 0)`.
2. `options = Plan.Game.ToSimulationOptions(blockSeed, Plan.HandsPerSeatMatch)`.
3. For every seat assignment (focal rotations: 4; full factorial: 24):
   - build `IPokerAgent[seats]` with the challenger at the focal seat and `c.OpponentIds` at the
     remaining seats in order (rotating the opponent order for full factorial);
   - `RunDetailed` challenger table, then `RunDetailed` incumbent table, same options;
   - assert both `HandSeeds` sequences are equal, else throw `InvalidOperationException("paired-deck-mismatch")`;
   - for each hand `h`, focal diff in chips = `(A.Outcomes[h].FinalStacks[focal] - start[focal]) -
     (B.Outcomes[h].FinalStacks[focal] - start[focal])`, where `start[focal]` is
     `Plan.Game.StartingStacks?[focal] ?? Plan.Game.StartingStack`;
   - accumulate the focal seat's bb/100 for this assignment (chips / BigBlind / hands * 100).
4. Block mean = mean over assignments. One `EvaluationUnit` per (composition, block).

Estimate units:
- `PerComposition`: the composition's block means (n = SeedBlocks).
- `PerSeat`: one unit per block for each focal seat. With focal rotations the seat contributes one
  assignment per block, so the unit is that assignment's mean over hands. With full factorial it is
  the mean over the six opponent permutations at that seat. n = SeedBlocks.
- `Estimate`: for each block, average the composition means into one block mean; n = SeedBlocks.
Bootstrap seed: `SeedDerivation.Derive(Plan.Holdout.Seeds[b=0], 0xB007, 1, 0, 0)` (documented constant).
`ChallengerNetChips`/`IncumbentNetChips`: sum of focal net chips over all challenger/incumbent runs.
`Tail`: worst block mean, 5th percentile of block means, worst single-hand focal diff in bb.
`RotationsPerBlock`: 4 or 24.

## 5. Tests (fail first, then pass)

`WIP/Wip.Tholdem.Tests/Evaluation/OpponentPoolTests.cs`
1. `SelectionV1_GivenPool_HasFiveDistinctArchetypesAndIds`.
2. `Create_GivenOpponentId_ReturnsAgentWithThatName`.
3. `PoolHash_GivenSamePool_IsStable_AndHoldoutDiffers` (pin the literal selection hash after first run).
4. `Compositions_GivenEveryComposition_ReferencesOnlyOwnPoolOpponents`.
5. `HoldoutV1_GivenSelectionPool_HasNoSharedOpponentIds`.
6. `Archetypes_GivenSameSeededTable_ShowDistinctActionFrequencies` — wrap each archetype in the
   existing `RecordingAgent` (test assembly), run 60 hands at seat 0 against three fixed TAGs, count
   `Call` and aggressive actions; assert station calls > TAG calls, LAG raises > TAG raises,
   tight-passive raises < TAG raises. Concrete counts are asserted as inequalities.

`WIP/Wip.Tholdem.Tests/Evaluation/BlockStatisticsTests.cs`
1. `NormalQuantile_Given975_IsWithin1e12Of1960` (tolerance 1e-9).
2. `Compute_GivenConstantUnits_ReturnsZeroWidthInterval`.
3. `Compute_GivenSameInputs_IsDeterministic`.
4. `Compute_GivenSyntheticNormalDatasets_CoverageIsWithinCalibrationBand` — 200 deterministic
   datasets of 24 standard-normal values, true mean 0; count intervals containing 0; assert 0.90–0.99.

`WIP/Wip.Tholdem.Tests/Evaluation/PopulationEvaluatorTests.cs` (small plans: 6 blocks, 4 hands,
`primary-mixed`, selection pool)
1. `Evaluate_GivenPolicyVersusItself_ReportsExactlyZero` — same nit agent both sides; mean/LCB/UB = 0.
2. `Evaluate_GivenSameInputs_IsByteIdentical` — report JSON equal and hash equal.
3. `Evaluate_GivenKnownDominatedControl_ReportsPositiveLowerBound` — 96 blocks, 20 hands, one
   composition, fold-only vs call-any; mean > 0 and lower bound > 0 (the pilot supports a large
   effect; if it fails on this fixed seed family, report the observed value instead of weakening the
   assertion).
4. `Evaluate_GivenPoolMismatch_Throws`.
5. `Evaluate_GivenCompositionMismatch_Throws`.
6. `Evaluate_GivenFocalRotations_ReportsEveryFocalSeat` — `Units.Count == blocks × compositions`,
   `PerSeat.Count == 4`, and every `SeatEstimate.UnitCount == blocks` with the right `Hands`.

## 6. Forbidden

- Do not change `MatchSimulator`, `MatchSimulation`, `GameConfiguration`, `SeedDomain`, `EvaluationPlanV2`,
  v1 evaluator/gate, engine, history, host or web.
- No clock, GUID, culture, ambient randomness. No weakening of existing tests.
