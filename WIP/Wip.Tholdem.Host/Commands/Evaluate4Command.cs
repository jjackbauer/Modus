using System.Text;
using Wip.Tholdem.Agents;
using Wip.Tholdem.Agents.Evaluation;
using Wip.Tholdem.Agents.Learning;

namespace Wip.Tholdem.Host;

/// <summary>
/// The additive four-seat <c>evaluate4</c> command. It reads a persisted, pre-registered
/// <see cref="EvaluationPlanV2"/> with <see cref="EvaluationPlanV2Json.Parse"/>, resolves the frozen
/// opponent pool by id, resolves each side as a strategy artifact or a data-only equity policy through
/// one factory, evaluates them with fresh agents, writes the canonical paired report, and, when an
/// anchor plan is supplied, writes the gate decision and the independent verifier's verdict. It never
/// writes the champion pointer.
/// </summary>
internal static class Evaluate4Command
{
    public const string UnknownPoolReason = "unknown-opponent-pool";

    public const string PlanInvalidReason = "evaluation-plan-invalid";

    private const string DecisionSchema = "tholdem.evaluate4-decision.v1";

    private const string VerificationSchema = "tholdem.evaluate4-verification.v1";

    private const string StrategyKind = "strategy";

    private const string EquityPolicyKind = "equity-policy";

    public static int Execute(CliOptions options, TextWriter output)
    {
        var planPath = options.Require("plan");
        var challengerId = CliPaths.SafeStrategyId(options.Require("challenger"));
        var incumbentId = CliPaths.SafeStrategyId(options.Require("incumbent"));
        var challengerKind = options.Optional("challenger-kind", StrategyKind);
        var incumbentKind = options.Optional("incumbent-kind", StrategyKind);
        var dataDirectory = CliPaths.ResolveDataDirectory(options);
        var outputDirectory = ResolveOutputDirectory(options, dataDirectory);

        var plan = LoadPlan(planPath);
        var pool = ResolvePool(plan, planPath);

        var primary = Evaluate(
            plan,
            pool,
            dataDirectory,
            challengerId,
            challengerKind,
            incumbentId,
            incumbentKind);
        var reportPath = WriteReport(outputDirectory, primary);

        string? decisionPath = null;
        string? verificationPath = null;
        PopulationPromotionDecision? decision = null;
        string? verifierReason = null;

        if (options.Get("anchor-plan") is { Length: > 0 } anchorPlanPath)
        {
            var anchorPlan = LoadPlan(anchorPlanPath);
            var anchorPool = ResolvePool(anchorPlan, anchorPlanPath);
            var anchor = Evaluate(
                anchorPlan,
                anchorPool,
                dataDirectory,
                challengerId,
                challengerKind,
                incumbentId,
                incumbentKind);
            decision = PopulationPromotionGate.Decide(primary, anchor, plan, anchorPlan);
            var suffix = primary.ReportHash[..8];
            decisionPath = WriteDocument(
                outputDirectory,
                "decision-" + suffix + ".json",
                DecisionJson(decision, primary, anchor));

            var verified = PopulationPromotionVerifier.Verify(
                decision,
                primary,
                anchor,
                plan,
                anchorPlan,
                () => Evaluate(
                    plan,
                    pool,
                    dataDirectory,
                    challengerId,
                    challengerKind,
                    incumbentId,
                    incumbentKind),
                () => Evaluate(
                    anchorPlan,
                    anchorPool,
                    dataDirectory,
                    challengerId,
                    challengerKind,
                    incumbentId,
                    incumbentKind),
                out var reason);

            if (!verified)
            {
                throw new CliInvariantException(
                    "verification-refused: " + reason + "; the persisted reports and the gate "
                    + "decision do not reproduce.");
            }

            verifierReason = reason;
            verificationPath = WriteDocument(
                outputDirectory,
                "verification-" + suffix + ".json",
                VerificationJson(decision, primary, anchor, reason));
        }

        if (options.Flag("json"))
        {
            output.WriteLine(primary.ToCanonicalJson());
            return TholdemCli.ExitOk;
        }

        WriteSummary(output, primary, reportPath, decisionPath, verificationPath);
        if (decision is not null)
        {
            output.WriteLine($"gate: {decision.Verdict} ({decision.Reason})");
            output.WriteLine($"verifier: verified ({verifierReason})");
        }

        return TholdemCli.ExitOk;
    }

