using System.Text.Json;

namespace Wip.Tholdem.Web.Strategies;

/// <summary>One strategy artifact: the file name without extension, the file name and its object.</summary>
public sealed record StrategyEntry(string Id, string File, JsonElement Payload);

/// <summary>The GET /api/strategies response body.</summary>
public sealed record StrategyList(IReadOnlyList<StrategyEntry> Strategies, int Skipped);

/// <summary>The GET /api/strategies/{id} response body.</summary>
public sealed record StrategyDetail(string Id, JsonElement Payload);

/// <summary>The GET /api/ledger response body.</summary>
public sealed record LedgerList(IReadOnlyList<JsonElement> Entries, int Skipped);

/// <summary>
/// Read-only access to the frozen strategy/ledger artifacts:
/// <c>&lt;data&gt;/strategies/&lt;id&gt;.json</c>, <c>&lt;data&gt;/ledger.json</c> and
/// <c>&lt;data&gt;/active-champion.json</c>. Payloads are passed through verbatim.
/// </summary>
public static class StrategyCatalog
{
    public static StrategyList ListStrategies(string dataDirectory)
    {
        var strategiesRoot = Path.Combine(dataDirectory, "strategies");
        if (!Directory.Exists(strategiesRoot))
        {
            return new StrategyList([], 0);
        }

        var strategies = new List<StrategyEntry>();
        var skipped = 0;
        foreach (var path in Directory.EnumerateFiles(strategiesRoot, "*.json").Order(StringComparer.Ordinal))
        {
            if (!TryReadObject(path, out var payload))
            {
                skipped++;
                continue;
            }

            strategies.Add(new StrategyEntry(
                Path.GetFileNameWithoutExtension(path),
                Path.GetFileName(path),
                payload));
        }

        strategies.Sort(static (left, right) => string.CompareOrdinal(left.Id, right.Id));
        return new StrategyList(strategies, skipped);
    }

    public static int CountStrategies(string dataDirectory) => ListStrategies(dataDirectory).Strategies.Count;

    public static StrategyDetail? TryGetStrategy(string dataDirectory, string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var entry = ListStrategies(dataDirectory).Strategies
            .FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));
        return entry is null ? null : new StrategyDetail(entry.Id, entry.Payload);
    }

    public static LedgerList ListLedger(string dataDirectory)
    {
        var ledgerPath = Path.Combine(dataDirectory, "ledger.json");
        if (!File.Exists(ledgerPath))
        {
            return new LedgerList([], 0);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(ledgerPath));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new LedgerList([], 1);
            }

            var entries = new List<JsonElement>();
            var skipped = 0;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    entries.Add(item.Clone());
                }
                else
                {
                    skipped++;
                }
            }

            return new LedgerList(entries, skipped);
        }
        catch (Exception exception) when (IsTolerableReadFailure(exception))
        {
            return new LedgerList([], 1);
        }
    }

    public static JsonElement? TryGetChampion(string dataDirectory)
    {
        var championPath = Path.Combine(dataDirectory, "active-champion.json");
        return File.Exists(championPath) && TryReadObject(championPath, out var payload)
            ? payload
            : null;
    }

    private static bool TryReadObject(string path, out JsonElement payload)
    {
        payload = default;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            payload = document.RootElement.Clone();
            return true;
        }
        catch (Exception exception) when (IsTolerableReadFailure(exception))
        {
            return false;
        }
    }

    private static bool IsTolerableReadFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException
            or FormatException or OverflowException;
}
