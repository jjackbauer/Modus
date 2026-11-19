using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Wip.Factory.Host.Factory;

/// <summary>
/// Raised when a journal contains an unreadable or digest-mismatched record that is not the trailing record.
/// A corrupted interior record is a real integrity failure and must never be skipped silently (v0.4 FAC-P1-04).
/// </summary>
public sealed class JournalCorruptionException : InvalidOperationException
{
    public JournalCorruptionException(string path, int lineNumber, string detail)
        : base($"journal-corrupt:{System.IO.Path.GetFileName(path)}:{lineNumber}:{detail}")
    {
        JournalPath = path;
        LineNumber = lineNumber;
    }

    public string JournalPath { get; }

    public int LineNumber { get; }
}

/// <summary>
/// Append-only journal whose every record carries its own content digest. A torn <b>trailing</b> record is the only
/// record that may be discarded on replay; a malformed or digest-mismatched interior record fails closed. Appends are
/// written through to disk so a caller can commit a higher-level fact only after its evidence is durable.
/// </summary>
public sealed class DurableJournal<T>
{
    private static readonly JsonSerializerOptions DefaultSerializerOptions = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;
    private readonly JsonSerializerOptions _options;
    private int _tornRecordCount;

    public DurableJournal(string path, JsonSerializerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        _options = options ?? DefaultSerializerOptions;
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public string Path => _path;

    /// <summary>Trailing records discarded as torn during the most recent read.</summary>
    public int TornRecordCount => _tornRecordCount;

    /// <summary>Reads every committed record. Throws <see cref="JournalCorruptionException"/> for interior damage.</summary>
    public IReadOnlyList<T> ReadAll()
    {
        _tornRecordCount = 0;
        if (!File.Exists(_path))
        {
            return [];
        }

        var content = File.ReadAllText(_path);
        var lines = content.Split('\n');
        var lastNonEmpty = lines.Length - 1;
        while (lastNonEmpty >= 0 && string.IsNullOrWhiteSpace(lines[lastNonEmpty]))
        {
            lastNonEmpty--;
        }

        var records = new List<T>();
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (TryReadRecord(line, out var record, out var detail))
            {
                records.Add(record!);
                continue;
            }

            if (index == lastNonEmpty)
            {
                // Only a trailing record can be a torn write: it was never a committed fact.
                _tornRecordCount++;
                continue;
            }

            throw new JournalCorruptionException(_path, index + 1, detail);
        }

        return records;
    }

    /// <summary>Appends one record and flushes it to disk before returning.</summary>
    public void Append(T record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var payload = JsonSerializer.Serialize(record, _options);
        var checksum = Digest(payload);
        var line = "{\"checksum\":\"" + checksum + "\",\"payload\":" + payload + "}\n";
        var bytes = Encoding.UTF8.GetBytes(line);

        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(
            _path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.WriteThrough);

        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    public static string Digest(string canonical) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();

    private bool TryReadRecord(string line, out T? record, out string detail)
    {
        record = default;
        detail = string.Empty;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException exception)
        {
            detail = "json:" + exception.Message;
            return false;
        }

        using (document)
        {
            var root = document.RootElement;

            // New format: a digest envelope around the exact payload bytes.
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("checksum", out var checksum)
                && root.TryGetProperty("payload", out var payload))
            {
                var raw = payload.GetRawText();
                var recorded = checksum.ValueKind == JsonValueKind.String ? checksum.GetString() : null;
                if (!string.Equals(recorded, Digest(raw), StringComparison.Ordinal))
                {
                    detail = "checksum-mismatch";
                    return false;
                }

                try
                {
                    record = JsonSerializer.Deserialize<T>(raw, _options);
                }
                catch (JsonException exception)
                {
                    detail = "payload-json:" + exception.Message;
                    return false;
                }

                if (record is null)
                {
                    detail = "payload-null";
                    return false;
                }

                return true;
            }

            // Legacy format (pre-v0.4 journals): a bare record with no envelope. Still parsed, still interior-checked.
            try
            {
                record = JsonSerializer.Deserialize<T>(line, _options);
            }
            catch (JsonException exception)
            {
                detail = "legacy-json:" + exception.Message;
                return false;
            }

            if (record is null)
            {
                detail = "legacy-null";
                return false;
            }

            return true;
        }
    }
}

