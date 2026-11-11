using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Simulation;

namespace Wip.Tholdem.Agents.Evaluation;

/// <summary>How the evaluator seats the focal policy against a composition's opponents.</summary>
public enum SeatPermutationMode
{
    FocalRotations = 0,
    FullFactorial = 1,
}

/// <summary>One paired block for one composition: its seed fingerprint, hands and mean.</summary>
public sealed record EvaluationUnit(
    string CompositionId,
    int BlockIndex,
    string SeedFingerprint,
    int Hands,
    double MeanBbPer100);

/// <summary>A composition's block estimate and the raw focal net chips behind it.</summary>
public sealed record CompositionEstimate(
    string CompositionId,
    PopulationEstimate Estimate,
    long ChallengerNetChips,
    long IncumbentNetChips);

/// <summary>One focal seat's block estimate and the number of hands that feed it.</summary>
public sealed record SeatEstimate(int Seat, PopulationEstimate Estimate, int Hands);

/// <summary>The worst block, the fifth-percentile block and the worst single hand, in bb/100.</summary>
public sealed record TailRisk(
    double WorstBlockBbPer100,
    double P05BlockBbPer100,
    double WorstHandBbPer100);

/// <summary>
/// The canonical paired four-seat evaluation report. Every field is derived from data alone: the
/// canonical JSON has a fixed key order, embeds every block unit and estimate, and contains no
/// clock, GUID, culture or ambient randomness.
/// </summary>
public sealed record PopulationEvaluationReport(
    string ChallengerId,
    string IncumbentId,
    string ChallengerPolicyHash,
    string IncumbentPolicyHash,
    string PlanHash,
    string GameConfigHash,
    string OpponentPoolId,
    string OpponentPoolHash,
    string EvaluatorVersion,
    int SeedBlocks,
    int HandsPerSeatMatch,
    string SeatPermutationMode,
    int RotationsPerBlock,
    PopulationEstimate Estimate,
    IReadOnlyList<EvaluationUnit> Units,
    IReadOnlyList<CompositionEstimate> PerComposition,
    IReadOnlyList<SeatEstimate> PerSeat,
    TailRisk Tail,
    long ChallengerNetChips,
    long IncumbentNetChips)
{
    public const string Schema = "tholdem.population-report.v2";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The content address of the canonical report.</summary>
    public string ReportHash => StrategyArtifact.ComputeHash(ToCanonicalJson());

    /// <summary>The fixed-key-order, culture-free JSON the report hash covers.</summary>
    public string ToCanonicalJson()
    {
        var builder = new StringBuilder(
            2048 + (Units.Count * 160) + (PerComposition.Count * 320) + (PerSeat.Count * 320));
        builder.Append("{\"schema\":");
        CanonicalJsonWriter.Text(builder, Schema);
        builder.Append(",\"challengerId\":");
        CanonicalJsonWriter.Text(builder, ChallengerId);
        builder.Append(",\"incumbentId\":");
        CanonicalJsonWriter.Text(builder, IncumbentId);
        builder.Append(",\"challengerPolicyHash\":");
        CanonicalJsonWriter.Text(builder, ChallengerPolicyHash);
        builder.Append(",\"incumbentPolicyHash\":");
        CanonicalJsonWriter.Text(builder, IncumbentPolicyHash);
        builder.Append(",\"planHash\":");
        CanonicalJsonWriter.Text(builder, PlanHash);
        builder.Append(",\"gameConfigHash\":");
        CanonicalJsonWriter.Text(builder, GameConfigHash);
        builder.Append(",\"opponentPoolId\":");
        CanonicalJsonWriter.Text(builder, OpponentPoolId);
        builder.Append(",\"opponentPoolHash\":");
        CanonicalJsonWriter.Text(builder, OpponentPoolHash);
        builder.Append(",\"evaluatorVersion\":");
        CanonicalJsonWriter.Text(builder, EvaluatorVersion);
        builder.Append(",\"seedBlocks\":");
        CanonicalJsonWriter.Number(builder, SeedBlocks);
        builder.Append(",\"handsPerSeatMatch\":");
        CanonicalJsonWriter.Number(builder, HandsPerSeatMatch);
        builder.Append(",\"seatPermutationMode\":");
        CanonicalJsonWriter.Text(builder, SeatPermutationMode);
        builder.Append(",\"rotationsPerBlock\":");
        CanonicalJsonWriter.Number(builder, RotationsPerBlock);
        builder.Append(",\"estimate\":");
        AppendEstimate(builder, Estimate);
        builder.Append(",\"units\":[");
        for (var index = 0; index < Units.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            AppendUnit(builder, Units[index]);
        }

        builder.Append("],\"perComposition\":[");
        for (var index = 0; index < PerComposition.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            AppendComposition(builder, PerComposition[index]);
        }

        builder.Append("],\"perSeat\":[");
        for (var index = 0; index < PerSeat.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            AppendSeat(builder, PerSeat[index]);
        }

        builder.Append("],\"tail\":{\"worstBlockBbPer100\":");
        CanonicalJsonWriter.Number(builder, Tail.WorstBlockBbPer100);
        builder.Append(",\"p05BlockBbPer100\":");
        CanonicalJsonWriter.Number(builder, Tail.P05BlockBbPer100);
        builder.Append(",\"worstHandBbPer100\":");
        CanonicalJsonWriter.Number(builder, Tail.WorstHandBbPer100);
        builder.Append("},\"challengerNetChips\":");
        CanonicalJsonWriter.Number(builder, ChallengerNetChips);
        builder.Append(",\"incumbentNetChips\":");
        CanonicalJsonWriter.Number(builder, IncumbentNetChips);
        builder.Append('}');
        return builder.ToString();
    }

    /// <summary>
    /// The first 16 lowercase hex characters of SHA-256 over the invariant decimal spelling of the
    /// block seed. The spelling uses the invariant culture, so the fingerprint is the same on every
    /// machine and locale.
    /// </summary>
    public static string SeedFingerprint(ulong blockSeed)
    {
        var decimalSeed = blockSeed.ToString(CultureInfo.InvariantCulture);
        var digest = SHA256.HashData(Utf8.GetBytes(decimalSeed));
        return Convert.ToHexString(digest).ToLowerInvariant()[..16];
    }

    private static void AppendEstimate(StringBuilder builder, PopulationEstimate estimate)
    {
        builder.Append("{\"meanBbPer100\":");
        CanonicalJsonWriter.Number(builder, estimate.MeanBbPer100);
        builder.Append(",\"lowerBoundBbPer100\":");
        CanonicalJsonWriter.Number(builder, estimate.LowerBoundBbPer100);
        builder.Append(",\"upperBoundBbPer100\":");
        CanonicalJsonWriter.Number(builder, estimate.UpperBoundBbPer100);
        builder.Append(",\"confidenceLevel\":");
        CanonicalJsonWriter.Number(builder, estimate.ConfidenceLevel);
        builder.Append(",\"method\":");
        CanonicalJsonWriter.Text(builder, estimate.Method);
        builder.Append(",\"unitCount\":");
        CanonicalJsonWriter.Number(builder, estimate.UnitCount);
        builder.Append(",\"standardError\":");
        CanonicalJsonWriter.Number(builder, estimate.StandardError);
        builder.Append(",\"normalLowerBoundBbPer100\":");
        CanonicalJsonWriter.Number(builder, estimate.NormalLowerBoundBbPer100);
        builder.Append(",\"bootstrapLowerBoundBbPer100\":");
        CanonicalJsonWriter.Number(builder, estimate.BootstrapLowerBoundBbPer100);
        builder.Append('}');
    }

    private static void AppendUnit(StringBuilder builder, EvaluationUnit unit)
    {
        builder.Append("{\"compositionId\":");
        CanonicalJsonWriter.Text(builder, unit.CompositionId);
        builder.Append(",\"blockIndex\":");
        CanonicalJsonWriter.Number(builder, unit.BlockIndex);
        builder.Append(",\"seedFingerprint\":");
        CanonicalJsonWriter.Text(builder, unit.SeedFingerprint);
        builder.Append(",\"hands\":");
        CanonicalJsonWriter.Number(builder, unit.Hands);
        builder.Append(",\"meanBbPer100\":");
        CanonicalJsonWriter.Number(builder, unit.MeanBbPer100);
        builder.Append('}');
    }

    private static void AppendComposition(StringBuilder builder, CompositionEstimate composition)
    {
        builder.Append("{\"compositionId\":");
        CanonicalJsonWriter.Text(builder, composition.CompositionId);
        builder.Append(",\"estimate\":");
        AppendEstimate(builder, composition.Estimate);
        builder.Append(",\"challengerNetChips\":");
        CanonicalJsonWriter.Number(builder, composition.ChallengerNetChips);
        builder.Append(",\"incumbentNetChips\":");
        CanonicalJsonWriter.Number(builder, composition.IncumbentNetChips);
        builder.Append('}');
    }

    private static void AppendSeat(StringBuilder builder, SeatEstimate seat)
    {
        builder.Append("{\"seat\":");
        CanonicalJsonWriter.Number(builder, seat.Seat);
        builder.Append(",\"estimate\":");
        AppendEstimate(builder, seat.Estimate);
        builder.Append(",\"hands\":");
        CanonicalJsonWriter.Number(builder, seat.Hands);
        builder.Append('}');
    }
}
