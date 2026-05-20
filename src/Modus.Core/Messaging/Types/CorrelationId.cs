namespace Modus.Core.Messaging;

public readonly record struct CorrelationId
{
    public CorrelationId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
