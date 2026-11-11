namespace Wip.Tholdem.Agents.Simulation;

/// <summary>
/// One supervised hand. Every per-seat list is indexed by seat (0 or 1).
/// </summary>
public sealed record HandOutcome(
    int HandNumber,
    IReadOnlyList<long> FinalStacks,
    IReadOnlyList<int> Winners,
    bool Showdown,
    IReadOnlyList<int> AggressiveActions,
    IReadOnlyList<int> Calls,
    IReadOnlyList<bool> ShowdownParticipants)
{
    /// <summary>
    /// One flag per seat: whether the seat voluntarily put chips in preflop (call, bet, raise or
    /// all-in). Posted blinds and checks are not voluntary participation.
    /// </summary>
    public IReadOnlyList<bool> VoluntaryPreflop { get; init; } = Array.Empty<bool>();
}
