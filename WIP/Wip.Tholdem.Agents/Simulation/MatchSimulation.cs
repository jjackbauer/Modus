namespace Wip.Tholdem.Agents.Simulation;

/// <summary>
/// The detailed result of one batch simulation: the aggregate report, the per-hand outcomes in hand
/// order and the per-hand seeds actually dealt. The seeds let a paired evaluator prove that both
/// policies saw the same deals.
/// </summary>
public sealed record MatchSimulation(
    SimulationReport Report,
    IReadOnlyList<HandOutcome> Outcomes,
    IReadOnlyList<ulong> HandSeeds);
