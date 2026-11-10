using System.Globalization;
using System.Text;
using System.Text.Json;
using Wip.Tholdem.Agents.Learning.Refinement;
using Wip.Tholdem.Agents.Simulation;

namespace Wip.Tholdem.Agents.Learning;

/// <summary>
/// Seed derivation for the meta-loop. Inner training/evaluation seeds keep the streams they already
/// use (hand bases 1000/5000, streams 1/3); meta episode seeds use hand base 20000 stream 7, meta
/// comparison seeds hand base 30000 stream 7, the fresh episode seed hand base 40000 stream 11 and
/// the fresh comparison seed hand base 45000 stream 11. All four meta values are asserted distinct
/// from every inner seed the episode derives.
/// </summary>
public static class MetaSeedDerivation
{
    public const int EpisodeSeedHandBase = 20_000;

    public const int ComparisonSeedHandBase = 30_000;

    public const int FreshEpisodeSeedHandBase = 40_000;

    public const int FreshComparisonSeedHandBase = 45_000;

    public const ulong MetaStream = 7;

    public const ulong FreshStream = 11;

    public static ulong EpisodeSeed(ulong jobSeed, int metaIndex) =>
        SeedDerivation.Derive(jobSeed, EpisodeSeedHandBase + metaIndex, 0, 0, MetaStream);

    public static ulong ComparisonSeed(ulong jobSeed, int metaIndex) =>
        SeedDerivation.Derive(jobSeed, ComparisonSeedHandBase + metaIndex, 0, 0, MetaStream);

    public static ulong FreshEpisodeSeed(ulong jobSeed) =>
        SeedDerivation.Derive(jobSeed, FreshEpisodeSeedHandBase, 0, 0, FreshStream);

    public static ulong FreshComparisonSeed(ulong jobSeed) =>
        SeedDerivation.Derive(jobSeed, FreshComparisonSeedHandBase, 0, 0, FreshStream);

    /// <summary>
    /// The meta values plus every inner seed the episode derives for <paramref name="innerIterations"/>.
    /// The result is checked pairwise for duplicates; a duplicate is a derivation defect and throws.
    /// </summary>
    public static IReadOnlyList<ulong> EpisodeSeedSet(ulong jobSeed, int metaIndex, int innerIterations)
    {
        if (innerIterations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(innerIterations), innerIterations, "At least one inner iteration is required.");
        }

        var seeds = new List<ulong>(4 + (innerIterations * 2))
        {
            EpisodeSeed(jobSeed, metaIndex),
            ComparisonSeed(jobSeed, metaIndex),
        };

        var episodeSeed = seeds[0];
        for (var iteration = 1; iteration <= innerIterations; iteration++)
        {
            seeds.Add(SeedDerivation.Derive(episodeSeed, TrainingLoop.TrainingSeedHandBase + iteration, 0, 0, TrainingLoop.TrainingSeedStream));
            seeds.Add(SeedDerivation.Derive(episodeSeed, TrainingLoop.EvaluationSeedHandBase + iteration, 0, 0, TrainingLoop.EvaluationSeedStream));
        }

        for (var left = 0; left < seeds.Count; left++)
        {
            for (var right = left + 1; right < seeds.Count; right++)
            {
                if (seeds[left] == seeds[right])
                {
                    throw new InvalidOperationException(
                        "The meta seed derivation produced a duplicate; meta and inner seed domains are not disjoint.");
                }
            }
        }

        return seeds;
    }
}

/// <summary>Normal-interval arithmetic used by the meta hit-rate comparison.</summary>
public static class MetaStatistics
{
    public static double Mean(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            throw new ArgumentException("A mean needs at least one value.", nameof(values));
        }

        var sum = 0.0;
        for (var index = 0; index < values.Count; index++)
        {
            sum += values[index];
        }

        return sum / values.Count;
    }

    public static double HalfWidth(IReadOnlyList<double> values, double mean, double z)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count < 2)
        {
            return 0.0;
        }

        var sumSquares = 0.0;
        for (var index = 0; index < values.Count; index++)
        {
            var deviation = values[index] - mean;
            sumSquares += deviation * deviation;
        }

        var sampleStandardDeviation = Math.Sqrt(sumSquares / (values.Count - 1));
        return z * sampleStandardDeviation / Math.Sqrt(values.Count);
    }
}

