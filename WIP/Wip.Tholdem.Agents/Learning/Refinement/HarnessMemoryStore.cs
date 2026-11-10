using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wip.Tholdem.Agents.Simulation;

namespace Wip.Tholdem.Agents.Learning.Refinement;

/// <summary>The fixed kind vocabulary of a harness-memory entry.</summary>
public static class HarnessMemoryKinds
{
    public const string Pattern = "pattern";

    public const string FailedApproach = "failed-approach";

    public const string DeadEnd = "dead-end";
}

/// <summary>
/// One canonical, data-only memory entry. The iteration indices are the only temporal fields: there is
/// no clock, no GUID, no seed and no card identity in it.
/// </summary>
public sealed record HarnessMemoryEntry(string Kind, string Key, int Count, int FirstIteration, int LastIteration);

/// <summary>
/// An immutable, verified view of the harness-memory file. <see cref="Entries"/> is always in the
/// canonical order (ordinal key order), and <see cref="ContentHash"/> covers exactly the canonical
/// entries array.
/// </summary>
public sealed record HarnessMemorySnapshot(IReadOnlyList<HarnessMemoryEntry> Entries)
{
    public static HarnessMemorySnapshot Empty { get; } = new(Array.Empty<HarnessMemoryEntry>());

    public string ContentHash => HarnessMemoryStore.ComputeContentHash(Entries);

    public string ToCanonicalJson() => HarnessMemoryStore.BuildCanonicalJson(Entries);
}

/// <summary>
/// The base refusal for a persisted refinement artifact that cannot be trusted. <see cref="Code"/> is
/// a stable machine-readable reason; it never contains a path, a clock or a random value.
/// </summary>
public class RefinementDataException : Exception
{
    public RefinementDataException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public RefinementDataException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>A canonical harness-memory file that failed validation or hash verification.</summary>
public sealed class HarnessMemoryException : RefinementDataException
{
    public HarnessMemoryException(string code, string message)
        : base(code, message)
    {
    }

    public HarnessMemoryException(string code, string message, Exception innerException)
        : base(code, message, innerException)
    {
    }
}

/// <summary>
/// The canonical harness-memory store. Persists <c>dataDirectory/harness/memory.json</c> as canonical
/// UTF-8 without BOM with schema <see cref="Schema"/>:
/// <c>{"schema":"tholdem.harness-memory.v1","contentHash":"&lt;sha256&gt;","entries":[...]}</c>, where
/// <see cref="HarnessMemorySnapshot.ContentHash"/> is the SHA-256 of the canonical entries array.
/// </summary>
/// <remarks>
/// The store is deliberately advisory and write-oriented: the current measured refinement policy never
/// receives a snapshot, so recording an entry cannot change a proposal. Every mutation loads and
/// verifies the previous file first, so a tampered file refuses both reads and writes. Entries are
/// keyed by an approach key and bounded to <see cref="MaximumEntries"/> with a deterministic
/// least-recently-seen eviction (lowest <see cref="HarnessMemoryEntry.LastIteration"/>, then ordinal
/// key). The third record of the same key becomes a dead end, which later records never demote.
/// </remarks>
public sealed class HarnessMemoryStore
{
    public const string Schema = "tholdem.harness-memory.v1";

    public const int MaximumEntries = 64;

    public const int DeadEndThreshold = 3;

    public const int MaximumKeyLength = 128;

    public const string DirectoryName = "harness";

    public const string FileName = "memory.json";

    public const string ContentHashMismatchCode = "memory-content-hash-mismatch";

    public const string SchemaMismatchCode = "memory-schema-mismatch";

    public const string MalformedCode = "memory-malformed";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _memoryDirectory;

    public HarnessMemoryStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _memoryDirectory = Path.Combine(dataDirectory, DirectoryName);
        MemoryPath = Path.Combine(_memoryDirectory, FileName);
    }

    /// <summary>The canonical file this store reads and rewrites atomically.</summary>
    public string MemoryPath { get; }

