using System.Text.Json;

namespace Wip.Tholdem.Agents.Learning;

/// <summary>Reads a persisted <see cref="MetaEvaluationDocument"/> back without trusting its verdict.</summary>
public static class MetaEvaluationJson
{
    public const string NotJsonReason = "meta-document-not-json";

    public const string ShapeReason = "meta-document-shape-invalid";

    public const string NotCanonicalReason = "meta-document-not-canonical";

    public static bool TryParse(string? json, out MetaEvaluationDocument? document, out string reason)
    {
        document = null;
        reason = NotJsonReason;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return false;
        }

        using (parsed)
        {
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                reason = ShapeReason;
                return false;
            }

            try
            {
                if (root.GetProperty("schemaVersion").GetInt32() != MetaEvaluationDocument.SchemaVersion)
                {
                    reason = ShapeReason;
                    return false;
                }

                var candidateProposer = ParseProposer(root.GetProperty("candidateProposer"));
                var incumbentProposer = ParseProposer(root.GetProperty("incumbentProposer"));
                if (!EvaluationReportJson.TryParse(root.GetProperty("primaryReport").GetRawText(), out var primary, out _)
                    || primary is null)
                {
                    reason = ShapeReason;
                    return false;
                }

                EvaluationReport? fresh = null;
                var freshElement = root.GetProperty("freshReport");
                if (freshElement.ValueKind != JsonValueKind.Null)
                {
                    if (!EvaluationReportJson.TryParse(freshElement.GetRawText(), out fresh, out _) || fresh is null)
                    {
                        reason = ShapeReason;
                        return false;
                    }
                }

                var candidateVerified = ParseIntArray(root.GetProperty("candidateVerifiedPerSeed"));
                var incumbentVerified = ParseIntArray(root.GetProperty("incumbentVerifiedPerSeed"));
                var requiredImprovement = root.GetProperty("requiredHitRateImprovement").GetDouble();
                var criteriaRoot = root.GetProperty("criteria");
                var criteria = new EvaluationCriteria(
                    criteriaRoot.GetProperty("marginBbPer100").GetDouble(),
                    criteriaRoot.GetProperty("minSeeds").GetInt32(),
                    criteriaRoot.GetProperty("z").GetDouble());
                var frozenHash = root.GetProperty("frozenManifestHash").GetString() ?? string.Empty;
                var decisionRoot = root.GetProperty("decision");
                var innerIterations = root.GetProperty("innerIterations").GetInt32();
                var metaSeeds = root.GetProperty("metaSeeds").GetInt32();
                var candidateId = root.GetProperty("candidateId").GetString() ?? string.Empty;
                var incumbentId = root.GetProperty("incumbentId").GetString() ?? string.Empty;
                var outcome = root.GetProperty("outcome").GetString() ?? string.Empty;

                var hitRate = HitRateCalculator.Compare(
                    candidateVerified,
                    incumbentVerified,
                    innerIterations,
                    criteria.Z);
                var decision = new MetaDecision(
                    decisionRoot.GetProperty("eligible").GetBoolean(),
                    decisionRoot.GetProperty("reason").GetString() ?? string.Empty,
                    frozenHash,
                    hitRate,
                    decisionRoot.GetProperty("primaryLowerBound").GetDouble(),
                    criteria.MarginBbPer100,
                    decisionRoot.GetProperty("primarySeeds").GetInt32(),
                    decisionRoot.GetProperty("primaryVerified").GetBoolean(),
                    requiredImprovement,
                    fresh?.LowerBoundBbPer100 ?? 0.0,
                    -criteria.MarginBbPer100);

                var candidate = new MetaEvaluationDocument(
                    candidateId,
                    incumbentId,
                    candidateProposer,
                    incumbentProposer,
                    metaSeeds,
                    innerIterations,
                    primary,
                    fresh,
                    candidateVerified,
                    incumbentVerified,
                    requiredImprovement,
                    criteria,
                    frozenHash,
                    decision,
                    outcome);

                if (candidate.MetaSeeds < 2
                    || candidate.InnerIterations < 1
                    || candidate.CandidateVerifiedPerSeed.Count != candidate.MetaSeeds
                    || candidate.IncumbentVerifiedPerSeed.Count != candidate.MetaSeeds)
                {
                    reason = ShapeReason;
                    return false;
                }

                if (!string.Equals(candidate.ToCanonicalJson(), json, StringComparison.Ordinal))
                {
                    reason = NotCanonicalReason;
                    return false;
                }

                document = candidate;
                reason = MetaDecisionVerifier.VerifiedReason;
                return true;
            }
            catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException
                or FormatException or OverflowException or ArgumentException)
            {
                reason = ShapeReason;
                return false;
            }
        }
    }

    private static ProposerGenome ParseProposer(JsonElement element)
    {
        var policyName = element.GetProperty("parameterSelectionPolicy").GetString() ?? string.Empty;
        var policy = policyName switch
        {
            "sessionDriven" => ParameterSelectionPolicy.SessionDriven,
            "cyclic" => ParameterSelectionPolicy.Cyclic,
            "aggressiveFirst" => ParameterSelectionPolicy.AggressiveFirst,
            _ => throw new FormatException("Unknown parameter selection policy."),
        };

        return new ProposerGenome(
            element.GetProperty("mutationStep").GetDouble(),
            policy,
            element.GetProperty("evaluationBudgetShare").GetDouble(),
            element.GetProperty("promptTemplate").GetString() ?? StrategyGenome.DefaultPromptText,
            MetaArtifact.ReadRefinementStrategy(element)).Clamp();
    }

    private static int[] ParseIntArray(JsonElement element)
    {
        var values = new List<int>();
        foreach (var item in element.EnumerateArray())
        {
            values.Add(item.GetInt32());
        }

        return values.ToArray();
    }
}
