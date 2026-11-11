using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Wip.Tholdem.Agents;
using Wip.Tholdem.Agents.Evaluation;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;

// Pre-registered Milestone B control experiment. It writes the plan BEFORE evaluating, runs the
// paired four-seat evaluator, asks the gate, then asks the independent verifier to recompute both
// reports with fresh agents. No clock, GUID or culture enters any evaluated value.

var outputDirectory = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath(Path.Combine("docs", "goal-tholdem-enhancement", "experiments", "control-v1", "results"));
Directory.CreateDirectory(outputDirectory);

var game = GameConfiguration.FourSeatCash();
var training = new SeedDomain("control-training", SeedPurpose.Training, 0xC0FFEE01UL, 64);
var selection = new SeedDomain("control-selection", SeedPurpose.Selection, 0xC0FFEE02UL, 64);
var holdout = new SeedDomain("control-holdout", SeedPurpose.Holdout, 0xC0FFEE03UL, 288);
var audit = new SeedDomain("control-audit", SeedPurpose.Audit, 0xC0FFEE04UL, 32);
var domains = new[] { training, selection, holdout, audit };
var primaryCompositions = new[] { "primary-mixed", "three-tag", "three-station", "pressure" };

var plan = EvaluationPlanV2.Create(
    game,
    FrozenOpponentPool.HoldoutPoolId,
    primaryCompositions,
    domains,
    handsPerSeatMatch: 20,
    minimumPracticalEffectBbPer100: 10.0,
    confidenceLevel: 0.95,
    plannedCandidates: 1,
    anchorRegressionToleranceBbPer100: 5.0);

var anchorPlan = EvaluationPlanV2.Create(
    game,
    FrozenOpponentPool.HoldoutPoolId,
    new[] { "primary-mixed" },
    domains,
    handsPerSeatMatch: 20,
    minimumPracticalEffectBbPer100: 10.0,
    confidenceLevel: 0.95,
    plannedCandidates: 1,
    anchorRegressionToleranceBbPer100: 5.0);

const string ChallengerId = "control-fold-only";
const string IncumbentId = "control-call-any";
var challengerPolicyHash = Sha256Hex("scripted:fold-only:v1");
var incumbentPolicyHash = Sha256Hex("scripted:call-any:v1");
var primaryRequest = new PopulationEvaluationRequest(plan, FrozenOpponentPool.HoldoutV1, SeatPermutationMode.FocalRotations);
var anchorRequest = new PopulationEvaluationRequest(anchorPlan, FrozenOpponentPool.HoldoutV1, SeatPermutationMode.FocalRotations);

var preregistration =
    "{\"schema\":\"tholdem.control-experiment.preregistration.v1\",\"experimentId\":\"control-v1\","
    + "\"date\":\"2026-11-10\","
    + "\"hypothesis\":\"A scripted fold-only policy beats a scripted call-any policy on the pre-registered four-seat holdout population.\","
    + "\"pilot\":{\"source\":\"Tests.Evaluation.PopulationEvaluatorTests (selection pool, three-tag)\",\"blocks\":96,\"handsPerSeatMatch\":20,\"meanBbPer100\":97.51041666666667,\"lowerBoundBbPer100\":57.95538331320324},"
    + "\"decisionRule\":{\"gate\":\"PopulationPromotionGate\",\"minimumPracticalEffectBbPer100\":10,\"confidenceLevel\":0.95,\"plannedCandidates\":1,\"anchorRegressionToleranceBbPer100\":5,\"verdicts\":[\"verified-improvement\",\"rejected\",\"inconclusive\",\"unsafe\",\"unverified\"]},"
    + "\"policies\":[{\"id\":\"" + ChallengerId + "\",\"kind\":\"scripted-fold-only\",\"policyHash\":\"" + challengerPolicyHash + "\"},{\"id\":\"" + IncumbentId + "\",\"kind\":\"scripted-call-any\",\"policyHash\":\"" + incumbentPolicyHash + "\"}],"
    + "\"primaryPlanHash\":\"" + plan.PlanHash + "\","
    + "\"anchorPlanHash\":\"" + anchorPlan.PlanHash + "\","
    + "\"primaryPlan\":" + plan.ToCanonicalJson() + ","
    + "\"anchorPlan\":" + anchorPlan.ToCanonicalJson()
    + "}";
