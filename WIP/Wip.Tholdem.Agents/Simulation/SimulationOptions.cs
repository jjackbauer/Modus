namespace Wip.Tholdem.Agents.Simulation;

/// <summary>Options for one reproducible batch match.</summary>
public sealed record SimulationOptions
{
    public SimulationOptions(
        ulong Seed,
        int Hands,
        long StartingStack = 1000,
        long SmallBlind = 5,
        long BigBlind = 10,
        bool AlternateButton = true,
        string? HistoryDirectory = null,
        IReadOnlyList<long>? StartingStacks = null)
    {
        if (Hands <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Hands), Hands, "A simulation must run at least one hand.");
        }

        this.Seed = Seed;
        this.Hands = Hands;
        this.StartingStack = StartingStack;
        this.SmallBlind = SmallBlind;
        this.BigBlind = BigBlind;
        this.AlternateButton = AlternateButton;
        this.HistoryDirectory = HistoryDirectory;
        this.StartingStacks = StartingStacks?.ToArray();
    }

    public ulong Seed { get; init; }

    public int Hands { get; init; }

    public long StartingStack { get; init; }

    public long SmallBlind { get; init; }

    public long BigBlind { get; init; }

    public bool AlternateButton { get; init; }

    public string? HistoryDirectory { get; init; }

    /// <summary>
    /// Per-seat starting stacks, indexed by seat. When <see langword="null"/> every seat starts with
    /// <see cref="StartingStack"/>. When supplied it must hold exactly one value per seat, and every
    /// value must cover the big blind; the engine enforces both rules when the first hand starts.
    /// </summary>
    public IReadOnlyList<long>? StartingStacks { get; init; }
}