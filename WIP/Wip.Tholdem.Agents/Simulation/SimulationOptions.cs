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
        IReadOnlyList<long>? StartingStacks = null,
        int actionCapPerHand = 1_000_000)
    {
        if (Hands <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Hands), Hands, "A simulation must run at least one hand.");
        }

        if (actionCapPerHand < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(actionCapPerHand),
                actionCapPerHand,
                "The per-hand action cap must be at least one.");
        }

        this.Seed = Seed;
        this.Hands = Hands;
        this.StartingStack = StartingStack;
        this.SmallBlind = SmallBlind;
        this.BigBlind = BigBlind;
        this.AlternateButton = AlternateButton;
        this.HistoryDirectory = HistoryDirectory;
        this.StartingStacks = StartingStacks?.ToArray();
        this.ActionCapPerHand = actionCapPerHand;
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

    /// <summary>
    /// The maximum number of applied actions one hand may take. The default of one million leaves
    /// every existing caller byte-identical because no real hand approaches it; a smaller cap turns
    /// a runaway agent into a <see cref="HandActionCapExceededException"/> instead of a hang.
    /// </summary>
    public int ActionCapPerHand { get; init; }
}
