using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Wip.Tholdem.Agents.Learning.Skills;
using Wip.Tholdem.Agents.Simulation;

namespace Wip.Tholdem.Agents.Learning;

/// <summary>
/// An immutable strategy artifact: data only, content-addressed and reproducible. There is no
/// clock, no identifier generator and no culture in its identity.
/// </summary>
/// <remarks>
/// The identifier embeds the first eight characters of <see cref="Hash"/>, so the identifier cannot
/// participate in its own hash. <see cref="Hash"/> therefore covers the canonical payload
/// (<see cref="ToHashInputJson"/>: version, parent, iteration, parameters and prompt) and
/// <see cref="ToCanonicalJson"/> appends the derived identifier. This is the only self-consistent
/// reading of "content-hashed id" and it keeps both values reproducible from data alone.
/// </remarks>
public sealed record StrategyArtifact(
    string Id,
    int Version,
    string? ParentId,
    int CreatedAtIteration,
    StrategyGenome Genome)
{
    public string Hash => ComputeHash(ToHashInputJson());

    public static StrategyArtifact Create(StrategyGenome genome, int iteration, StrategyArtifact? parent)
    {
        ArgumentNullException.ThrowIfNull(genome);

        var clamped = genome.Clamp();
        var version = parent is null ? 1 : parent.Version + 1;
        var hash = ComputeHash(Payload(version, parent?.Id, iteration, clamped));
        var id = "tholdem-strategy-v" + version.ToString(CultureInfo.InvariantCulture) + "-" + hash[..8];
        return new StrategyArtifact(id, version, parent?.Id, iteration, clamped);
    }

    /// <summary>The persisted form: a fixed key order and no timestamp anywhere.</summary>
    public string ToCanonicalJson()
    {
        var builder = new StringBuilder(512);
        builder.Append("{\"id\":");
        CanonicalJsonWriter.Text(builder, Id);
        builder.Append(",");
        AppendPayload(builder, Version, ParentId, CreatedAtIteration, Genome);
        builder.Append('}');
        return builder.ToString();
    }

    /// <summary>The canonical payload the content address is computed over.</summary>
    public string ToHashInputJson() => Payload(Version, ParentId, CreatedAtIteration, Genome);

    public static string ComputeHash(string canonicalPayload) =>
        Convert.ToHexString(SHA256.HashData(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(canonicalPayload)))
            .ToLowerInvariant();

    private static string Payload(int version, string? parentId, int iteration, StrategyGenome genome)
    {
        var builder = new StringBuilder(512);
        builder.Append('{');
        AppendPayload(builder, version, parentId, iteration, genome);
        builder.Append('}');
        return builder.ToString();
    }

    private static void AppendPayload(
        StringBuilder builder,
        int version,
        string? parentId,
        int iteration,
        StrategyGenome genome)
    {
        builder.Append("\"version\":");
        CanonicalJsonWriter.Number(builder, version);
        builder.Append(",\"parentId\":");
        if (parentId is null)
        {
            builder.Append("null");
        }
        else
        {
            CanonicalJsonWriter.Text(builder, parentId);
        }

        builder.Append(",\"createdAtIteration\":");
        CanonicalJsonWriter.Number(builder, iteration);
        builder.Append(",\"parameters\":{\"raiseThreshold\":");
        CanonicalJsonWriter.Number(builder, genome.RaiseThreshold);
        builder.Append(",\"callThreshold\":");
        CanonicalJsonWriter.Number(builder, genome.CallThreshold);
        builder.Append(",\"bluffFrequency\":");
        CanonicalJsonWriter.Number(builder, genome.BluffFrequency);
        builder.Append(",\"sizingMultiplier\":");
        CanonicalJsonWriter.Number(builder, genome.SizingMultiplier);
        builder.Append("},\"promptText\":");
        CanonicalJsonWriter.Text(builder, genome.PromptText);
        if (genome.Skills is not null)
        {
            builder.Append(",\"skills\":");
            StrategySkillSetJson.Write(builder, genome.Skills);
        }
    }
}
