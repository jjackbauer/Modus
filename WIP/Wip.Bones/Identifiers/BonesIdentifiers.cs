namespace Wip.Bones.Identifiers;

public readonly record struct BonesTileId
{
    public BonesTileId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct BonesMoveId
{
    public BonesMoveId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct BonesGameId
{
    public BonesGameId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct BonesStrategyId
{
    public BonesStrategyId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct BonesPlayerId
{
    public const int MinSeat = 1;
    public const int MaxSeat = 4;

    public BonesPlayerId(int seat)
    {
        if (seat < MinSeat || seat > MaxSeat)
            throw new ArgumentOutOfRangeException(nameof(seat), seat, $"Seat must be between {MinSeat} and {MaxSeat}.");

        Seat = seat;
    }

    public int Seat { get; }

    public override string ToString() => Seat.ToString();
}