using System.Globalization;
using Wip.Tholdem.Agents.Evaluation;
using Wip.Tholdem.Agents.Learning;

// Equal-budget proposer ablation over multiple starting genomes and opponent tasks. A null outcome
// ("no-benefit") is a valid, reportable result; no pointer is changed and no claim is made here.

var outputDirectory = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath(Path.Combine("docs", "goal-tholdem-enhancement", "experiments", "learning-ablation-v1", "results"));
Directory.CreateDirectory(outputDirectory);

var pool = FrozenOpponentPool.HoldoutV1;
var defaultGenome = new StrategyGenome(0.72, 0.45, 0.12, 1.0);
var tightGenome = new StrategyGenome(0.90, 0.60, 0.00, 1.0);
var tasks = new[]
{
    new SearchTask("default-primary", defaultGenome, "primary-mixed"),
    new SearchTask("default-three-tag", defaultGenome, "three-tag"),
    new SearchTask("tight-primary", tightGenome, "primary-mixed"),
    new SearchTask("tight-three-tag", tightGenome, "three-tag"),
};

var options = new SearchAblationOptions(
    GameConfiguration.FourSeatCash(),
    pool,
    tasks,
    CandidatesPerArm: 8,
    SelectionBlocks: 48,
    SelectionHandsPerMatch: 20,
    HoldoutBlocks: 96,
    HoldoutHandsPerMatch: 20,
    MinimumPracticalEffectBbPer100: 10.0,
    ConfidenceLevel: 0.95,
    AnchorRegressionToleranceBbPer100: 5.0,
    Seed: 0x5EEDBEEF1234UL);

var report = SearchAblation.Run(options);
File.WriteAllText(
    Path.Combine(outputDirectory, "report.json"),
    report.ToCanonicalJson(),
    new System.Text.UTF8Encoding(false));

Console.WriteLine($"schema {report.Schema}");
Console.WriteLine($"outcome {report.Outcome}");
Console.WriteLine($"reportHash {report.ReportHash}");
Console.WriteLine($"budget candidatesPerArm={report.Budget.CandidatesPerArm} selectionFocalHandsPerArmPerTask={report.Budget.SelectionHandsPerArmPerTask} totalFocalHands={report.Budget.TotalHands}");
foreach (var result in report.Results)
{
    Console.WriteLine(
        $"{result.TaskId} {result.Arm} best={result.BestSelectionMeanBbPer100.ToString("R", CultureInfo.InvariantCulture)} "
        + $"holdout={result.HoldoutMeanBbPer100.ToString("R", CultureInfo.InvariantCulture)} "
        + $"lower={result.HoldoutLowerBoundBbPer100.ToString("R", CultureInfo.InvariantCulture)} "
        + $"verdict={result.Verdict} reason={result.Reason}");
}

return 0;