File.WriteAllText(Path.Combine(outputDirectory, "preregistration.json"), preregistration, new UTF8Encoding(false));
File.WriteAllText(Path.Combine(outputDirectory, "preregistration.sha256"), Sha256Hex(preregistration), new UTF8Encoding(false));

Console.WriteLine($"preregistration hash {Sha256Hex(preregistration)}");
Console.WriteLine($"primary plan hash {plan.PlanHash} seedBlocks {plan.SeedBlocks}");
Console.WriteLine($"anchor  plan hash {anchorPlan.PlanHash}");

var primary = PopulationMatchEvaluator.Evaluate(
    primaryRequest,
    new FoldOnlyAgent(ChallengerId),
    ChallengerId,
    challengerPolicyHash,
    new CallAnyAgent(IncumbentId),
    IncumbentId,
    incumbentPolicyHash);
File.WriteAllText(Path.Combine(outputDirectory, "primary-report.json"), primary.ToCanonicalJson(), new UTF8Encoding(false));
Console.WriteLine($"primary mean {primary.Estimate.MeanBbPer100:R} lower {primary.Estimate.LowerBoundBbPer100:R} units {primary.Units.Count}");

var anchor = PopulationMatchEvaluator.Evaluate(
    anchorRequest,
    new FoldOnlyAgent(ChallengerId),
    ChallengerId,
    challengerPolicyHash,
    new CallAnyAgent(IncumbentId),
    IncumbentId,
    incumbentPolicyHash);
File.WriteAllText(Path.Combine(outputDirectory, "anchor-report.json"), anchor.ToCanonicalJson(), new UTF8Encoding(false));
Console.WriteLine($"anchor  mean {anchor.Estimate.MeanBbPer100:R} lower {anchor.Estimate.LowerBoundBbPer100:R} units {anchor.Units.Count}");

var decision = PopulationPromotionGate.Decide(primary, anchor, plan, anchorPlan);
var decisionJson =
    "{\"schema\":\"tholdem.control-experiment.decision.v1\",\"verdict\":\"" + decision.Verdict + "\",\"reason\":\"" + decision.Reason
    + "\",\"lowerBoundBbPer100\":" + decision.LowerBoundBbPer100.ToString("R", CultureInfo.InvariantCulture)
    + ",\"marginBbPer100\":" + decision.MarginBbPer100.ToString("R", CultureInfo.InvariantCulture)
    + ",\"unitCount\":" + decision.UnitCount.ToString(CultureInfo.InvariantCulture)
    + ",\"adjustedConfidenceLevel\":" + decision.AdjustedConfidenceLevel.ToString("R", CultureInfo.InvariantCulture)
    + ",\"primaryReportHash\":\"" + primary.ReportHash + "\",\"anchorReportHash\":\"" + anchor.ReportHash + "\"}";
File.WriteAllText(Path.Combine(outputDirectory, "decision.json"), decisionJson, new UTF8Encoding(false));
Console.WriteLine($"decision {decision.Verdict} {decision.Reason} lower {decision.LowerBoundBbPer100:R}");

var verified = PopulationPromotionVerifier.Verify(
    decision,
    primary,
    anchor,
    plan,
    anchorPlan,
    () => PopulationMatchEvaluator.Evaluate(
        primaryRequest,
        new FoldOnlyAgent(ChallengerId),
        ChallengerId,
        challengerPolicyHash,
        new CallAnyAgent(IncumbentId),
        IncumbentId,
        incumbentPolicyHash),
    () => PopulationMatchEvaluator.Evaluate(
        anchorRequest,
        new FoldOnlyAgent(ChallengerId),
        ChallengerId,
        challengerPolicyHash,
        new CallAnyAgent(IncumbentId),
        IncumbentId,
        incumbentPolicyHash),
    out var verificationReason);

