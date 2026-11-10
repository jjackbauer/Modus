namespace Wip.Tholdem.Agents.Simulation;

/// <summary>The deterministic result of one batch simulation.</summary>
public sealed record SimulationReport(
    string SessionId,
    ulong Seed,
    int Hands,
    string ManifestHash,
    string CanonicalJson,
    IReadOnlyList<AgentMetrics> Agents);