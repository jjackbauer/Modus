using System.Security.Cryptography;
using System.Text;
using Wip.Tholdem.Agents.Evaluation;
using Wip.Tholdem.Agents.Learning;

// Writes the pre-registration, plans and data artifacts BEFORE the evaluation CLI runs.
var experimentRoot = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath(Path.Combine("docs", "goal-tholdem-enhancement", "experiments", "equity-policy-v1"));
var dataDirectory = Path.Combine(experimentRoot, "data");
var resultsDirectory = Path.Combine(experimentRoot, "results");
Directory.CreateDirectory(Path.Combine(dataDirectory, "strategies"));
Directory.CreateDirectory(Path.Combine(dataDirectory, "equity-policies"));
Directory.CreateDirectory(resultsDirectory);

var utf8 = new UTF8Encoding(false);
var incumbent = StrategyArtifact.Create(new StrategyGenome(0.72, 0.45, 0.12, 1.0), 0, null);
File.WriteAllText(Path.Combine(dataDirectory, "strategies", incumbent.Id + ".json"), incumbent.ToCanonicalJson(), utf8);

var policy = new EquityPolicy();
var policyJson = EquityPolicyJson.Write(policy);
var policyHash = EquityPolicyJson.Hash(policy);
const string PolicyId = "equity-default-v1";
File.WriteAllText(Path.Combine(dataDirectory, "equity-policies", PolicyId + ".json"), policyJson, utf8);

var game = GameConfiguration.FourSeatCash();
var training = new SeedDomain("equity-training", SeedPurpose.Training, 0xE9A001UL, 32);
var selection = new SeedDomain("equity-selection", SeedPurpose.Selection, 0xE9A002UL, 32);
var holdout = new SeedDomain("equity-holdout", SeedPurpose.Holdout, 0xE9A003UL, 16);
var audit = new SeedDomain("equity-audit", SeedPurpose.Audit, 0xE9A004UL, 8);
var domains = new[] { training, selection, holdout, audit };

var plan = EvaluationPlanV2.Create(
    game, FrozenOpponentPool.HoldoutPoolId,
    new[] { "primary-mixed", "three-tag" }, domains,
    handsPerSeatMatch: 2, minimumPracticalEffectBbPer100: 10.0,
    confidenceLevel: 0.95, plannedCandidates: 1, anchorRegressionToleranceBbPer100: 5.0);
var anchorPlan = EvaluationPlanV2.Create(
    game, FrozenOpponentPool.HoldoutPoolId,
    new[] { "primary-mixed" }, domains,
    handsPerSeatMatch: 2, minimumPracticalEffectBbPer100: 10.0,
    confidenceLevel: 0.95, plannedCandidates: 1, anchorRegressionToleranceBbPer100: 5.0);

var planPath = Path.Combine(experimentRoot, "plan.json");
var anchorPath = Path.Combine(experimentRoot, "anchor-plan.json");
File.WriteAllText(planPath, plan.ToCanonicalJson(), utf8);
File.WriteAllText(anchorPath, anchorPlan.ToCanonicalJson(), utf8);

var preregistration = "{\"schema\":\"tholdem.equity-experiment.preregistration.v1\","
    + "\"experimentId\":\"equity-policy-v1\",\"date\":\"2026-11-10\","
    + "\"hypothesis\":\"The default equity-aware policy beats the legacy four-parameter strategy on the pre-registered holdout population and does not regress on the anchor composition.\","
    + "\"decisionRule\":{\"gate\":\"PopulationPromotionGate\",\"minimumPracticalEffectBbPer100\":10,\"confidenceLevel\":0.95,\"plannedCandidates\":1,\"anchorRegressionToleranceBbPer100\":5},"
    + "\"policies\":[{\"id\":\"equity-default-v1\",\"kind\":\"equity-policy\",\"hash\":\"" + policyHash + "\"},"
    + "{\"id\":\"" + incumbent.Id + "\",\"kind\":\"strategy\",\"hash\":\"" + incumbent.Hash + "\"}],"
    + "\"planHash\":\"" + plan.PlanHash + "\",\"anchorPlanHash\":\"" + anchorPlan.PlanHash + "\","
    + "\"plan\":" + plan.ToCanonicalJson() + ",\"anchorPlan\":" + anchorPlan.ToCanonicalJson() + "}";
var preregPath = Path.Combine(experimentRoot, "preregistration.json");
File.WriteAllText(preregPath, preregistration, utf8);
File.WriteAllText(preregPath + ".sha256", Sha256Hex(preregistration), utf8);

Console.WriteLine($"incumbentId={incumbent.Id}");
Console.WriteLine($"incumbentHash={incumbent.Hash}");
Console.WriteLine($"policyId={PolicyId} policyHash={policyHash}");
Console.WriteLine($"planHash={plan.PlanHash} anchorPlanHash={anchorPlan.PlanHash}");
Console.WriteLine($"preregistrationHash={Sha256Hex(preregistration)}");
return 0;

static string Sha256Hex(string text) =>
    Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false).GetBytes(text))).ToLowerInvariant();