    private static void WriteSummary(
        TextWriter output,
        PopulationEvaluationReport report,
        string reportPath,
        string? decisionPath,
        string? verificationPath)
    {
        output.WriteLine($"report: {reportPath}");
        output.WriteLine($"plan hash: {report.PlanHash}");
        output.WriteLine(
            $"mean bb/100: {CliJson.Format(report.Estimate.MeanBbPer100)}  "
            + $"lower bound: {CliJson.Format(report.Estimate.LowerBoundBbPer100)}  "
            + $"units: {CliJson.Format(report.Units.Count)}");
        if (decisionPath is not null && verificationPath is not null)
        {
            output.WriteLine($"decision: {decisionPath}");
            output.WriteLine($"verification: {verificationPath}");
        }
    }

    private static PopulationEvaluationReport Evaluate(
        EvaluationPlanV2 plan,
        FrozenOpponentPool pool,
        string dataDirectory,
        string challengerId,
        string challengerKind,
        string incumbentId,
        string incumbentKind)
    {
        try
        {
            var challenger = ResolveAgent(dataDirectory, challengerId, challengerKind);
            var incumbent = ResolveAgent(dataDirectory, incumbentId, incumbentKind);
            return PopulationMatchEvaluator.Evaluate(
                new PopulationEvaluationRequest(plan, pool),
                challenger.Agent,
                challenger.Id,
                challenger.PolicyHash,
                incumbent.Agent,
                incumbent.Id,
                incumbent.PolicyHash);
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
        {
            throw new CliArtifactException(
                PlanInvalidReason + ": the persisted plan cannot be evaluated: " + exception.Message,
                exception);
        }
    }

    /// <summary>
    /// One factory for both sides: the strategy path is unchanged (load the artifact, build a fresh
    /// <see cref="StrategyAgent"/> and hash the artifact), while the equity-policy path loads the
    /// data-only policy, builds a fresh <see cref="EquityPolicyAgent"/> and hashes the policy bytes.
    /// Every evaluation pass, including the verifier's recomputations, calls this factory again so
    /// the agents are always fresh and the hashes always come from the persisted data.
    /// </summary>
    private static ResolvedAgent ResolveAgent(string dataDirectory, string agentId, string kind) =>
        kind switch
        {
            StrategyKind => ResolveStrategy(dataDirectory, agentId),
            EquityPolicyKind => ResolveEquityPolicy(dataDirectory, agentId),
            _ => throw new CliUsageException(
                "unknown agent kind '" + kind + "'; supported kinds are '" + StrategyKind + "' and '"
                + EquityPolicyKind + "'."),
        };

    private static ResolvedAgent ResolveStrategy(string dataDirectory, string strategyId)
    {
        var artifact = StrategyArtifactStore.Load(dataDirectory, strategyId);
        return new ResolvedAgent(
            new StrategyAgent(artifact.Genome, artifact.Id),
            artifact.Id,
            artifact.Hash);
    }

    private static ResolvedAgent ResolveEquityPolicy(string dataDirectory, string policyId)
    {
        var policy = EquityPolicyStore.Load(dataDirectory, policyId);
        return new ResolvedAgent(
            new EquityPolicyAgent(policy, name: policyId),
            policyId,
            EquityPolicyJson.Hash(policy));
    }

    private sealed record ResolvedAgent(IPokerAgent Agent, string Id, string PolicyHash);

    private static EvaluationPlanV2 LoadPlan(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path, CliJson.Utf8);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CliArtifactException(
                $"evaluation plan '{path}' could not be read: {exception.Message}",
                exception);
        }

