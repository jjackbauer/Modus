using System.Text;
using System.Text.Json;
using Wip.Tholdem.Agents.Evaluation;
using Wip.Tholdem.Agents.Learning;

var root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath(Path.Combine("docs", "goal-tholdem-enhancement", "experiments", "family-probe-v1"));
var resultsDirectory = Path.Combine(root, "results");
Directory.CreateDirectory(resultsDirectory);
var utf8 = new UTF8Encoding(false);
var game = GameConfiguration.FourSeatCash();
var pool = FrozenOpponentPool.SelectionV1;
var incumbentGenome = new StrategyGenome();
var incumbent = StrategyArtifact.Create(incumbentGenome, 0, null);
var raises = new[] { 0.40, 0.60, 0.80, 0.95 };
var calls = new[] { 0.20, 0.40, 0.60, 0.80 };
var bluffs = new[] { 0.0, 0.20, 0.40 };
var sizings = new[] { 0.75, 1.25, 1.75 };
var rows = new List<object>();
var verified = 0;
var index = 0;
foreach (var raise in raises)
foreach (var call in calls)
foreach (var bluff in bluffs)
foreach (var sizing in sizings)
{
    var candidate = new StrategyGenome(raise, call, bluff, sizing).Clamp();
    var challenger = StrategyArtifact.Create(candidate, 1, incumbent);
    var salt = (ulong)(0xF00D + index * 13);
    var domains = new[]
    {
        new SeedDomain("training", SeedPurpose.Training, salt + 1, 1),
        new SeedDomain("selection", SeedPurpose.Selection, salt + 2, 1),
        new SeedDomain("holdout", SeedPurpose.Holdout, salt + 3, 8),
        new SeedDomain("audit", SeedPurpose.Audit, salt + 4, 1),
    };
    var plan = EvaluationPlanV2.Create(game, FrozenOpponentPool.SelectionPoolId, new[] { "primary-mixed" }, domains, 2, 5.0, 0.95, 1, 5.0);
    var anchorPlan = EvaluationPlanV2.Create(game, FrozenOpponentPool.SelectionPoolId, new[] { "three-tag" }, domains, 2, 5.0, 0.95, 1, 5.0);
    var report = PopulationMatchEvaluator.Evaluate(new PopulationEvaluationRequest(plan, pool), new StrategyAgent(challenger.Genome, challenger.Id), challenger.Id, challenger.Hash, new StrategyAgent(incumbent.Genome, incumbent.Id), incumbent.Id, incumbent.Hash);
    var anchor = PopulationMatchEvaluator.Evaluate(new PopulationEvaluationRequest(anchorPlan, pool), new StrategyAgent(challenger.Genome, challenger.Id), challenger.Id, challenger.Hash, new StrategyAgent(incumbent.Genome, incumbent.Id), incumbent.Id, incumbent.Hash);
    var decision = PopulationPromotionGate.Decide(report, anchor, plan, anchorPlan);
    if (decision.Verdict == PromotionVerdict.VerifiedImprovement) verified++;
    rows.Add(new { raise, call, bluff, sizing, verdict = decision.Verdict.ToString(), reason = decision.Reason, mean = report.Estimate.MeanBbPer100, lower = report.Estimate.LowerBoundBbPer100 });
    index++;
}
var ordered = rows.Select(r => new { r, lower = (double)r.GetType().GetProperty("lower")!.GetValue(r)! }).OrderByDescending(x => x.lower).Take(10).Select(x => x.r).ToArray();
var json = JsonSerializer.Serialize(new { schema = "tholdem.family-probe.v1", candidates = rows.Count, verified, top = ordered, rows }, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(Path.Combine(resultsDirectory, "probe.json"), json, utf8);
Console.WriteLine($"candidates={rows.Count} verified={verified}");
return 0;
