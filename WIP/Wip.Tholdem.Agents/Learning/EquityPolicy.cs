namespace Wip.Tholdem.Agents.Learning;

/// <summary>
/// The data-only configuration of <see cref="EquityPolicyAgent"/>: the Monte Carlo sample count, two
/// equity edges above the opponent-count baseline, a seeded bluff frequency, the pot fraction used
/// to size bets and raises and the extra margin facing a preflop raise. Expected pot share scales
/// like 1/(opponents+1), so a fixed absolute threshold would be unreachable at a full table; both
/// decision thresholds are therefore relative to the baseline 1/(liveOpponents+1). The record itself
/// is never mutated; the agent clamps a copy to the documented domains at construction. The
/// uncertain call outcome is always resolved as a fold and therefore needs no field.
/// </summary>
/// <param name="EquitySamples">Monte Carlo samples per decision; the agent clamps to [50, 5000].</param>
/// <param name="ValueEdge">Edge above the 1/(opponents+1) baseline for a no-bet value bet, in [0, 1].</param>
/// <param name="RaiseEdge">Edge above the baseline required by the expected share and the interval lower bound to raise, in [0, 1].</param>
/// <param name="BluffFrequency">Seeded bluff probability when there is nothing to call, in [0, 1].</param>
/// <param name="ValueBetPotFraction">Pot fraction for bet and raise sizing, in (0, 2].</param>
/// <param name="PreflopRaiseMargin">
/// Extra break-even equity required facing one preflop raise, doubled facing a three-bet and zero
/// otherwise, in [0, 1].
/// </param>
public sealed record EquityPolicy(
    int EquitySamples = 400,
    double ValueEdge = 0.10,
    double RaiseEdge = 0.18,
    double BluffFrequency = 0.08,
    double ValueBetPotFraction = 0.65,
    double PreflopRaiseMargin = 0.03);