var verificationJson =
    "{\"schema\":\"tholdem.control-experiment.verification.v1\",\"verified\":" + (verified ? "true" : "false")
    + ",\"reason\":\"" + verificationReason + "\",\"decisionVerdict\":\"" + decision.Verdict + "\",\"decisionReason\":\"" + decision.Reason
    + "\",\"primaryReportHash\":\"" + primary.ReportHash + "\",\"anchorReportHash\":\"" + anchor.ReportHash + "\"}";
File.WriteAllText(Path.Combine(outputDirectory, "verification.json"), verificationJson, new UTF8Encoding(false));
Console.WriteLine($"verifier {verified} {verificationReason}");

// Negative controls: the same reports must not justify an invented promotion, and a recomputation
// that returns a different report must be refused.
var forced = new PopulationPromotionDecision(
    PromotionVerdict.VerifiedImprovement,
    "forced-control",
    999.0,
    plan.MinimumPracticalEffectBbPer100,
    primary.Units.Count,
    0.95);
var forcedAccepted = PopulationPromotionVerifier.Verify(
    forced,
    primary,
    anchor,
    plan,
    anchorPlan,
    () => primary,
    () => anchor,
    out var forcedReason);

var tamperedPrimary = primary with
{
    Units = primary.Units.Select(unit => unit with { MeanBbPer100 = unit.MeanBbPer100 - 1000.0 }).ToArray(),
};
var tamperedAccepted = PopulationPromotionVerifier.Verify(
    decision,
    tamperedPrimary,
    anchor,
    plan,
    anchorPlan,
    () => primary,
    () => anchor,
    out var tamperedReason);

var controlsJson =
    "{\"schema\":\"tholdem.control-experiment.controls.v1\","
    + "\"forcedPromotionAccepted\":" + (forcedAccepted ? "true" : "false") + ",\"forcedPromotionReason\":\"" + forcedReason + "\","
    + "\"tamperedReportAccepted\":" + (tamperedAccepted ? "true" : "false") + ",\"tamperedReportReason\":\"" + tamperedReason + "\"}";
File.WriteAllText(Path.Combine(outputDirectory, "controls.json"), controlsJson, new UTF8Encoding(false));
Console.WriteLine($"controls forcedAccepted={forcedAccepted} ({forcedReason}) tamperedAccepted={tamperedAccepted} ({tamperedReason})");

return verified && !forcedAccepted && !tamperedAccepted ? 0 : 1;

static string Sha256Hex(string text) =>
    Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false).GetBytes(text))).ToLowerInvariant();

sealed class FoldOnlyAgent : IPokerAgent
{
    public FoldOnlyAgent(string name) => Name = name;
    public string Name { get; }
    public ValueTask<PokerAction> ActAsync(PlayerObservation observation, AgentTurnContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var kind in observation.LegalActionKinds)
        {
            if (kind == ActionKind.Fold) return ValueTask.FromResult(PokerAction.Fold());
        }
        foreach (var kind in observation.LegalActionKinds)
        {
            if (kind == ActionKind.Check) return ValueTask.FromResult(PokerAction.Check());
        }
        return ValueTask.FromResult(PokerAction.Call());
    }
}

sealed class CallAnyAgent : IPokerAgent
{
    public CallAnyAgent(string name) => Name = name;
    public string Name { get; }
    public ValueTask<PokerAction> ActAsync(PlayerObservation observation, AgentTurnContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var kind in observation.LegalActionKinds)
        {
            if (kind == ActionKind.Call) return ValueTask.FromResult(PokerAction.Call());
        }
        foreach (var kind in observation.LegalActionKinds)
        {
            if (kind == ActionKind.Check) return ValueTask.FromResult(PokerAction.Check());
        }
        foreach (var kind in observation.LegalActionKinds)
        {
            if (kind == ActionKind.AllIn) return ValueTask.FromResult(PokerAction.AllIn());
        }
        return ValueTask.FromResult(PokerAction.Fold());
    }
}
