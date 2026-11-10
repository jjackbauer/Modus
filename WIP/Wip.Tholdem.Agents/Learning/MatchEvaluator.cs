using Wip.Tholdem.Agents.Simulation;

namespace Wip.Tholdem.Agents.Learning;

/// <summary>
/// Paired, held-out evaluation. For every seed the challenger plays one match in each seat against
/// the incumbent, so the deal variance cancels inside the pair. The seed derivation is the same
/// <see cref="SeedDerivation"/> the simulator uses; seeds are never the training seed.
/// </summary>
public static class MatchEvaluator
{
    /// <summary>The hand-number stream the held-out evaluation seeds live in.</summary>
    public const int SeedHandBase = 9_000;

    public static EvaluationReport Evaluate(
        IPokerAgent challenger,
        string challengerId,
        IPokerAgent incumbent,
        string incumbentId,
        ulong baseSeed,
        int seeds,
        int handsPerMatch,
        long startingStack = 1000,
        long bigBlind = 10)
    {
        ArgumentNullException.ThrowIfNull(challenger);
        ArgumentException.ThrowIfNullOrEmpty(challengerId);
        ArgumentNullException.ThrowIfNull(incumbent);
        ArgumentException.ThrowIfNullOrEmpty(incumbentId);

        if (seeds < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(seeds), seeds, "An evaluation needs at least one seed.");
        }

        if (handsPerMatch < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(handsPerMatch),
                handsPerMatch,
                "An evaluation match needs at least one hand.");
        }

        if (bigBlind < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(bigBlind), bigBlind, "The big blind must be at least two chips.");
        }

        var smallBlind = bigBlind / 2;
        var perSeedBbPer100 = new double[seeds];
        long challengerNetChips = 0;
        var challengerWins = 0;
        var incumbentWins = 0;
        var ties = 0;

        for (var index = 0; index < seeds; index++)
        {
            var seed = SeedDerivation.Derive(baseSeed, SeedHandBase + index, 0, 0, 0);
            var options = new SimulationOptions(
                seed,
                handsPerMatch,
                startingStack,
                smallBlind,
                bigBlind,
                AlternateButton: true);

            // Seat 0 first, then the mirrored seat assignment. Both matches deal the same cards.
            var forward = MatchSimulator.Run(challenger, incumbent, options);
            var reverse = MatchSimulator.Run(incumbent, challenger, options);

            var forwardNet = forward.Agents[0].NetChips;
            var reverseNet = reverse.Agents[1].NetChips;
            var net = forwardNet + reverseNet;
            challengerNetChips += net;
            perSeedBbPer100[index] = (net / (double)bigBlind) / (2.0 * handsPerMatch) * 100.0;

            challengerWins += (forwardNet > 0 ? 1 : 0) + (reverseNet > 0 ? 1 : 0);
            incumbentWins += (forwardNet < 0 ? 1 : 0) + (reverseNet < 0 ? 1 : 0);
            ties += (forwardNet == 0 ? 1 : 0) + (reverseNet == 0 ? 1 : 0);
        }

        var mean = Mean(perSeedBbPer100);
        var halfWidth = HalfWidth(perSeedBbPer100, mean);
        return new EvaluationReport(
            challengerId,
            incumbentId,
            seeds,
            handsPerMatch,
            bigBlind,
            mean,
            new ConfidenceInterval(mean - halfWidth, mean + halfWidth),
            mean - halfWidth,
            challengerWins,
            incumbentWins,
            ties,
            challengerNetChips);
    }

    private static double Mean(double[] values)
    {
        var sum = 0.0;
        for (var index = 0; index < values.Length; index++)
        {
            sum += values[index];
        }

        return sum / values.Length;
    }

    private static double HalfWidth(double[] values, double mean)
    {
        if (values.Length < 2)
        {
            return 0.0;
        }

        var sumSquares = 0.0;
        for (var index = 0; index < values.Length; index++)
        {
            var deviation = values[index] - mean;
            sumSquares += deviation * deviation;
        }

        var sampleStandardDeviation = Math.Sqrt(sumSquares / (values.Length - 1));
        return MetricsCalculator.Z * sampleStandardDeviation / Math.Sqrt(values.Length);
    }
}
