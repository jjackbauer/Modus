namespace Wip.Tholdem.Agents.Baselines;

/// <summary>Thresholds for the tight-aggressive baseline.</summary>
public sealed record TightAggressiveProfile(
    double RaiseThreshold = 0.72,
    double CallThreshold = 0.45,
    double BluffFrequency = 0.12);