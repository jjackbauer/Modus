using System.Text.Json;

namespace Wip.Tholdem.Agents.Learning.Refinement;

/// <summary>
/// One content-addressed strategy artifact in a champion's parent chain, joined to the hash-chained
/// ledger row that evaluated it as a challenger. The outcome fields are null for the root artifact
/// (nothing evaluated it as a challenger) and for a ledger row that carries no metrics (an inert
/// proposal).
/// </summary>
public sealed record StrategyLineageEntry(
    string Id,
    int Version,
    string? ParentId,
    int CreatedAtIteration,
    string? Outcome,
    string? Reason,
    double? MeanBbPer100,
    double? LowerBoundBbPer100,
    int? Seeds);

/// <summary>A strategy lineage that cannot be resolved or verified.</summary>
public sealed class StrategyLineageException : RefinementDataException
{
    public StrategyLineageException(string code, string message)
        : base(code, message)
    {
    }

    public StrategyLineageException(string code, string message, Exception innerException)
        : base(code, message, innerException)
    {
    }
}

/// <summary>
/// Resolves a champion's parent chain from the persisted content-addressed artifacts and joins each
/// link to the hash-chained ledger row that evaluated it. The result is ordered root first (ascending
/// artifact version). Every refusal is stable and returns no partial list.
/// </summary>
/// <remarks>
/// The structural walk reads each artifact's <c>id</c> and <c>parentId</c> before content verification,
/// so a forged cycle or a missing parent is refused as such even when the forged artifacts are not
/// content-addressed. After the shape is proven acyclic and within the depth cap,
/// <see cref="TrainingLoop.ReadPersistedStrategy"/> re-reads every link and verifies its content hash,
/// so no tampered artifact can contribute an entry. The ledger hash chain is verified first, and a
/// tampered ledger refuses the whole read.
/// </remarks>
public static class StrategyLineageReader
{
    /// <summary>The maximum number of artifacts one lineage may contain.</summary>
    public const int MaximumDepth = 64;

    public const string MissingParentCode = "lineage-missing-parent";

    public const string CycleCode = "lineage-cycle";

    public const string ArtifactHashMismatchCode = "lineage-artifact-hash-mismatch";

    public const string ArtifactUnreadableCode = "lineage-artifact-unreadable";

    public const string LedgerIntegrityCode = "lineage-ledger-integrity";

    public const string DepthExceededCode = "lineage-depth-exceeded";

    public static IReadOnlyList<StrategyLineageEntry> Read(string dataDirectory, string championId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(championId);

        var ledgerRows = ReadLedgerRows(dataDirectory);
        var strategyDirectory = Path.Combine(dataDirectory, "strategies");

        // Phase 1: prove the parent graph is a finite, acyclic chain rooted at a null parent. A cycle
        // is refused before any unbounded walk and before content verification.
        var chain = new List<(string Id, string? ParentId)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? current = championId;
        while (current is not null)
        {
            if (!seen.Add(current))
            {
                throw new StrategyLineageException(
                    CycleCode,
                    $"The strategy lineage from '{championId}' contains a cycle at '{current}'.");
            }

            if (chain.Count >= MaximumDepth)
            {
                throw new StrategyLineageException(
                    DepthExceededCode,
                    $"The strategy lineage from '{championId}' exceeds the {MaximumDepth}-artifact depth cap.");
            }

            var path = Path.Combine(strategyDirectory, current + ".json");
            if (!File.Exists(path))
            {
                throw new StrategyLineageException(
                    MissingParentCode,
                    $"The strategy lineage from '{championId}' is missing the artifact '{current}' at '{path}'.");
            }

            var parentId = ReadRawParentId(path, current);
            chain.Add((current, parentId));
            current = parentId;
        }

        // Phase 2: content verification. TrainingLoop.ReadPersistedStrategy refuses an artifact whose
        // id no longer matches its content hash, so a forged link never reaches the result.
        var artifacts = new List<StrategyArtifact>(chain.Count);
        for (var index = 0; index < chain.Count; index++)
        {
            var id = chain[index].Id;
            try
            {
                artifacts.Add(TrainingLoop.ReadPersistedStrategy(dataDirectory, id));
            }
            catch (InvalidOperationException exception)
            {
                throw new StrategyLineageException(ArtifactHashMismatchCode, exception.Message, exception);
            }
            catch (Exception exception) when (IsUnreadableArtifactFailure(exception))
            {
                throw new StrategyLineageException(ArtifactUnreadableCode, exception.Message, exception);
            }
        }

        // The walk is champion -> root; the contract is root first.
        artifacts.Reverse();

        var outcomeIndex = BuildOutcomeIndex(ledgerRows);
        var entries = new List<StrategyLineageEntry>(artifacts.Count);
        foreach (var artifact in artifacts)
        {
            LedgerRow? row = null;
            if (artifact.ParentId is not null)
            {
                outcomeIndex.TryGetValue((artifact.Id, artifact.ParentId), out row);
            }

            entries.Add(new StrategyLineageEntry(
                artifact.Id,
                artifact.Version,
                artifact.ParentId,
                artifact.CreatedAtIteration,
                row?.Outcome,
                row?.Reason,
                row?.MeanBbPer100,
                row?.LowerBoundBbPer100,
                row?.Seeds));
        }

        return entries;
    }

