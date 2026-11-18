using System.Text.Json.Serialization;

namespace Wip.Factory.Contracts.Identifiers;

/// <summary>
/// Semantic identifiers for the factory domain (F-A.1). These follow the repository's
/// <c>readonly record struct</c> identifier rule so no contract record carries a raw string where an
/// identity is meant.
/// </summary>
/// <remarks>
/// Each value type marks its constructor with <see cref="JsonConstructorAttribute"/>. Their payload
/// property is get-only, so without the attribute a JSON round-trip through the durable store would
/// silently produce a default instance with a null payload.
/// </remarks>
public readonly record struct WorkItemId
{
    [JsonConstructor]
    public WorkItemId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct WorkAttemptId
{
    [JsonConstructor]
    public WorkAttemptId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct AgentDefinitionId
{
    [JsonConstructor]
    public AgentDefinitionId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct AgentProfileId
{
    [JsonConstructor]
    public AgentProfileId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct FactoryEventId
{
    [JsonConstructor]
    public FactoryEventId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct SubscriberId
{
    [JsonConstructor]
    public SubscriberId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// A correlation identity that ties several committed facts to one originating cause. Distinct from
/// Modus' own correlation type so the factory contract layer stays independent of the plugin kernel.
/// </summary>
public readonly record struct CorrelationId
{
    [JsonConstructor]
    public CorrelationId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct StageName
{
    [JsonConstructor]
    public StageName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct PackageId
{
    [JsonConstructor]
    public PackageId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct OutcomeId
{
    [JsonConstructor]
    public OutcomeId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct ModelId
{
    [JsonConstructor]
    public ModelId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct ScheduleId
{
    [JsonConstructor]
    public ScheduleId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct DeliveryEnvironmentId
{
    [JsonConstructor]
    public DeliveryEnvironmentId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct EvaluationPolicyId
{
    [JsonConstructor]
    public EvaluationPolicyId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// An immutable agent-profile version number. The content digest lives on the profile artifact, not
/// on the identity, so a version number alone can never be mistaken for the profile body.
/// </summary>
public readonly record struct AgentProfileVersion
{
    public const int MinimumValue = 1;

    [JsonConstructor]
    public AgentProfileVersion(int value)
    {
        if (value < MinimumValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"An agent profile version must be at least {MinimumValue}.");
        }

        Value = value;
    }

    public int Value { get; }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