/// <summary>The pre-registered hit-rate comparison over paired meta seeds.</summary>
public sealed record HitRateComparison(
    int Seeds,
    int InnerIterations,
    double CandidateHitRate,
    double IncumbentHitRate,
    double Improvement,
    double LowerBoundImprovement,
    double Z)
{
    public bool IsPreRegisteredImprovementMet(double requiredImprovement) =>
        Seeds >= 2 && LowerBoundImprovement > requiredImprovement;
}

/// <summary>Rebuilds the meta evaluation from raw episode outcomes; no self-reported score is trusted.</summary>
public static class HitRateCalculator
{
    public static HitRateComparison Compare(
        IReadOnlyList<int> candidateVerifiedPromotions,
        IReadOnlyList<int> incumbentVerifiedPromotions,
        int innerIterations,
        double z)
    {
        ArgumentNullException.ThrowIfNull(candidateVerifiedPromotions);
        ArgumentNullException.ThrowIfNull(incumbentVerifiedPromotions);

        if (innerIterations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(innerIterations), innerIterations, "At least one inner iteration is required.");
        }

        if (candidateVerifiedPromotions.Count != incumbentVerifiedPromotions.Count
            || candidateVerifiedPromotions.Count < 2)
        {
            throw new ArgumentException("Hit-rate comparison needs paired lists of at least two meta seeds.");
        }

        var differences = new double[candidateVerifiedPromotions.Count];
        var candidateRates = new double[candidateVerifiedPromotions.Count];
        var incumbentRates = new double[candidateVerifiedPromotions.Count];
        for (var index = 0; index < differences.Length; index++)
        {
            var candidate = candidateVerifiedPromotions[index];
            var incumbent = incumbentVerifiedPromotions[index];
            if (candidate < 0 || candidate > innerIterations || incumbent < 0 || incumbent > innerIterations)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(candidateVerifiedPromotions),
                    "A verified-promotion count must be between zero and the inner-iteration count.");
            }

            candidateRates[index] = candidate / (double)innerIterations;
            incumbentRates[index] = incumbent / (double)innerIterations;
            differences[index] = candidateRates[index] - incumbentRates[index];
        }

        var improvement = MetaStatistics.Mean(differences);
        var halfWidth = MetaStatistics.HalfWidth(differences, improvement, z);
        return new HitRateComparison(
            differences.Length,
            innerIterations,
            MetaStatistics.Mean(candidateRates),
            MetaStatistics.Mean(incumbentRates),
            improvement,
            improvement - halfWidth,
            z);
    }
}

/// <summary>The result of one meta-iteration's compound gate.</summary>
public sealed record MetaDecision(
    bool Eligible,
    string Reason,
    string FrozenManifestHash,
    HitRateComparison HitRate,
    double PrimaryLowerBound,
    double MarginBbPer100,
    int PrimarySeeds,
    bool PrimaryVerified,
    double RequiredHitRateImprovement,
    double FreshLowerBound,
    double FreshNonInferiorityFloor)
{
    public const string EligibleReason = "eligible";

    public const string InertProposerReason = "inert-proposer";

    public const string IdenticalProposerReason = "identical-proposer";

    public const string SelfComparisonReason = "champion-vs-itself";

    public const string HitRateInsufficientSeedsReason = "hit-rate-insufficient-meta-seeds";

    public const string HitRateBelowImprovementReason = "hit-rate-below-pre-registered-improvement";

    public const string PrimaryUnverifiedReason = "primary-verifier-refused";

    public const string PrimaryRejectedReason = "primary-gate-rejected";

    public const string FreshRegressionReason = "fresh-holdout-regression";
}

