namespace Wip.Tholdem.Agents.Simulation;

/// <summary>Per-agent metrics for one batch simulation.</summary>
public sealed record AgentMetrics(
    string AgentName,
    int Hands,
    int Wins,
    double WinRate,
    ConfidenceInterval WinRateCi,
    long NetChips,
    double BigBlindsPer100,
    ConfidenceInterval BigBlindsPer100Ci,
    double AggressionFactor,
    double ShowdownRate);