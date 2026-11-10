namespace Wip.Tholdem.Agents.Simulation;

/// <summary>
/// Pure, testable metric formulas: win rate with a Wilson 95% interval, net chips,
/// big-blinds-per-100 with a normal interval, aggression factor and showdown rate.
/// </summary>
public static class MetricsCalculator
{
    public const double Z = 1.959963984540054;

    public static AgentMetrics Compute(
        string agentName,
        int seat,
        IReadOnlyList<HandOutcome> outcomes,
        long startingStack,
        long bigBlind)
    {
        ArgumentException.ThrowIfNullOrEmpty(agentName);
        ArgumentNullException.ThrowIfNull(outcomes);

        if (seat < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seat), seat, "A seat must not be negative.");
        }

        if (outcomes.Count == 0)
        {
            throw new ArgumentException("At least one hand outcome is required.", nameof(outcomes));
        }

        var seats = outcomes[0].FinalStacks.Count;
        if (seat >= seats)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seat),
                seat,
                $"A seat must be between 0 and {seats - 1}.");
        }

        if (bigBlind <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bigBlind), bigBlind, "The big blind must be positive.");
        }

        var hands = outcomes.Count;
        var wins = 0;
        long netChips = 0;
        long aggressiveActions = 0;
        long calls = 0;
        var showdowns = 0;
        var perHandBigBlinds = new double[hands];
        var sumBigBlinds = 0.0;

        for (var index = 0; index < hands; index++)
        {
            var outcome = outcomes[index];
            if (Contains(outcome.Winners, seat))
            {
                wins++;
            }

            var net = outcome.FinalStacks[seat] - startingStack;
            netChips += net;
            var bigBlinds = (double)net / bigBlind;
            perHandBigBlinds[index] = bigBlinds;
            sumBigBlinds += bigBlinds;
            aggressiveActions += outcome.AggressiveActions[seat];
            calls += outcome.Calls[seat];
            if (outcome.Showdown && outcome.ShowdownParticipants[seat])
            {
                showdowns++;
            }
        }

        var meanBigBlinds = sumBigBlinds / hands;
        var sampleStandardDeviation = 0.0;
        if (hands > 1)
        {
            var sumSquares = 0.0;
            for (var index = 0; index < hands; index++)
            {
                var deviation = perHandBigBlinds[index] - meanBigBlinds;
                sumSquares += deviation * deviation;
            }

            sampleStandardDeviation = Math.Sqrt(sumSquares / (hands - 1));
        }

        var margin = hands > 1
            ? Z * sampleStandardDeviation / Math.Sqrt(hands)
            : 0.0;
        var winRate = (double)wins / hands;
        var bigBlindsPer100 = meanBigBlinds * 100;

        return new AgentMetrics(
            agentName,
            hands,
            wins,
            winRate,
            Wilson(winRate, hands),
            netChips,
            bigBlindsPer100,
            new ConfidenceInterval((meanBigBlinds - margin) * 100, (meanBigBlinds + margin) * 100),
            aggressiveActions / (double)Math.Max(1L, calls),
            (double)showdowns / hands);
    }

    private static ConfidenceInterval Wilson(double probability, int hands)
    {
        var zSquared = Z * Z;
        var denominator = 1.0 + (zSquared / hands);
        var center = (probability + (zSquared / (2.0 * hands))) / denominator;
        var halfWidth = Z * Math.Sqrt(
            (probability * (1.0 - probability) / hands)
            + (zSquared / (4.0 * hands * hands))) / denominator;
        return new ConfidenceInterval(
            Math.Max(0.0, center - halfWidth),
            Math.Min(1.0, center + halfWidth));
    }

    private static bool Contains(IReadOnlyList<int> values, int value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] == value)
            {
                return true;
            }
        }

        return false;
    }
}