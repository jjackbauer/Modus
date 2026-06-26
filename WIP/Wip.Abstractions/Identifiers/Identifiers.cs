namespace Wip.Abstractions.Identifiers;

public readonly record struct CapabilityId
{
    public CapabilityId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct ArtifactId
{
    public ArtifactId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct SessionId
{
    public SessionId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct WorkflowId
{
    public WorkflowId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct PolicyId
{
    public PolicyId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
