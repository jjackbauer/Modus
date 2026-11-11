using System.Text;
using System.Text.Json;
using Wip.Tholdem.Agents.Evaluation;
using Wip.Tholdem.Agents.Learning;

var experimentRoot = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath(Path.Combine("docs", "goal-tholdem-enhancement", "experiments", "diagnosis-ablation-v1"));
var dataDirectory = Path.Combine(experimentRoot, "data");
var resultsDirectory = Path.Combine(experimentRoot, "results");
Directory.CreateDirectory(dataDirectory);
Directory.CreateDirectory(resultsDirectory);
var utf8 = new UTF8Encoding(false);

var tasks = new (string Id, StrategyGenome Genome)[]
{
    ("default", new StrategyGenome()),
    ("tight", new StrategyGenome(0.85, 0.35, 0.05, 1.20)),
    ("loose", new StrategyGenome(0.55, 0.65, 0.25, 0.80)),
    ("aggressive", new StrategyGenome(0.65, 0.50, 0.30, 1.50)),
};

var rows = new List<object>();
var taskSummaries = new List<(string Task, int OffPromoted, int OnPromoted, int OnUnsafe)>();
foreach (var task in tasks)
{
    var off = RunArm(experimentRoot, dataDirectory, task.Genome, task.Id, "deterministic", diagnosis: false);
    var on = RunArm(experimentRoot, dataDirectory, task.Genome, task.Id, "diagnosis", diagnosis: true);
    var offPromoted = off.Count(r => r.Outcome == "promoted");
    var onPromoted = on.Count(r => r.Outcome == "promoted");
    var onUnsafe = on.Count(r => r.Verdict == "Unsafe");
    taskSummaries.Add((task.Id, offPromoted, onPromoted, onUnsafe));
    rows.Add(new { task = task.Id, arm = "deterministic", promoted = offPromoted, iterations = off });
    rows.Add(new { task = task.Id, arm = "diagnosis", promoted = onPromoted, unsafeCount = onUnsafe, iterations = on });
}

var benefit = taskSummaries.Count(s => s.OnPromoted > s.OffPromoted) >= 2
    && taskSummaries.All(s => s.OnUnsafe == 0)
    && taskSummaries.Sum(s => s.OnPromoted) > taskSummaries.Sum(s => s.OffPromoted);
var report = new
{
    schema = "tholdem.diagnosis-ablation.v1",
    tasks = taskSummaries,
    totals = new { offPromoted = taskSummaries.Sum(s => s.OffPromoted), onPromoted = taskSummaries.Sum(s => s.OnPromoted), onUnsafe = taskSummaries.Sum(s => s.OnUnsafe) },
    outcome = benefit ? "benefit" : "no-benefit",
    iterations = rows,
};
var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(Path.Combine(resultsDirectory, "report.json"), json, utf8);
Console.WriteLine($"outcome={report.outcome} off={report.totals.offPromoted} on={report.totals.onPromoted} unsafe={report.totals.onUnsafe}");
return 0;

static List<IterationRow> RunArm(string root, string data, StrategyGenome genome, string task, string arm, bool diagnosis)
{
    var run = FourSeatTraining.Run(new FourSeatTrainingOptions(
        Seed: 0x5EED_2026UL,
        Iterations: 4,
        DataDirectory: Path.Combine(data, task, arm),
        StartingGenome: genome,
        SelectionBlocks: 8,
        SelectionHandsPerMatch: 2,
        HoldoutBlocks: 8,
        HoldoutHandsPerMatch: 2,
        MarginBbPer100: 5.0,
        DiagnosisDriven: diagnosis,
        DiagnosisHands: 40));
    return run.Iterations.Select(i => new IterationRow(i.Iteration, i.Outcome, i.Verdict, i.Reason, i.SelectionMeanBbPer100, i.ChallengerId)).ToList();
}

record IterationRow(int Iteration, string Outcome, string Verdict, string Reason, double SelectionMean, string ChallengerId);
