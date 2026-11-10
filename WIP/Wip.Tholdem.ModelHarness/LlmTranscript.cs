using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Wip.Tholdem.ModelHarness;

/// <summary>
/// One canonical, append-only LLM call record. The positional member order below is the serialized
/// member order; the canonical form is UTF-8 without a BOM, uses invariant numbers and carries no
/// clock or generated identifier. The prompt and response are stored after <see cref="LlmRedaction"/>
/// so <see cref="PromptHash"/> and <see cref="ResponseHash"/> are always checkable against the stored
/// text.
/// </summary>
/// <remarks>
/// <see cref="ToCanonicalJson"/> refuses a record whose schema or outcome is outside the fixed
/// vocabulary, or whose hashes do not match its stored text.
/// </remarks>
public sealed record LlmTranscript(
    string Schema,
    int Iteration,
    string CorrelationId,
    string RequestedModel,
    string ServedModel,
    string PromptHash,
    string Prompt,
    string Response,
    string ResponseHash,
    LlmUsage Usage,
    long LatencyMilliseconds,
    string Outcome,
    string? Error)
{
    /// <summary>The only schema id this record version writes or reads.</summary>
    public const string SchemaId = "tholdem.llm-transcript.v1";

    /// <summary>
    /// Creates a record from call data: redacts secrets, hashes the post-redaction text and sets the
    /// schema id. No clock, GUID or ambient input participates.
    /// </summary>
    public static LlmTranscript Create(
        int iteration,
        string correlationId,
        string requestedModel,
        string servedModel,
        string prompt,
        string response,
        LlmUsage usage,
        long latencyMilliseconds,
        string outcome,
        string? error = null)
    {
        ArgumentNullException.ThrowIfNull(correlationId);
        ArgumentNullException.ThrowIfNull(requestedModel);
        ArgumentNullException.ThrowIfNull(servedModel);
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(outcome);

        var redactedPrompt = LlmRedaction.Redact(prompt);
        var redactedResponse = LlmRedaction.Redact(response);
        return new LlmTranscript(
            SchemaId,
            iteration,
            correlationId,
            requestedModel,
            servedModel,
            LlmTranscriptHash.Compute(redactedPrompt),
            redactedPrompt,
            redactedResponse,
            LlmTranscriptHash.Compute(redactedResponse),
            usage,
            latencyMilliseconds,
            outcome,
            error);
    }

    /// <summary>The canonical one-line JSON form: fixed members, fixed order, invariant numbers.</summary>
    public string ToCanonicalJson()
    {
        Validate();
        var builder = new StringBuilder(768);
        builder.Append("{\"schema\":");
        AppendText(builder, Schema);
        builder.Append(",\"iteration\":");
        builder.Append(Iteration.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"correlationId\":");
        AppendText(builder, CorrelationId);
        builder.Append(",\"requestedModel\":");
        AppendText(builder, RequestedModel);
        builder.Append(",\"servedModel\":");
        AppendText(builder, ServedModel);
        builder.Append(",\"promptHash\":");
        AppendText(builder, PromptHash);
        builder.Append(",\"prompt\":");
        AppendText(builder, Prompt);
        builder.Append(",\"response\":");
        AppendText(builder, Response);
        builder.Append(",\"responseHash\":");
        AppendText(builder, ResponseHash);
        builder.Append(",\"usage\":{\"promptTokens\":");
        builder.Append(Usage.PromptTokens.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"completionTokens\":");
        builder.Append(Usage.CompletionTokens.ToString(CultureInfo.InvariantCulture));
        builder.Append("},\"latencyMilliseconds\":");
        builder.Append(LatencyMilliseconds.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"outcome\":");
        AppendText(builder, Outcome);
        builder.Append(",\"error\":");
        if (Error is null)
        {
            builder.Append("null");
        }
        else
        {
            AppendText(builder, Error);
        }

        builder.Append('}');
        return builder.ToString();
    }

    private void Validate()
    {
        if (!string.Equals(Schema, SchemaId, StringComparison.Ordinal))
        {
            throw new LlmTranscriptException(
                LlmTranscriptException.InvalidSchemaCode,
                "The transcript schema must be '" + SchemaId + "'.");
        }

        if (!LlmTranscriptOutcomes.IsKnown(Outcome))
        {
            throw new LlmTranscriptException(
                LlmTranscriptException.InvalidOutcomeCode,
                "The transcript outcome '" + Outcome + "' is outside the fixed vocabulary.");
        }

        if (!LlmTranscriptHash.Matches(PromptHash, Prompt)
            || !LlmTranscriptHash.Matches(ResponseHash, Response))
        {
            throw new LlmTranscriptException(
                LlmTranscriptException.HashMismatchCode,
                "The transcript prompt or response hash does not match its stored text.");
        }
    }

    private static void AppendText(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (character < ' ')
                    {
                        builder.Append("\\u");
                        builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        builder.Append('"');
    }
}

/// <summary>Token usage recorded for one model call.</summary>
public sealed record LlmUsage(int PromptTokens, int CompletionTokens);

/// <summary>The fixed transcript outcome vocabulary.</summary>
public static class LlmTranscriptOutcomes
{
    public const string Ok = "ok";

    public const string Malformed = "malformed";

    public const string Repaired = "repaired";

    public const string Timeout = "timeout";

    public const string Http429 = "http-429";

    public const string Http5xx = "http-5xx";

    public const string Refused = "refused";

    public const string BudgetExceeded = "budget-exceeded";

    private static readonly string[] Known =
    [
        Ok,
        Malformed,
        Repaired,
        Timeout,
        Http429,
        Http5xx,
        Refused,
        BudgetExceeded,
    ];

    /// <summary>The complete fixed vocabulary, in declaration order.</summary>
    public static IReadOnlyList<string> All => Known;

    /// <summary>True when the value is one of the eight fixed outcomes.</summary>
    public static bool IsKnown(string outcome)
    {
        foreach (var candidate in Known)
        {
            if (string.Equals(candidate, outcome, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// The <c>sha256:&lt;lowercase hex&gt;</c> hashing contract shared by a transcript and its store.
/// </summary>
public static class LlmTranscriptHash
{
    public const string Prefix = "sha256:";

    /// <summary>Hashes the UTF-8 bytes of the text and returns the prefixed lowercase hex digest.</summary>
    public static string Compute(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
        return Prefix + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    /// <summary>True when the supplied hash is exactly the canonical hash of the text.</summary>
    public static bool Matches(string hash, string text) =>
        string.Equals(hash, Compute(text), StringComparison.Ordinal);
}

/// <summary>A stable-coded refusal raised while writing, reading or replaying a transcript.</summary>
public sealed class LlmTranscriptException : FormatException
{
    public const string InvalidEncodingCode = "llm-transcript-invalid-encoding";

    public const string InvalidJsonCode = "llm-transcript-invalid-json";

    public const string NonCanonicalCode = "llm-transcript-non-canonical";

    public const string HashMismatchCode = "llm-transcript-hash-mismatch";

    public const string InvalidSchemaCode = "llm-transcript-invalid-schema";

    public const string InvalidOutcomeCode = "llm-transcript-invalid-outcome";

    public LlmTranscriptException(string code, string message, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
    }

    /// <summary>The stable refusal code a caller can match without parsing the message.</summary>
    public string Code { get; }
}
