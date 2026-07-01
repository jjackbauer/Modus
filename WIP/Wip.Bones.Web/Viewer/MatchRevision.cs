namespace Wip.Bones.Web.Viewer;

public readonly record struct MatchRevision
{
    public MatchRevision(long value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Revision must be non-negative.");

        Value = value;
    }

    public long Value { get; }

    public override string ToString() => Value.ToString();
}