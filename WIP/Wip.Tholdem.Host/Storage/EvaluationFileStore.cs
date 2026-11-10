using System.Text;
using System.Text.Json;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Simulation;

namespace Wip.Tholdem.Host;

/// <summary>The parsed content of one <c>cli-&lt;challenger&gt;-vs-&lt;incumbent&gt;.json</c> file.</summary>
internal sealed record CliEvaluationFile(EvaluationCriteria Criteria, EvaluationReport Report);

/// <summary>
/// The CLI evaluation document: the graded report plus the exact criteria it must be verified
/// against. Canonical JSON, fixed key order, no timestamp.
/// </summary>
internal static class EvaluationFileStore
{
    public const string Schema = "tholdem.cli-evaluation.v1";

    public static string PathFor(string dataDirectory, string challengerId, string incumbentId) =>
        Path.Combine(
            dataDirectory,
            "evaluations",
            "cli-" + challengerId + "-vs-" + incumbentId + ".json");

    public static string Write(
        string dataDirectory,
        EvaluationCriteria criteria,
        EvaluationReport report)
    {
        var directory = Path.Combine(dataDirectory, "evaluations");
        Directory.CreateDirectory(directory);
        var path = PathFor(dataDirectory, report.ChallengerId, report.IncumbentId);

        var builder = new StringBuilder(512);
        builder.Append("{\"schema\":");
        builder.Append(CliJson.Text(Schema));
        builder.Append(",\"criteria\":{\"marginBbPer100\":");
        builder.Append(CliJson.Number(criteria.MarginBbPer100));
        builder.Append(",\"minSeeds\":");
        builder.Append(CliJson.Number(criteria.MinSeeds));
        builder.Append("},\"report\":");
        builder.Append(report.ToCanonicalJson());
        builder.Append('}');

        File.WriteAllText(path, builder.ToString(), CliJson.Utf8);
        return path;
    }

    public static CliEvaluationFile Read(string path)
    {
        if (!File.Exists(path))
        {
            throw new CliArtifactException($"CLI evaluation file '{path}' does not exist.");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("the evaluation file must be a JSON object.");
            }

            var schema = root.GetProperty("schema").GetString();
            if (!string.Equals(schema, Schema, StringComparison.Ordinal))
            {
                throw new FormatException($"the evaluation schema must be '{Schema}'; observed '{schema}'.");
            }

            var criteria = root.GetProperty("criteria");
            var margin = criteria.GetProperty("marginBbPer100").GetDouble();
            var minSeeds = criteria.GetProperty("minSeeds").GetInt32();
            if (!double.IsFinite(margin))
            {
                throw new FormatException("criteria.marginBbPer100 must be finite.");
            }

            if (minSeeds < 1)
            {
                throw new FormatException("criteria.minSeeds must be positive.");
            }

            var report = ParseReport(root.GetProperty("report"));
            return new CliEvaluationFile(new EvaluationCriteria(margin, minSeeds), report);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException
            or FormatException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            throw new CliArtifactException(
                $"CLI evaluation file '{path}' is not a valid evaluation: {exception.Message}",
                exception);
        }
    }

    private static EvaluationReport ParseReport(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("report must be a JSON object.");
        }

        var challengerId = RequiredString(element, "challengerId");
        var incumbentId = RequiredString(element, "incumbentId");
        var seeds = RequiredInt32(element, "seeds");
        var handsPerMatch = RequiredInt32(element, "handsPerMatch");
        var bigBlind = RequiredInt64(element, "bigBlind");
        var mean = RequiredDouble(element, "meanBbPer100");
        var interval = element.GetProperty("bbPer100Ci");
        var low = RequiredDouble(interval, "low");
        var high = RequiredDouble(interval, "high");
        var lowerBound = RequiredDouble(element, "lowerBoundBbPer100");
        var challengerWins = RequiredInt32(element, "challengerWins");
        var incumbentWins = RequiredInt32(element, "incumbentWins");
        var ties = RequiredInt32(element, "ties");
        var challengerNetChips = RequiredInt64(element, "challengerNetChips");

        if (seeds < 1 || handsPerMatch < 1 || bigBlind < 1)
        {
            throw new FormatException("report seeds, handsPerMatch and bigBlind must be positive.");
        }

        return new EvaluationReport(
            challengerId,
            incumbentId,
            seeds,
            handsPerMatch,
            bigBlind,
            mean,
            new ConfidenceInterval(low, high),
            lowerBound,
            challengerWins,
            incumbentWins,
            ties,
            challengerNetChips);
    }

    private static string RequiredString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"report.{name} must be a string.");
        }

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException($"report.{name} must not be empty.");
        }

        return text;
    }

    private static int RequiredInt32(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var result))
        {
            throw new FormatException($"report.{name} must be a 32-bit integer.");
        }

        return result;
    }

    private static long RequiredInt64(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var result))
        {
            throw new FormatException($"report.{name} must be a 64-bit integer.");
        }

        return result;
    }

    private static double RequiredDouble(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number
            || !value.TryGetDouble(out var result)
            || !double.IsFinite(result))
        {
            throw new FormatException($"report.{name} must be a finite number.");
        }

        return result;
    }
}