    private sealed record LedgerRow(
        int Iteration,
        string ChampionId,
        string ChallengerId,
        string? Outcome,
        string? Reason,
        double? MeanBbPer100,
        double? LowerBoundBbPer100,
        int? Seeds);

    private static Dictionary<(string ChallengerId, string ChampionId), LedgerRow> BuildOutcomeIndex(
        IReadOnlyList<LedgerRow> rows)
    {
        var index = new Dictionary<(string ChallengerId, string ChampionId), LedgerRow>();
        foreach (var row in rows)
        {
            index[(row.ChallengerId, row.ChampionId)] = row;
        }

        return index;
    }

    private static IReadOnlyList<LedgerRow> ReadLedgerRows(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, TrainingLoop.LedgerFileName);
        if (!File.Exists(path))
        {
            return Array.Empty<LedgerRow>();
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new StrategyLineageException(
                LedgerIntegrityCode,
                $"The training ledger '{path}' could not be read: {exception.Message}",
                exception);
        }

        var integrity = LedgerChain.Verify(text);
        if (!integrity.Verified)
        {
            throw new StrategyLineageException(
                LedgerIntegrityCode,
                $"The training ledger '{path}' failed its hash-chain integrity check: {integrity.Reason}.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<LedgerRow>();
        }

        var rows = new List<LedgerRow>();
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new StrategyLineageException(
                    LedgerIntegrityCode,
                    $"The training ledger '{path}' is not a JSON array.");
            }

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object
                    || !element.TryGetProperty("iteration", out var iterationElement)
                    || !iterationElement.TryGetInt32(out var iteration)
                    || !element.TryGetProperty("championId", out var championElement)
                    || championElement.ValueKind != JsonValueKind.String
                    || !element.TryGetProperty("challengerId", out var challengerElement)
                    || challengerElement.ValueKind != JsonValueKind.String)
                {
                    throw new StrategyLineageException(
                        LedgerIntegrityCode,
                        $"The training ledger '{path}' contains an entry without iteration, championId and challengerId.");
                }

                rows.Add(new LedgerRow(
                    iteration,
                    championElement.GetString()!,
                    challengerElement.GetString()!,
                    OptionalString(element, "outcome"),
                    OptionalString(element, "reason"),
                    OptionalDouble(element, "meanBbPer100"),
                    OptionalDouble(element, "lowerBoundBbPer100"),
                    OptionalInt(element, "seeds")));
            }
        }
        catch (JsonException exception)
        {
            throw new StrategyLineageException(
                LedgerIntegrityCode,
                $"The training ledger '{path}' could not be parsed: {exception.Message}",
                exception);
        }

        return rows;
    }

    private static string? ReadRawParentId(string path, string expectedId)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new StrategyLineageException(
                    ArtifactUnreadableCode,
                    $"The strategy artifact '{path}' is not a JSON object.");
            }

            if (!root.TryGetProperty("id", out var idElement)
                || idElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(idElement.GetString()))
            {
                throw new StrategyLineageException(
                    ArtifactUnreadableCode,
                    $"The strategy artifact '{path}' has no id.");
            }

            var id = idElement.GetString()!;
            if (!string.Equals(id, expectedId, StringComparison.Ordinal))
            {
                throw new StrategyLineageException(
                    ArtifactUnreadableCode,
                    $"The strategy artifact '{path}' names '{id}' but the lineage requires '{expectedId}'.");
            }

            if (!root.TryGetProperty("parentId", out var parentElement)
                || parentElement.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (parentElement.ValueKind != JsonValueKind.String)
            {
                throw new StrategyLineageException(
                    ArtifactUnreadableCode,
                    $"The strategy artifact '{path}' has a non-string parentId.");
            }

            var parentId = parentElement.GetString();
            return string.IsNullOrEmpty(parentId) ? null : parentId;
        }
        catch (JsonException exception)
        {
            throw new StrategyLineageException(
                ArtifactUnreadableCode,
                $"The strategy artifact '{path}' is not readable JSON: {exception.Message}",
                exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new StrategyLineageException(
                ArtifactUnreadableCode,
                $"The strategy artifact '{path}' could not be read: {exception.Message}",
                exception);
        }
    }

    private static bool IsUnreadableArtifactFailure(Exception exception) =>
        exception is JsonException or FormatException or KeyNotFoundException or OverflowException
            or ArgumentException;

    private static string? OptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static double? OptionalDouble(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.Number
        && property.TryGetDouble(out var value)
            ? value
            : null;

    private static int? OptionalInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.Number
        && property.TryGetInt32(out var value)
            ? value
            : null;
}
