using System.Text;
using System.Text.Json;
using Wip.Tholdem.Agents.Evaluation;
using Wip.Tholdem.Agents.Learning;

var root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath(Path.Combine("docs", "goal-tholdem-enhancement", "experiments", "llm-attribution-v1"));
var resultsDirectory = Path.Combine(root, "results");
var utf8 = new UTF8Encoding(false);
var probe = JsonDocument.Parse(File.ReadAllText(Path.Combine(resultsDirectory, "probe.json")));
var tasks = new Dictionary<string, StrategyGenome>(StringComparer.Ordinal)
{
    ["default"] = new StrategyGenome(),
    ["tight"] = new StrategyGenome(0.85, 0.35, 0.05, 1.20),
    ["loose"] = new StrategyGenome(0.55, 0.65, 0.25, 0.80),
    ["aggressive"] = new StrategyGenome(0.65, 0.50, 0.30, 1.50),
};
var game = GameConfiguration.FourSeatCash();
var rows = new List<object>();
var verified = 0;
var unsafeCount = 0;
var index = 0;
foreach (var row in probe.RootElement.GetProperty("rows").EnumerateArray())
{
    if (row.GetProperty("status").GetString() != "parsed") continue;
    var task = row.GetProperty("task").GetString()!;
    var iteration = row.GetProperty("iteration").GetInt32();
    var proposal = new StrategyGenome(
        row.GetProperty("RaiseThreshold").GetDouble(),
        row.GetProperty("CallThreshold").GetDouble(),
        row.GetProperty("BluffFrequency").GetDouble(),
        row.GetProperty("SizingMultiplier").GetDouble());
    var incumbent = StrategyArtifact.Create(tasks[task], 0, null);
    var challenger = StrategyArtifact.Create(proposal, iteration, incumbent);
    var salt = (ulong)(0xAA00 + index * 17);
    var domains = new[]
    {
        new SeedDomain("training", SeedPurpose.Training, salt + 1, 1),
        new SeedDomain("selection", SeedPurpose.Selection, salt + 2, 1),
        new SeedDomain("holdout", SeedPurpose.Holdout, salt + 3, 4),
        new SeedDomain("audit", SeedPurpose.Audit, salt + 4, 1),
    };
    var plan = EvaluationPlanV2.Create(game, FrozenOpponentPool.SelectionPoolId, new[] { "primary-mixed" }, domains, 2, 5.0, 0.95, 1, 5.0);
    var anchorPlan = EvaluationPlanV2.Create(game, FrozenOpponentPool.SelectionPoolId, new[] { "three-tag" }, domains, 2, 5.0, 0.95, 1, 5.0);
    var pool = FrozenOpponentPool.SelectionV1;
    var report = PopulationMatchEvaluator.Evaluate(new PopulationEvaluationRequest(plan, pool), new StrategyAgent(challenger.Genome, challenger.Id), challenger.Id, challenger.Hash, new StrategyAgent(incumbent.Genome, incumbent.Id), incumbent.Id, incumbent.Hash);
    var anchor = PopulationMatchEvaluator.Evaluate(new PopulationEvaluationRequest(anchorPlan, pool), new StrategyAgent(challenger.Genome, challenger.Id), challenger.Id, challenger.Hash, new StrategyAgent(incumbent.Genome, incumbent.Id), incumbent.Id, incumbent.Hash);
    var decision = PopulationPromotionGate.Decide(report, anchor, plan, anchorPlan);
    var verdict = decision.Verdict.ToString();
    if (verdict == "VerifiedImprovement") verified++;
    if (verdict == "Unsafe") unsafeCount++;
    rows.Add(new { task, iteration, verdict, reason = decision.Reason, mean = report.Estimate.MeanBbPer100, lower = report.Estimate.LowerBoundBbPer100, anchormean = anchor.Estimate.MeanBbPer100 });
    index++;
}

var json = JsonSerializer.Serialize(new { schema = "tholdem.llm-gate-pilot.v1", count = rows.Count, verified, unsafeCount, rows }, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(Path.Combine(resultsDirectory, "gated.json"), json, utf8);
Console.WriteLine($"evaluated={rows.Count} verified={verified} unsafe={unsafeCount}");
return 0;
