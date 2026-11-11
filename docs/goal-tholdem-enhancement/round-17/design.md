# Round 17 design — four-seat training/promotion path (item 1)

**Objective:** an additive four-seat training loop that promotes only through the v2 paired evaluator,
anchor gate and verifier, writes an isolated `four-seat/` champion pointer, and never touches the v1
training artifacts. A CLI `train4` command exposes it.

## 1. Evaluation/FourSeatTraining.cs (new)

~~~csharp
public sealed record FourSeatTrainingOptions(
    ulong Seed,
    int Iterations,
    string DataDirectory,
    GameConfiguration? Game = null,
    FrozenOpponentPool? Pool = null,
    IReadOnlyList<string>? Compositions = null,
    int SelectionBlocks = 8,
    int SelectionHandsPerMatch = 4,
    int HoldoutBlocks = 16,
    int HoldoutHandsPerMatch = 4,
    double MarginBbPer100 = 5.0,
    double ConfidenceLevel = 0.95,
    double AnchorToleranceBbPer100 = 5.0,
    StrategyGenome? StartingGenome = null,
    string? AnchorComposition = null,
    Func<StrategyGenome, int, StrategyGenome>? Proposer = null);

public sealed record FourSeatTrainingIteration(
    int Iteration, string ChampionId, string ChallengerId, string Outcome,
    double SelectionMeanBbPer100, string Verdict, string Reason);

public sealed record FourSeatTrainingRun(
    string FinalChampionId, IReadOnlyList<FourSeatTrainingIteration> Iterations, string RunHash);

public static class FourSeatTraining
{
    public const string DirectoryName = "four-seat";
    public const string ChampionFileName = "active-champion.json";
    public static FourSeatTrainingRun Run(FourSeatTrainingOptions options);
}
~~~

Defaults: Game = FourSeatCash, Pool = SelectionV1, Compositions = ["primary-mixed"], starting genome =
the default genome, proposer = DeterministicStrategyProposer with a neutral analysis
(SessionAnalysis(0, 0, 0, 1, 0, null)). Anchor composition = "three-tag" unless the primary list
contains only "three-tag", then "primary-mixed".

Algorithm per iteration:
1. challenger genome = proposer(champion.Genome, iteration) or the deterministic proposer; clamp.
2. challenger artifact = StrategyArtifact.Create(genome, iteration, champion artifact); write under
   <data>/four-seat/strategies/<id>.json.
3. Build per-iteration disjoint domains: selection (Selection, SelectionBlocks), holdout (Holdout,
   HoldoutBlocks), training and audit (count 1), each with a distinct base derived from Seed and the
   iteration.
4. Selection plan and report (challenger vs champion, FocalRotations). Write
   reports/iter-<n>-selection.json.
5. If selection mean <= 0 -> outcome rejected-selection, no holdout spend.
6. Else holdout report + anchor report + PopulationPromotionGate.Decide; write
   reports/iter-<n>-holdout.json and reports/iter-<n>-decision.json. Only VerifiedImprovement replaces
   the isolated champion (write four-seat/active-champion.json); otherwise rejected-holdout.
7. Every persisted report is canonical; no clock/GUID/culture/ambient randomness. No v1 pointer or
   v1 training file is read or written.

RunHash = StrategyArtifact.ComputeHash(canonical JSON of the run summary: seed, iterations, final id,
and every iteration row).

## 2. Tests — WIP/Wip.Tholdem.Tests/Evaluation/FourSeatTrainingTests.cs (four)

1. Run_GivenInertChallenger_RejectsAtSelectionAndLeavesChampion: inject a proposer returning the
   champion with only a different PromptText (same behavior) -> selection mean 0; outcome
   rejected-selection; final champion id unchanged; strategy and selection report written; no v1
   active-champion.json; one-seat plan (1 block, 1 hand) keeps it fast.
2. Run_GivenSameOptions_IsByteIdentical: two directories, same options -> equal RunHash and equal
   canonical report files.
3. Run_GivenInvalidOptions_Throws: iterations -1, selection blocks 0, unknown composition.
4. Run_GivenOneIteration_WritesOnlyFourSeatArtifacts: no file outside <data>/four-seat is created.

## 3. CLI — Host/Commands/Train4Command.cs (new), TholdemCli (edit)

~~~text
tholdem train4 --seed <strong> --iterations <n> [--selection-blocks <n>=8]
               [--selection-hands <n>=4] [--holdout-blocks <n>=16]
               [--holdout-hands <n>=4] [--margin <bb/100>=5]
               [--json] [--data-dir <path>] [--allow-weak-seed]
~~~

Strong-seed policy like train. Prints one line per iteration and the final isolated champion. No v1
pointer, no merge/deploy.

Test (add to a new Host/Train4CommandTests.cs): tiny run (1 iteration, 1 block, 1 hand) exits 0,
creates four-seat artifacts, and leaves the v1 active-champion.json absent.

## 4. Forbidden

Do not change TrainingLoop, TrainingOptions, the v1 TrainCommand, the evaluator/gate/verifier, or any
existing test. No clock/GUID/culture/ambient randomness. No v1 pointer writes.
