using System.Text;
using System.Text.Json;
using Wip.Tholdem.Agents.Evaluation;
using Wip.Tholdem.Agents.Learning;

var root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath(Path.Combine("docs", "goal-tholdem-enhancement", "experiments", "proposal-comparison-v1"));
var resultsDirectory = Path.Combine(root, "results");
Directory.CreateDirectory(resultsDirectory);
var utf8 = new UTF8Encoding(false);
var probe = JsonDocument.Parse(File.ReadAllText(Path.Combine("docs", "goal-tholdem-enhancement", "experiments", "llm-attribution-v1", "results", "probe.json")));

var taskList = new (string Id, StrategyGenome Genome)[]
{
    ("default", new StrategyGenome()),
    ("tight", new StrategyGenome(0.85, 0.35, 0.05, 1.20)),
    ("loose", new StrategyGenome(0.55, 0.65, 0.25, 0.80)),
    ("aggressive", new StrategyGenome(0.65, 0.50, 0.30, 1.50)),
};
var game = GameConfiguration.FourSeatCash();
var pool = FrozenOpponentPool.SelectionV1;
var grid = new[]
{
    new StrategyGenome(0.60, 0.40, 0.05, 1.00),
    new StrategyGenome(0.70, 0.50, 0.10, 1.25),
    new StrategyGenome(0.80, 0.60, 0.20, 1.50),
    new StrategyGenome(0.90, 0.70, 0.30, 2.00),
};
var analysis = new SessionAnalysis(40, 0.5, 0.0, 1.0, 0.5, 0.4);
var deterministic = new DeterministicStrategyProposer();
var rows = new List<object>();
var totals = new Dictionary<string, (int Verified, int Unsafe)>(StringComparer.Ordinal);
foreach (var arm in new[] { "llm", "random", "grid", "greedy", "nosearch" }) totals[arm] = (0, 0);

for (var taskIndex = 0; taskIndex < taskList.Length; taskIndex++)
{
    var task = taskList[taskIndex];
    var llm = LlmProposals(probe, task.Id);
    for (var candidate = 0; candidate < 4; candidate++)
    {
        foreach (var arm in new[] { "llm", "random", "grid", "greedy", "nosearch" })
        {
            var genome = arm switch
            {
                "llm" => llm[candidate],
                "random" => RandomGenome(taskIndex, candidate, task.Genome),
                "grid" => grid[candidate],
                "greedy" => DeterministicSteps(deterministic, task.Genome, analysis, candidate + 1),
                _ => task.Genome,
            };
            var (verdict, reason, mean, lower) = Evaluate(task.Genome, genome, (ulong)(0xCC00 + taskIndex * 100 + candidate));
            var t = totals[arm];
            if (verdict == "VerifiedImprovement") t.Verified++;
            if (verdict == "Unsafe") t.Unsafe++;
            totals[arm] = t;
            rows.Add(new { task = task.Id, candidate, arm, verdict, reason, mean, lower });
        }
    }
}

var outcomes = totals.ToDictionary(p => p.Key, p => p.Value);
var llmTotal = outcomes["llm"];
var benefit = llmTotal.Verified >= 2
    && llmTotal.Unsafe == 0
    && outcomes.Where(p => p.Key != "llm").All(p => llmTotal.Verified > p.Value.Verified);
var json = JsonSerializer.Serialize(new { schema = "tholdem.proposal-comparison.v1", outcome = benefit ? "benefit" : "no-benefit", totals = outcomes, rows }, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(Path.Combine(resultsDirectory, "report.json"), json, utf8);
Console.WriteLine($"outcome={(benefit ? "benefit" : "no-benefit")} llm={llmTotal.Verified}/{llmTotal.Unsafe} controls=" + string.Join(",", outcomes.Where(p => p.Key != "llm").Select(p => p.Key + ":" + p.Value.Verified + "/" + p.Value.Unsafe)));
return 0;

static StrategyGenome[] LlmProposals(JsonDocument probe, string task)
{
    var list = new List<StrategyGenome>();
    foreach (var row in probe.RootElement.GetProperty("rows").EnumerateArray())
    {
        if (row.GetProperty("task").GetString() != task) continue;
        list.Add(new StrategyGenome(row.GetProperty("RaiseThreshold").GetDouble(), row.GetProperty("CallThreshold").GetDouble(), row.GetProperty("BluffFrequency").GetDouble(), row.GetProperty("SizingMultiplier").GetDouble()));
    }
    return list.ToArray();
}

static StrategyGenome RandomGenome(int task, int candidate, StrategyGenome start)
{
    var state = (uint)(0x1234 + task * 31 + candidate * 7);
    double Next() { state = state * 1664525u + 1013904223u; return (state >> 8) / 16777216.0; }
    return new StrategyGenome(0.3 + Next() * 0.7, 0.2 + Next() * 0.7, Next() * 0.5, 0.5 + Next() * 2.0).Clamp();
}

static StrategyGenome DeterministicSteps(IStrategyProposer proposer, StrategyGenome start, SessionAnalysis analysis, int steps)
{
    var genome = start;
    for (var i = 1; i <= steps; i++) genome = proposer.Propose(genome, analysis, i);
    return genome;
}

static (string Verdict, string Reason, double Mean, double Lower) Evaluate(StrategyGenome start, StrategyGenome candidate, ulong salt)
{
    var incumbent = StrategyArtifact.Create(start, 0, null);
    var challenger = StrategyArtifact.Create(candidate, 1, incumbent);
    var domains = new[]
    {
        new SeedDomain("training", SeedPurpose.Training, salt + 1, 1),
        new SeedDomain("selection", SeedPurpose.Selection, salt + 2, 1),
        new SeedDomain("holdout", SeedPurpose.Holdout, salt + 3, 4),
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
