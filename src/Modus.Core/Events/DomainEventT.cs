namespace Modus.Core.Events;

public sealed record DomainEvent<TPayload>
{
    public DomainEvent(string Name, TPayload Payload)
    {
        ArgumentNullException.ThrowIfNull(Name);
        if (default(TPayload) is null)
            ArgumentNullException.ThrowIfNull(Payload);
        this.Name = Name;
        this.Payload = Payload;
    }

    public string Name { get; }
    public TPayload Payload { get; }
}
