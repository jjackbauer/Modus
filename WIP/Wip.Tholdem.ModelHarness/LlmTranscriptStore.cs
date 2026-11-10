using System.Text;
using System.Text.Json;

namespace Wip.Tholdem.ModelHarness;

/// <summary>
/// Appends canonical transcript lines to <c>&lt;directory&gt;/llm-transcripts.jsonl</c> and reads them
/// back in append order. <see cref="ReadAll"/> validates every line before returning anything, so a
/// non-canonical line or a hash mismatch refuses the whole read with a stable
/// <see cref="LlmTranscriptException.Code"/> instead of yielding a partial list.
/// </summary>
public sealed class LlmTranscriptStore
{
    /// <summary>The single append-only file name inside the store directory.</summary>
    public const string FileName = "llm-transcripts.jsonl";

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _directory;

    public LlmTranscriptStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
    }

    /// <summary>The absolute store directory.</summary>
    public string DirectoryPath => _directory;

    /// <summary>The absolute transcript file path.</summary>
    public string FilePath => Path.Combine(_directory, FileName);

    /// <summary>Appends exactly one canonical JSON line for the record.</summary>
    public async ValueTask AppendAsync(LlmTranscript transcript, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        var line = transcript.ToCanonicalJson();
        Directory.CreateDirectory(_directory);
        await File
            .AppendAllTextAsync(FilePath, line + "\n", Utf8, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Returns every record in append order, or refuses the whole file. A UTF-8 BOM, invalid UTF-8,
    /// an empty interior line, a non-canonical line, a wrong schema/outcome or a prompt/response hash
    /// mismatch is refused before the first record is returned.
    /// </summary>
    public IReadOnlyList<LlmTranscript> ReadAll()
    {
        if (!File.Exists(FilePath))
        {
            return Array.Empty<LlmTranscript>();
        }

        var bytes = File.ReadAllBytes(FilePath);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            throw new LlmTranscriptException(
                LlmTranscriptException.InvalidEncodingCode,
                "The transcript file must be UTF-8 without a byte-order mark.");
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new LlmTranscriptException(
                LlmTranscriptException.InvalidEncodingCode,
                "The transcript file is not valid UTF-8.",
                exception);
        }

        var lines = text.Split('\n');
        var records = new List<LlmTranscript>(lines.Length);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Length == 0)
            {
                if (index == lines.Length - 1)
                {
                    // The single trailing newline belongs to the last appended record.
                    continue;
                }

                throw new LlmTranscriptException(
                    LlmTranscriptException.NonCanonicalCode,
                    "Transcript line " + (index + 1) + " is empty.");
            }

            records.Add(ParseLine(line, index + 1));
        }

        return records;
    }

    private static LlmTranscript ParseLine(string line, int lineNumber)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException exception)
        {
            throw new LlmTranscriptException(
                LlmTranscriptException.InvalidJsonCode,
                "Transcript line " + lineNumber + " is not JSON: " + exception.Message,
                exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Invalid(lineNumber, "must be a JSON object");
            }

            var usageElement = RequiredObject(root, "usage", lineNumber);
            var record = new LlmTranscript(
                RequiredString(root, "schema", lineNumber),
                RequiredInt32(root, "iteration", lineNumber),
                RequiredString(root, "correlationId", lineNumber),
                RequiredString(root, "requestedModel", lineNumber),
                RequiredString(root, "servedModel", lineNumber),
                RequiredString(root, "promptHash", lineNumber),
                RequiredString(root, "prompt", lineNumber),
                RequiredString(root, "response", lineNumber),
                RequiredString(root, "responseHash", lineNumber),
                new LlmUsage(
                    RequiredInt32(usageElement, "promptTokens", lineNumber),
                    RequiredInt32(usageElement, "completionTokens", lineNumber)),
                RequiredInt64(root, "latencyMilliseconds", lineNumber),
                RequiredString(root, "outcome", lineNumber),
                RequiredNullableString(root, "error", lineNumber));

            if (!LlmTranscriptHash.Matches(record.PromptHash, record.Prompt)
                || !LlmTranscriptHash.Matches(record.ResponseHash, record.Response))
            {
                throw new LlmTranscriptException(
                    LlmTranscriptException.HashMismatchCode,
                    "Transcript line " + lineNumber + " has a prompt or response hash mismatch.");
            }

            var canonical = record.ToCanonicalJson();
            if (!string.Equals(canonical, line, StringComparison.Ordinal))
            {
                throw new LlmTranscriptException(
                    LlmTranscriptException.NonCanonicalCode,
                    "Transcript line " + lineNumber + " is not the canonical form of its record.");
            }

            return record;
        }
    }

    private static string RequiredString(JsonElement parent, string name, int lineNumber)
    {
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
        {
            throw Invalid(lineNumber, "member '" + name + "' must be a string");
        }

        return element.GetString()!;
    }

    private static string? RequiredNullableString(JsonElement parent, string name, int lineNumber)
    {
        if (!parent.TryGetProperty(name, out var element))
        {
            throw Invalid(lineNumber, "member '" + name + "' is missing");
        }

        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            _ => throw Invalid(lineNumber, "member '" + name + "' must be a string or null"),
        };
    }

    private static int RequiredInt32(JsonElement parent, string name, int lineNumber)
    {
        if (!parent.TryGetProperty(name, out var element)
            || element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt32(out var value))
        {
            throw Invalid(lineNumber, "member '" + name + "' must be an integer");
        }

        return value;
    }

    private static long RequiredInt64(JsonElement parent, string name, int lineNumber)
    {
        if (!parent.TryGetProperty(name, out var element)
            || element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt64(out var value))
        {
            throw Invalid(lineNumber, "member '" + name + "' must be an integer");
        }

        return value;
    }

    private static JsonElement RequiredObject(JsonElement parent, string name, int lineNumber)
    {
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid(lineNumber, "member '" + name + "' must be an object");
        }

        return element;
    }

    private static LlmTranscriptException Invalid(int lineNumber, string detail) =>
        new(
            LlmTranscriptException.InvalidJsonCode,
            "Transcript line " + lineNumber + " " + detail + ".");
}