/// <summary>The persisted, recomputable record of one meta-iteration.</summary>
public sealed record MetaEvaluationDocument(
    string CandidateId,
    string IncumbentId,
    ProposerGenome CandidateProposer,
    ProposerGenome IncumbentProposer,
    int MetaSeeds,
    int InnerIterations,
    EvaluationReport PrimaryReport,
    EvaluationReport? FreshReport,
    IReadOnlyList<int> CandidateVerifiedPerSeed,
    IReadOnlyList<int> IncumbentVerifiedPerSeed,
    double RequiredHitRateImprovement,
    EvaluationCriteria Criteria,
    string FrozenManifestHash,
    MetaDecision Decision,
    string Outcome)
{
    public const int SchemaVersion = 1;

    public string ReportHash => StrategyArtifact.ComputeHash(PrimaryReport.ToCanonicalJson());

    public string ToCanonicalJson()
    {
        var builder = new StringBuilder(2048);
        builder.Append("{\"schemaVersion\":");
        CanonicalJsonWriter.Number(builder, SchemaVersion);
        builder.Append(",\"candidateId\":");
        CanonicalJsonWriter.Text(builder, CandidateId);
        builder.Append(",\"incumbentId\":");
        CanonicalJsonWriter.Text(builder, IncumbentId);
        builder.Append(",\"candidateProposer\":");
        AppendProposer(builder, CandidateProposer);
        builder.Append(",\"incumbentProposer\":");
        AppendProposer(builder, IncumbentProposer);
        builder.Append(",\"metaSeeds\":");
        CanonicalJsonWriter.Number(builder, MetaSeeds);
        builder.Append(",\"innerIterations\":");
        CanonicalJsonWriter.Number(builder, InnerIterations);
        builder.Append(",\"primaryReport\":");
        builder.Append(PrimaryReport.ToCanonicalJson());
        builder.Append(",\"freshReport\":");
        if (FreshReport is null)
        {
            builder.Append("null");
        }
        else
        {
            builder.Append(FreshReport.ToCanonicalJson());
        }

        builder.Append(",\"candidateVerifiedPerSeed\":");
        AppendIntArray(builder, CandidateVerifiedPerSeed);
        builder.Append(",\"incumbentVerifiedPerSeed\":");
        AppendIntArray(builder, IncumbentVerifiedPerSeed);
        builder.Append(",\"requiredHitRateImprovement\":");
        CanonicalJsonWriter.Number(builder, RequiredHitRateImprovement);
        builder.Append(",\"criteria\":{\"marginBbPer100\":");
        CanonicalJsonWriter.Number(builder, Criteria.MarginBbPer100);
        builder.Append(",\"minSeeds\":");
        CanonicalJsonWriter.Number(builder, Criteria.MinSeeds);
        builder.Append(",\"z\":");
        CanonicalJsonWriter.Number(builder, Criteria.Z);
        builder.Append("},\"frozenManifestHash\":");
        CanonicalJsonWriter.Text(builder, FrozenManifestHash);
        builder.Append(",\"decision\":{\"eligible\":");
        CanonicalJsonWriter.Boolean(builder, Decision.Eligible);
        builder.Append(",\"reason\":");
        CanonicalJsonWriter.Text(builder, Decision.Reason);
        builder.Append(",\"primaryLowerBound\":");
        CanonicalJsonWriter.Number(builder, Decision.PrimaryLowerBound);
        builder.Append(",\"primarySeeds\":");
        CanonicalJsonWriter.Number(builder, Decision.PrimarySeeds);
        builder.Append(",\"primaryVerified\":");
        CanonicalJsonWriter.Boolean(builder, Decision.PrimaryVerified);
        builder.Append(",\"hitRateImprovement\":");
        CanonicalJsonWriter.Number(builder, Decision.HitRate.Improvement);
        builder.Append(",\"hitRateLowerBound\":");
        CanonicalJsonWriter.Number(builder, Decision.HitRate.LowerBoundImprovement);
        builder.Append(",\"freshLowerBound\":");
        CanonicalJsonWriter.Number(builder, Decision.FreshLowerBound);
        builder.Append("},\"outcome\":");
        CanonicalJsonWriter.Text(builder, Outcome);
        builder.Append('}');
        return builder.ToString();
    }

    public string DocumentHash => StrategyArtifact.ComputeHash(ToCanonicalJson());

    private static void AppendProposer(StringBuilder builder, ProposerGenome genome)
    {
        builder.Append("{\"mutationStep\":");
        CanonicalJsonWriter.Number(builder, genome.MutationStep);
        builder.Append(",\"parameterSelectionPolicy\":");
        CanonicalJsonWriter.Text(builder, MetaArtifact.ParameterSelectionPolicyName(genome.ParameterSelectionPolicy));
        builder.Append(",\"evaluationBudgetShare\":");
        CanonicalJsonWriter.Number(builder, genome.EvaluationBudgetShare);
        builder.Append(",\"promptTemplate\":");
        CanonicalJsonWriter.Text(builder, genome.PromptTemplate);
        MetaArtifact.AppendRefinementStrategy(builder, genome.RefinementStrategy);
        builder.Append('}');
    }

    private static void AppendIntArray(StringBuilder builder, IReadOnlyList<int> values)
    {
        builder.Append('[');
        for (var index = 0; index < values.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            CanonicalJsonWriter.Number(builder, values[index]);
        }

        builder.Append(']');
    }
}

