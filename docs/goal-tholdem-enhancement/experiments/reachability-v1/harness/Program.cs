using System.Text;
using System.Text.Json;
using Wip.Tholdem.Agents;
using Wip.Tholdem.Agents.Evaluation;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Learning.Refinement;
using Wip.Tholdem.Agents.Simulation;

var root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath(Path.Combine("docs", "goal-tholdem-enhancement", "experiments", "reachability-v1"));
var resultsDirectory = Path.Combine(root, "results");
Directory.CreateDirectory(resultsDirectory);
var utf8 = new UTF8Encoding(false);
var start = new StrategyGenome();
var analysis = new SessionAnalysis(40, 0.5, 0.0, 1.0, 0.5, 0.4);
var pool = FrozenOpponentPool.SelectionV1;
var composition = pool.GetComposition("primary-mixed");
var agents = new List<IPokerAgent> { new StrategyAgent(start, "start") };
foreach (var opponentId in composition.OpponentIds.Take(3)) agents.Add(pool.Create(opponentId));
var capture = TrainingTrajectoryCapture.Capture(root, 1, new SimulationOptions(0xC0FFEEUL, 40, 1000, 5, 10), agents);
var signatures = FailureSignatureExtractor.Extract(capture.Hands);

var grid = new[]
{
    new StrategyGenome(0.95, 0.45, 0.12, 1.00),
    new StrategyGenome(0.72, 0.20, 0.12, 1.00),
    new StrategyGenome(0.72, 0.45, 0.40, 1.00),
    new StrategyGenome(0.72, 0.45, 0.12, 0.75),
    new StrategyGenome(0.85, 0.30, 0.20, 0.90),
    new StrategyGenome(0.60, 0.30, 0.30, 0.75),
    new StrategyGenome(0.90, 0.60, 0.10, 1.25),
    new StrategyGenome(0.50, 0.50, 0.50, 1.00),
};

var rows = new List<object>();
var totals = new Dictionary<string, int>(StringComparer.Ordinal);
foreach (var arm in new[] { "diagnosis", "deterministic", "random", "grid", "nosearch" }) totals[arm] = 0;
var diagnosis = new DiagnosisDrivenProposer(signatures);
var deterministic = new DeterministicStrategyProposer();
var diag = start;
var det = start;
var rng = 0xABCDu;
for (var i = 1; i <= 8; i++)
{
    diag = diagnosis.Propose(diag, analysis, i);
    det = deterministic.Propose(det, analysis, i);
    rng = rng * 1664525u + 1013904223u;
    var r1 = (rng >> 8) / 16777216.0; rng = rng * 1664525u + 1013904223u;
    var r2 = (rng >> 8) / 16777216.0; rng = rng * 1664525u + 1013904223u;
    var r3 = (rng >> 8) / 16777216.0; rng = rng * 1664525u + 1013904223u;
    var r4 = (rng >> 8) / 16777216.0;
    var random = new StrategyGenome(0.3 + r1 * 0.7, 0.2 + r2 * 0.7, r3 * 0.5, 0.5 + r4 * 2.0).Clamp();
    foreach (var (arm, genome) in new[] { ("diagnosis", diag), ("deterministic", det), ("random", random), ("grid", grid[i - 1]), ("nosearch", start) })
    {
        var (verdict, reason, mean, lower) = Evaluate(start, genome, (ulong)(0xBB00 + i * 7));
        if (verdict == "VerifiedImprovement") totals[arm]++;
        rows.Add(new { arm, iteration = i, verdict, reason, mean, lower });
    }
}
var json = JsonSerializer.Serialize(new { schema = "tholdem.reachability.v1", totals, signatures = signatures.ToCanonicalJson(), rows }, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(Path.Combine(resultsDirectory, "report.json"), json, utf8);
Console.WriteLine(string.Join(" ", totals.Select(p => p.Key + "=" + p.Value)));
return 0;

static (string Verdict, string Reason, double Mean, double Lower) Evaluate(StrategyGenome start, StrategyGenome candidate, ulong salt)
{
    var incumbent = StrategyArtifact.Create(start, 0, null);
    var challenger = StrategyArtifact.Create(candidate, 1, incumbent);
    var domains = new[]
    {
        new SeedDomain("training", SeedPurpose.Training, salt + 1, 1),
        new SeedDomain("selection", SeedPurpose.Selection, salt + 2, 1),
        new SeedDomain("holdout", SeedPurpose.Holdout, salt + 3, 8),
        new SeedDomain("audit", SeedPurpose.Audit, salt + 4, 1),
    };
    var plan = EvaluationPlanV2.Create(GameConfiguration.FourSeatCash(), FrozenOpponentPool.SelectionPoolId, new[] { "primary-mixed" }, domains, 2, 5.0, 0.95, 1, 5.0);
    var anchorPlan = EvaluationPlanV2.Create(GameConfiguration.FourSeatCash(), FrozenOpponentPool.SelectionPoolId, new[] { "three-tag" }, domains, 2, 5.0, 0.95, 1, 5.0);
    var pool = FrozenOpponentPool.SelectionV1;
    var report = PopulationMatchEvaluator.Evaluate(new PopulationEvaluationRequest(plan, pool), new StrategyAgent(challenger.Genome, challenger.Id), challenger.Id, challenger.Hash, new StrategyAgent(incumbent.Genome, incumbent.Id), incumbent.Id, incumbent.Hash);
    var anchor = PopulationMatchEvaluator.Evaluate(new PopulationEvaluationRequest(anchorPlan, pool), new StrategyAgent(challenger.Genome, challenger.Id), challenger.Id, challenger.Hash, new StrategyAgent(incumbent.Genome, incumbent.Id), incumbent.Id, incumbent.Hash);
    var decision = PopulationPromotionGate.Decide(report, anchor, plan, anchorPlan);
    return (decision.Verdict.ToString(), decision.Reason, report.Estimate.MeanBbPer100, report.Estimate.LowerBoundBbPer100);
}
