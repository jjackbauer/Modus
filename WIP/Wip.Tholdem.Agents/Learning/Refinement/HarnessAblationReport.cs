using System.Text;
using Wip.Tholdem.Agents.Simulation;

namespace Wip.Tholdem.Agents.Learning.Refinement;

/// <summary>One configuration's measured row. Every value is derived from the finished training run.</summary>
public sealed record HarnessAblationConfigResult(
    string Config,
    string FinalChampionId,
    int Iterations,
    int VerifiedPromotions,
    double? MeanBbPer100,
    int ZeroEffectIterations,
    int InertProposals,
    int MemoryEntries,
    string? MemoryContentHash,
    string Note);

/// <summary>
/// The canonical <c>tholdem.ablation.v1</c> report. The top-level order is fixed
/// (<c>schema</c>, <c>seed</c>, <c>iterations</c>, <c>configurations</c>); each row then emits
/// <c>config</c>, <c>finalChampionId</c>, <c>iterations</c>, <c>verifiedPromotions</c>,
/// <c>meanBbPer100</c>, <c>zeroEffectIterations</c>, <c>inertProposals</c>, <c>memoryEntries</c>,
/// <c>memoryContentHash</c> and <c>note</c>. There is no clock, path, GUID or culture-dependent
/// value in it, so two runs with the same options produce identical bytes.
/// </summary>
public sealed record HarnessAblationReport(
    ulong Seed,
    int Iterations,
    IReadOnlyList<HarnessAblationConfigResult> Configurations)
{
    public const string Schema = "tholdem.ablation.v1";

    /// <summary>The canonical UTF-8 text, without a BOM and without a trailing newline.</summary>
    public string ToCanonicalJson()
    {
        var builder = new StringBuilder(1024);
        builder.Append("{\"schema\":");
        CanonicalJsonWriter.Text(builder, Schema);
        builder.Append(",\"seed\":");
        CanonicalJsonWriter.Number(builder, Seed);
        builder.Append(",\"iterations\":");
        CanonicalJsonWriter.Number(builder, Iterations);
        builder.Append(",\"configurations\":[");
        for (var index = 0; index < Configurations.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            var row = Configurations[index];
            builder.Append("{\"config\":");
            CanonicalJsonWriter.Text(builder, row.Config);
            builder.Append(",\"finalChampionId\":");
            CanonicalJsonWriter.Text(builder, row.FinalChampionId);
            builder.Append(",\"iterations\":");
            CanonicalJsonWriter.Number(builder, row.Iterations);
            builder.Append(",\"verifiedPromotions\":");
            CanonicalJsonWriter.Number(builder, row.VerifiedPromotions);
            builder.Append(",\"meanBbPer100\":");
            if (row.MeanBbPer100 is double mean)
            {
                CanonicalJsonWriter.Number(builder, mean);
            }
            else
            {
                builder.Append("null");
            }

            builder.Append(",\"zeroEffectIterations\":");
            CanonicalJsonWriter.Number(builder, row.ZeroEffectIterations);
            builder.Append(",\"inertProposals\":");
            CanonicalJsonWriter.Number(builder, row.InertProposals);
            builder.Append(",\"memoryEntries\":");
            CanonicalJsonWriter.Number(builder, row.MemoryEntries);
            builder.Append(",\"memoryContentHash\":");
            if (row.MemoryContentHash is null)
            {
                builder.Append("null");
            }
            else
            {
                CanonicalJsonWriter.Text(builder, row.MemoryContentHash);
            }

            builder.Append(",\"note\":");
            CanonicalJsonWriter.Text(builder, row.Note);
            builder.Append('}');
        }

        builder.Append("]}");
        return builder.ToString();
    }
}
