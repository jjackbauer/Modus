using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wip.Abstractions.Policies;

public sealed record PolicyViolationPayload
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    [JsonPropertyOrder(0)]
    public required string BlockedAction { get; init; }

    [JsonPropertyOrder(1)]
    public required string BlockingPolicy { get; init; }

    [JsonPropertyOrder(2)]
    public required string NextStepGuidance { get; init; }

    [JsonPropertyOrder(3)]
    public required string Reason { get; init; }

    public string ToDeterministicPayload()
    {
        Validate(BlockedAction, BlockingPolicy, NextStepGuidance, Reason);
        return JsonSerializer.Serialize(this, SerializerOptions);
    }

    public static PolicyViolationPayload Create(
        string blockedAction,
        string blockingPolicy,
        string nextStepGuidance,
        string reason)
    {
        Validate(blockedAction, blockingPolicy, nextStepGuidance, reason);

        return new PolicyViolationPayload
        {
            BlockedAction = blockedAction,
            BlockingPolicy = blockingPolicy,
            NextStepGuidance = nextStepGuidance,
            Reason = reason
        };
    }

    public static bool TryParse(string payloadText, out PolicyViolationPayload payload)
    {
        payload = default!;

        if (string.IsNullOrWhiteSpace(payloadText))
            return false;

        try
        {
            var parsed = JsonSerializer.Deserialize<PolicyViolationPayload>(payloadText, SerializerOptions);
            if (parsed is null
                || string.IsNullOrWhiteSpace(parsed.BlockedAction)
                || string.IsNullOrWhiteSpace(parsed.BlockingPolicy)
                || string.IsNullOrWhiteSpace(parsed.NextStepGuidance)
                || string.IsNullOrWhiteSpace(parsed.Reason))
            {
                return false;
            }

            payload = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string EnsureDeterministicPayload(
        string blockedAction,
        string blockingPolicy,
        string nextStepGuidance,
        string reasonOrPayload)
    {
        if (TryParse(reasonOrPayload, out var parsed))
        {
            return Create(
                blockedAction: string.IsNullOrWhiteSpace(parsed.BlockedAction) ? blockedAction : parsed.BlockedAction,
                blockingPolicy: string.IsNullOrWhiteSpace(parsed.BlockingPolicy) ? blockingPolicy : parsed.BlockingPolicy,
                nextStepGuidance: string.IsNullOrWhiteSpace(parsed.NextStepGuidance) ? nextStepGuidance : parsed.NextStepGuidance,
                reason: string.IsNullOrWhiteSpace(parsed.Reason) ? reasonOrPayload : parsed.Reason)
                .ToDeterministicPayload();
        }

        return Create(blockedAction, blockingPolicy, nextStepGuidance, reasonOrPayload).ToDeterministicPayload();
    }

    private static void Validate(string blockedAction, string blockingPolicy, string nextStepGuidance, string reason)
    {
        if (string.IsNullOrWhiteSpace(blockedAction))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(blockedAction));

        if (string.IsNullOrWhiteSpace(blockingPolicy))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(blockingPolicy));

        if (string.IsNullOrWhiteSpace(nextStepGuidance))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(nextStepGuidance));

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(reason));
    }
}