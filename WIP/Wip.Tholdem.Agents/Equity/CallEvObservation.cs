using Wip.Tholdem.History;

namespace Wip.Tholdem.Agents.Equity;

/// <summary>
/// Bridges a <see cref="PlayerObservation"/> to the layered call-EV calculator: it projects the
/// public seat totals into <see cref="CallEvInput"/>, counts the caller-eligible pot layers and
/// repeats the supplied uniform equity for each of them. Per-layer equity is deliberately a single
/// uniform share in this slice; the estimator exposes only one overall pot share.
/// </summary>
public static class CallEvObservation
{
    /// <summary>
    /// Computes the layered call EV for the acting seat. The uniform layer equity is validated in
    /// [0, 1] and repeated once per caller-eligible layer. The effective call mirrors the
    /// calculator's cap at the largest live commitment, so the layer count matches exactly the
    /// caller-eligible layers the calculator builds.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="observation"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="uniformLayerEquity"/> is not finite or not in [0, 1].
    /// </exception>
    public static CallEvResult Compute(PlayerObservation observation, double uniformLayerEquity)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (!double.IsFinite(uniformLayerEquity)
            || uniformLayerEquity < 0.0
            || uniformLayerEquity > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(uniformLayerEquity),
                uniformLayerEquity,
                "The uniform layer equity must be finite and between 0 and 1.");
        }

        var opponents = new List<CallEvOpponent>(Math.Max(0, observation.Seats.Count - 1));
        var liveMax = 0L;
        foreach (var seat in observation.Seats)
        {
            if (seat.Seat == observation.Seat)
            {
                continue;
            }

            opponents.Add(new CallEvOpponent(seat.Seat, seat.TotalCommitted, seat.Folded));
            if (!seat.Folded)
            {
                liveMax = Math.Max(liveMax, seat.TotalCommitted);
            }
        }

        var effectiveCall = Math.Min(
            observation.AmountToCall,
            Math.Max(0L, liveMax - observation.OwnTotalCommitted));
        var ownAfter = observation.OwnTotalCommitted + effectiveCall;

        var levels = new SortedSet<long>();
        if (ownAfter > 0)
        {
            levels.Add(ownAfter);
        }

        foreach (var opponent in opponents)
        {
            if (opponent.TotalCommitted > 0)
            {
                levels.Add(opponent.TotalCommitted);
            }
        }

        var callerEligibleLayers = 0;
        foreach (var level in levels)
        {
            if (ownAfter >= level)
            {
                callerEligibleLayers++;
            }
        }

        // The current estimator exposes one overall pot share; per-layer equities are a later
        // refinement and are therefore labelled as a uniform repetition here.
        var layerEquities = new double[callerEligibleLayers];
        Array.Fill(layerEquities, uniformLayerEquity);

        return CallEvCalculator.Compute(new CallEvInput(
            observation.Pot,
            observation.OwnTotalCommitted,
            observation.OwnStack,
            observation.AmountToCall,
            opponents,
            layerEquities));
    }
}
