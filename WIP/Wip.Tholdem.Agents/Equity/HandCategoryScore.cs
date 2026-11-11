using Wip.Tholdem.Agents.Baselines;
using Wip.Tholdem.Cards;

namespace Wip.Tholdem.Agents.Equity;

/// <summary>
/// A relative hand-category rank score in [0, 1] derived from the deterministic hand-strength
/// heuristic. It ranks how strong a hand looks; it is never showdown equity, is not a probability,
/// and deliberately has no implicit or explicit conversion to <see cref="double"/> or
/// <see cref="EquityEstimate"/>.
/// </summary>
public readonly record struct HandCategoryScore(double Score)
{
    /// <summary>
    /// Builds the relative rank score by wrapping the internal deterministic
    /// <c>HandStrength.Estimate</c> heuristic for the supplied hole cards and board.
    /// </summary>
    public static HandCategoryScore FromHeuristic(
        IReadOnlyList<Card> hole,
        IReadOnlyList<Card> board)
        => new(HandStrength.Estimate(hole, board));
}