        try
        {
            return EvaluationPlanV2Json.Parse(json);
        }
        catch (FormatException exception)
        {
            throw new CliArtifactException(
                $"evaluation plan '{path}' is not a valid persisted plan: {exception.Message}",
                exception);
        }
    }

    private static FrozenOpponentPool ResolvePool(EvaluationPlanV2 plan, string planPath) =>
        plan.OpponentPoolId switch
        {
            FrozenOpponentPool.SelectionPoolId => FrozenOpponentPool.SelectionV1,
            FrozenOpponentPool.HoldoutPoolId => FrozenOpponentPool.HoldoutV1,
            _ => throw new CliArtifactException(
                UnknownPoolReason + ": evaluation plan '" + planPath + "' names opponent pool '"
                + plan.OpponentPoolId + "'; supported pools are '"
                + FrozenOpponentPool.SelectionPoolId + "' and '" + FrozenOpponentPool.HoldoutPoolId
                + "'."),
        };

    private static string ResolveOutputDirectory(CliOptions options, string dataDirectory)
    {
        var configured = options.Get("out");
        return Path.GetFullPath(
            string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(dataDirectory, "evaluations4")
                : configured);
    }

    private static string WriteReport(string outputDirectory, PopulationEvaluationReport report) =>
        WriteDocument(
            outputDirectory,
            "report-" + report.ReportHash[..8] + ".json",
            report.ToCanonicalJson());

    private static string WriteDocument(string outputDirectory, string fileName, string json)
    {
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, fileName);
        File.WriteAllText(path, json, CliJson.Utf8);
        return path;
    }

    private static string DecisionJson(
        PopulationPromotionDecision decision,
        PopulationEvaluationReport primary,
        PopulationEvaluationReport anchor)
    {
        var builder = new StringBuilder(512);
        builder.Append("{\"schema\":");
        builder.Append(CliJson.Text(DecisionSchema));
        builder.Append(",\"verdict\":");
        builder.Append(CliJson.Text(decision.Verdict.ToString()));
        builder.Append(",\"reason\":");
        builder.Append(CliJson.Text(decision.Reason));
        builder.Append(",\"lowerBoundBbPer100\":");
        builder.Append(CliJson.Number(decision.LowerBoundBbPer100));
        builder.Append(",\"marginBbPer100\":");
        builder.Append(CliJson.Number(decision.MarginBbPer100));
        builder.Append(",\"unitCount\":");
        builder.Append(CliJson.Number(decision.UnitCount));
        builder.Append(",\"adjustedConfidenceLevel\":");
        builder.Append(CliJson.Number(decision.AdjustedConfidenceLevel));
        builder.Append(",\"planHash\":");
        builder.Append(CliJson.Text(primary.PlanHash));
        builder.Append(",\"anchorPlanHash\":");
        builder.Append(CliJson.Text(anchor.PlanHash));
        builder.Append(",\"reportHash\":");
        builder.Append(CliJson.Text(primary.ReportHash));
        builder.Append(",\"anchorReportHash\":");
        builder.Append(CliJson.Text(anchor.ReportHash));
        builder.Append('}');
        return builder.ToString();
    }

    private static string VerificationJson(
        PopulationPromotionDecision decision,
        PopulationEvaluationReport primary,
        PopulationEvaluationReport anchor,
        string verifierReason)
    {
        var builder = new StringBuilder(512);
        builder.Append("{\"schema\":");
        builder.Append(CliJson.Text(VerificationSchema));
        builder.Append(",\"verified\":true,\"reason\":");
        builder.Append(CliJson.Text(verifierReason));
        builder.Append(",\"verdict\":");
        builder.Append(CliJson.Text(decision.Verdict.ToString()));
        builder.Append(",\"gateReason\":");
        builder.Append(CliJson.Text(decision.Reason));
        builder.Append(",\"planHash\":");
        builder.Append(CliJson.Text(primary.PlanHash));
        builder.Append(",\"anchorPlanHash\":");
        builder.Append(CliJson.Text(anchor.PlanHash));
        builder.Append(",\"reportHash\":");
        builder.Append(CliJson.Text(primary.ReportHash));
        builder.Append(",\"anchorReportHash\":");
        builder.Append(CliJson.Text(anchor.ReportHash));
        builder.Append('}');
        return builder.ToString();
    }
}