    /// <summary>
    /// Reads and verifies the memory file. A missing file is an empty snapshot; a file whose schema,
    /// shape or content hash does not match is refused with a stable <see cref="HarnessMemoryException.Code"/>.
    /// </summary>
    public HarnessMemorySnapshot Load()
    {
        if (!File.Exists(MemoryPath))
        {
            return HarnessMemorySnapshot.Empty;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(MemoryPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new HarnessMemoryException(
                MalformedCode,
                $"The harness memory '{MemoryPath}' could not be read: {exception.Message}",
                exception);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes);
        }
        catch (JsonException exception)
        {
            throw new HarnessMemoryException(
                MalformedCode,
                $"The harness memory '{MemoryPath}' is not readable JSON: {exception.Message}",
                exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Malformed("is not a JSON object");
            }

            if (!root.TryGetProperty("schema", out var schemaElement)
                || schemaElement.ValueKind != JsonValueKind.String
                || !string.Equals(schemaElement.GetString(), Schema, StringComparison.Ordinal))
            {
                throw new HarnessMemoryException(
                    SchemaMismatchCode,
                    $"The harness memory '{MemoryPath}' does not declare schema '{Schema}'.");
            }

            if (!root.TryGetProperty("contentHash", out var hashElement)
                || hashElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(hashElement.GetString()))
            {
                throw new HarnessMemoryException(
                    MalformedCode,
                    $"The harness memory '{MemoryPath}' has no contentHash.");
            }

            if (!root.TryGetProperty("entries", out var entriesElement)
                || entriesElement.ValueKind != JsonValueKind.Array)
            {
                throw new HarnessMemoryException(
                    MalformedCode,
                    $"The harness memory '{MemoryPath}' has no entries array.");
            }

            var storedHash = hashElement.GetString()!;
            var entries = new List<HarnessMemoryEntry>();
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in entriesElement.EnumerateArray())
            {
                var entry = ParseEntry(element);
                if (!seenKeys.Add(entry.Key))
                {
                    throw new HarnessMemoryException(
                        MalformedCode,
                        $"The harness memory '{MemoryPath}' contains the key '{entry.Key}' more than once.");
                }

                entries.Add(entry);
            }

            entries.Sort(static (left, right) => string.CompareOrdinal(left.Key, right.Key));
            var computedHash = ComputeContentHash(entries);
            if (!string.Equals(computedHash, storedHash, StringComparison.Ordinal))
            {
                throw new HarnessMemoryException(
                    ContentHashMismatchCode,
                    $"The harness memory '{MemoryPath}' content hash does not match its entries "
                    + $"(stored '{storedHash}', computed '{computedHash}').");
            }

            return new HarnessMemorySnapshot(entries);
        }
    }

    /// <summary>Records one more rejection of an approach and returns the new verified snapshot.</summary>
    public HarnessMemorySnapshot RecordRejection(string key, int iteration) =>
        Record(key, iteration, rejection: true);

    /// <summary>Records one more acceptance of an approach and returns the new verified snapshot.</summary>
    public HarnessMemorySnapshot RecordPattern(string key, int iteration) =>
        Record(key, iteration, rejection: false);

    /// <summary>The canonical entries array the content hash is computed over.</summary>
    public static string BuildEntriesJson(IReadOnlyList<HarnessMemoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var builder = new StringBuilder(2 + (entries.Count * 128));
        builder.Append('[');
        for (var index = 0; index < entries.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            var entry = entries[index];
            builder.Append("{\"kind\":");
            CanonicalJsonWriter.Text(builder, entry.Kind);
            builder.Append(",\"key\":");
            CanonicalJsonWriter.Text(builder, entry.Key);
            builder.Append(",\"count\":");
            CanonicalJsonWriter.Number(builder, entry.Count);
            builder.Append(",\"firstIteration\":");
            CanonicalJsonWriter.Number(builder, entry.FirstIteration);
            builder.Append(",\"lastIteration\":");
            CanonicalJsonWriter.Number(builder, entry.LastIteration);
            builder.Append('}');
        }

        builder.Append(']');
        return builder.ToString();
    }

    /// <summary>SHA-256 over the canonical entries array, lowercase hexadecimal.</summary>
    public static string ComputeContentHash(IReadOnlyList<HarnessMemoryEntry> entries) =>
        StrategyArtifact.ComputeHash(BuildEntriesJson(entries));

    /// <summary>The full canonical file text: schema, content hash and entries in that order.</summary>
    public static string BuildCanonicalJson(IReadOnlyList<HarnessMemoryEntry> entries)
    {
        var entriesJson = BuildEntriesJson(entries);
        var builder = new StringBuilder(entriesJson.Length + 128);
        builder.Append("{\"schema\":");
        CanonicalJsonWriter.Text(builder, Schema);
        builder.Append(",\"contentHash\":");
        CanonicalJsonWriter.Text(builder, ComputeContentHash(entries));
        builder.Append(",\"entries\":");
        builder.Append(entriesJson);
        builder.Append('}');
        return builder.ToString();
    }

    private HarnessMemorySnapshot Record(string key, int iteration, bool rejection)
    {
        ValidateKey(key);
        if (iteration < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(iteration),
                iteration,
                "A harness-memory iteration must not be negative.");
        }

        var entries = new List<HarnessMemoryEntry>(Load().Entries);
        var index = entries.FindIndex(entry => string.Equals(entry.Key, key, StringComparison.Ordinal));
        if (index < 0)
        {
            entries.Add(new HarnessMemoryEntry(
                rejection ? HarnessMemoryKinds.FailedApproach : HarnessMemoryKinds.Pattern,
                key,
                1,
                iteration,
                iteration));
        }
        else
        {
            var existing = entries[index];
            var count = existing.Count + 1;
            var kind = count >= DeadEndThreshold
                ? HarnessMemoryKinds.DeadEnd
                : rejection
                    ? HarnessMemoryKinds.FailedApproach
                    : HarnessMemoryKinds.Pattern;
            entries[index] = existing with { Kind = kind, Count = count, LastIteration = iteration };
        }

        while (entries.Count > MaximumEntries)
        {
            var victim = entries
                .OrderBy(static entry => entry.LastIteration)
                .ThenBy(static entry => entry.Key, StringComparer.Ordinal)
                .First();
            entries.Remove(victim);
        }

        entries.Sort(static (left, right) => string.CompareOrdinal(left.Key, right.Key));
        var snapshot = new HarnessMemorySnapshot(entries);
        WriteAtomic(snapshot);
        return snapshot;
    }

    private void WriteAtomic(HarnessMemorySnapshot snapshot)
    {
        Directory.CreateDirectory(_memoryDirectory);
        var tempPath = MemoryPath + ".tmp";
        File.WriteAllText(tempPath, snapshot.ToCanonicalJson(), Utf8);
        File.Move(tempPath, MemoryPath, overwrite: true);
    }

    private HarnessMemoryEntry ParseEntry(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Malformed("contains a non-object entry");
        }

        var kind = RequiredString(element, "kind");
        if (kind is not (HarnessMemoryKinds.Pattern or HarnessMemoryKinds.FailedApproach or HarnessMemoryKinds.DeadEnd))
        {
            throw Malformed($"contains the unknown kind '{kind}'");
        }

        var key = RequiredString(element, "key");
        ValidateKey(key);
        var count = RequiredInt(element, "count");
        if (count < 1)
        {
            throw Malformed($"contains the key '{key}' with a non-positive count");
        }

        var firstIteration = RequiredInt(element, "firstIteration");
        var lastIteration = RequiredInt(element, "lastIteration");
        if (firstIteration < 0 || lastIteration < firstIteration)
        {
            throw Malformed($"contains the key '{key}' with invalid iteration bounds");
        }

        return new HarnessMemoryEntry(kind, key, count, firstIteration, lastIteration);
    }

    private string RequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrEmpty(property.GetString()))
        {
            throw Malformed($"contains an entry without a non-empty {propertyName}");
        }

        return property.GetString()!;
    }

    private int RequiredInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt32(out var value))
        {
            throw Malformed($"contains an entry without an integer {propertyName}");
        }

        return value;
    }

    private HarnessMemoryException Malformed(string detail) =>
        new(MalformedCode, $"The harness memory '{MemoryPath}' {detail}.");

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("A harness-memory key must not be empty.", nameof(key));
        }

        if (key.Length > MaximumKeyLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(key),
                key.Length,
                $"A harness-memory key must not exceed {MaximumKeyLength} characters.");
        }
    }
}
