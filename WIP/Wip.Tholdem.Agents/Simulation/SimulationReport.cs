namespace Wip.Tholdem.Agents.Simulation;

/// <summary>
/// One seat's measured voluntary-preflop participation over a batch. A hand in which the seat never
/// acted preflop is still a denominator hand; only a seat with no observed hands has missing VPIP.
/// </summary>
public sealed record VpipSummary(int Hands, int VoluntaryPreflopHands)
{
    /// <summary>The measured rate, or <see langword="null"/> when no hand was observed.</summary>
    public double? Vpip => Hands == 0 ? null : VoluntaryPreflopHands / (double)Hands;
}

/// <summary>The deterministic result of one batch simulation.</summary>
public sealed record SimulationReport(
    string SessionId,
    ulong Seed,
    int Hands,
    string ManifestHash,
    string CanonicalJson,
    IReadOnlyList<AgentMetrics> Agents)
{
    /// <summary>
    /// One measured summary per seat, in seat order. Empty for a hand-built report and for any
    /// report produced before VPIP was measured; it is deliberately not part of the canonical JSON.
    /// </summary>
    public IReadOnlyList<VpipSummary> Vpip { get; init; } = Array.Empty<VpipSummary>();
}