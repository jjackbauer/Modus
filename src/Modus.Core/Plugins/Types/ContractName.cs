namespace Modus.Core.Plugins;

public readonly record struct ContractName
{
    public ContractName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
