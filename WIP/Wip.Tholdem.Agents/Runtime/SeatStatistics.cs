namespace Wip.Tholdem.Agents.Runtime;

/// <summary>
/// Aggregate session statistics for one seat. <see cref="Vpip"/> and <see cref="Pfr"/> are per-hand
/// fractions over <see cref="Hands"/>; <see cref="AggressionFactor"/> is aggressive actions divided
/// by <c>max(1, calls)</c>; <see cref="ShowdownRate"/> is the fraction of hands that reached showdown.
/// </summary>
public sealed record SeatStatistics(
    int Hands,
    double Vpip,
    double Pfr,
    double AggressionFactor,
    double ShowdownRate);