/// <summary>
/// Re-runs every meta gate from a persisted document. The meta-loop and the promote endpoint call
/// this same function, so a stored "eligible" flag is never accepted on its own.
/// </summary>
public static class MetaDecisionVerifier
{
    public const string VerifiedReason = "meta-verifier:verified";

    public const string FrozenMismatchReason = "meta-verifier:frozen-manifest-mismatch";

    public const string IdenticalProposerReason = "meta-verifier:identical-proposer";

    public const string InertProposerReason = "meta-verifier:inert-proposer";

    public const string ReportIdReason = "meta-verifier:report-id-mismatch";

    public const string HitRateSeedsReason = "meta-verifier:insufficient-meta-seeds";

    public const string HitRateReason = "meta-verifier:hit-rate-below-improvement";

    public const string PrimaryReason = "meta-verifier:primary-gate-rejected";

    public const string PrimaryVerifierReason = "meta-verifier:primary-verifier-refused";

    public const string FreshReason = "meta-verifier:fresh-holdout-regression";

    public static bool Verify(
        MetaEvaluationDocument document,
        FrozenManifest capturedManifest,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(capturedManifest);

        if (!string.Equals(document.FrozenManifestHash, capturedManifest.ManifestHash, StringComparison.Ordinal)
            || !FrozenComponents.MatchesPinned(capturedManifest, out _))
        {
            reason = FrozenMismatchReason;
            return false;
        }

        if (string.Equals(document.CandidateId, document.IncumbentId, StringComparison.Ordinal))
        {
            reason = IdenticalProposerReason;
            return false;
        }

        if (document.CandidateProposer.BehaviorEquals(document.IncumbentProposer))
        {
            reason = InertProposerReason;
            return false;
        }

        if (document.PrimaryReport.ChallengerId != document.CandidateId
            || document.PrimaryReport.IncumbentId != document.IncumbentId)
        {
            reason = ReportIdReason;
            return false;
        }

        if (document.MetaSeeds < document.Criteria.MinSeeds)
        {
            reason = HitRateSeedsReason;
            return false;
        }

        var hitRate = HitRateCalculator.Compare(
            document.CandidateVerifiedPerSeed,
            document.IncumbentVerifiedPerSeed,
            document.InnerIterations,
            document.Criteria.Z);
        if (hitRate.LowerBoundImprovement <= document.RequiredHitRateImprovement)
        {
            reason = HitRateReason;
            return false;
        }

        var decision = new StatisticalPromotionPolicy().Decide(document.PrimaryReport, document.Criteria);
        if (!decision.Promoted)
        {
            reason = PrimaryReason;
            return false;
        }

        if (!PromotionVerifier.Verify(decision, document.PrimaryReport, document.Criteria, out _))
        {
            reason = PrimaryVerifierReason;
            return false;
        }

        if (document.FreshReport is null
            || document.FreshReport.LowerBoundBbPer100 < -document.Criteria.MarginBbPer100)
        {
            reason = FreshReason;
            return false;
        }

        reason = VerifiedReason;
        return true;
    }
}
