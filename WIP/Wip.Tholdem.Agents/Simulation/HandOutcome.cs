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
    IReadOnlyList<bool> ShowdownParticipants);