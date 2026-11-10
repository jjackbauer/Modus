using System.Text.Json;
using Wip.Tholdem.Agents.Learning;

namespace Wip.Tholdem.Host;

/// <summary>
/// Reads strategy artifacts and the champion pointer as plain data. The CLI is not a friend of the
/// agents assembly, so it re-parses the public artifact schema instead of using a private reader.
/// </summary>
internal static class StrategyArtifactStore
{
    public const string ChampionFileName = "active-champion.json";

    public static StrategyArtifact Load(string dataDirectory, string strategyId)
    {
        var safeId = CliPaths.SafeStrategyId(strategyId);
        var path = Path.Combine(dataDirectory, "strategies", safeId + ".json");
        if (!File.Exists(path))
        {
            throw new CliArtifactException($"strategy artifact '{path}' does not exist.");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("the strategy artifact must be a JSON object.");
            }

            var id = root.GetProperty("id").GetString();
            if (!string.Equals(id, safeId, StringComparison.Ordinal))
            {
                throw new FormatException(
                    $"the artifact names strategy '{id}' but the file is '{safeId}.json'.");
            }

            var version = root.GetProperty("version").GetInt32();
            var parentId = root.TryGetProperty("parentId", out var parent) && parent.ValueKind == JsonValueKind.String
                ? parent.GetString()
                : null;
            var createdAtIteration = root.GetProperty("createdAtIteration").GetInt32();
            var parameters = root.GetProperty("parameters");
            var genome = new StrategyGenome(
                parameters.GetProperty("raiseThreshold").GetDouble(),
                parameters.GetProperty("callThreshold").GetDouble(),
                parameters.GetProperty("bluffFrequency").GetDouble(),
                parameters.GetProperty("sizingMultiplier").GetDouble(),
                root.GetProperty("promptText").GetString() ?? new StrategyGenome().PromptText).Clamp();

            if (version < 1 || createdAtIteration < 0)
            {
                throw new FormatException("the strategy version and creation iteration must not be negative.");
            }

            return new StrategyArtifact(safeId, version, parentId, createdAtIteration, genome);
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            throw new CliArtifactException(
                $"strategy artifact '{path}' is not a valid strategy artifact: {exception.Message}",
                exception);
        }
    }

    public static string ReadChampionStrategyId(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, ChampionFileName);
        if (!File.Exists(path))
        {
            throw new CliArtifactException(
                $"champion pointer '{path}' does not exist; pass --incumbent explicitly.");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("strategyId", out var strategyId)
                || strategyId.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(strategyId.GetString()))
            {
                throw new FormatException("the pointer must be an object with a non-empty string strategyId.");
            }

            return CliPaths.SafeStrategyId(strategyId.GetString()!);
        }
        catch (Exception exception) when (exception is not CliArtifactException && IsReadFailure(exception))
        {
            throw new CliArtifactException(
                $"champion pointer '{path}' is not valid: {exception.Message}",
                exception);
        }
    }

    public static string WriteChampionPointer(string dataDirectory, string strategyId)
    {
        var safeId = CliPaths.SafeStrategyId(strategyId);
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, ChampionFileName);
        var json = "{\"strategyId\":" + CliJson.Text(safeId) + ",\"iteration\":0}";
        File.WriteAllText(path, json, CliJson.Utf8);
        return path;
    }

    private static bool IsReadFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or JsonException or FormatException
            or KeyNotFoundException or InvalidOperationException or ArgumentException;
}
