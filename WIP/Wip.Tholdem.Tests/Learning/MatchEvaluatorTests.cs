using Wip.Tholdem.Agents;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Simulation;
using Xunit;

namespace Wip.Tholdem.Tests.Learning;

public sealed class MatchEvaluatorTests
{
    private const long BigBlind = 10;
    private const int HandsPerMatch = 4;

    [Fact]
    public void Evaluate_GivenPairedHeldOutSeeds_ExpectedNetChipsWinsAndNormalInterval()
    {
        var challengerGenome = new StrategyGenome(0.6, 0.3, 0.25, 1.5);
        var incumbentGenome = new StrategyGenome(0.72, 0.45, 0.12, 1.0);

        var report = MatchEvaluator.Evaluate(
            new StrategyAgent(challengerGenome, "challenger"),
            "challenger-1",
            new StrategyAgent(incumbentGenome, "incumbent"),
            "incumbent-1",
            baseSeed: 777UL,
            seeds: 6,
            handsPerMatch: HandsPerMatch,
            startingStack: 1000,
            bigBlind: BigBlind);

        Assert.Equal("challenger-1", report.ChallengerId);
        Assert.Equal("incumbent-1", report.IncumbentId);
        Assert.Equal(6, report.Seeds);
        Assert.Equal(HandsPerMatch, report.HandsPerMatch);
        Assert.Equal(BigBlind, report.BigBlind);

        // Recompute every paired match independently from the contracted derivation.
        var challenger = new StrategyAgent(challengerGenome, "challenger");
        var incumbent = new StrategyAgent(incumbentGenome, "incumbent");
        var perSeedBbPer100 = new double[report.Seeds];
        long netChips = 0;
        var challengerWins = 0;
        var incumbentWins = 0;
        var ties = 0;

        for (var index = 0; index < report.Seeds; index++)
        {
            var seed = SeedDerivation.Derive(777UL, 9_000 + index, 0, 0, 0);
            var options = new SimulationOptions(seed, HandsPerMatch, 1000, 5, BigBlind, AlternateButton: true);
            var forward = MatchSimulator.Run(challenger, incumbent, options);
            var reverse = MatchSimulator.Run(incumbent, challenger, options);

            var forwardNet = forward.Agents[0].NetChips;
            var reverseNet = reverse.Agents[1].NetChips;
            netChips += forwardNet + reverseNet;
            perSeedBbPer100[index] = ((forwardNet + reverseNet) / (double)BigBlind) / (2.0 * HandsPerMatch) * 100.0;

            challengerWins += (forwardNet > 0 ? 1 : 0) + (reverseNet > 0 ? 1 : 0);
            incumbentWins += (forwardNet < 0 ? 1 : 0) + (reverseNet < 0 ? 1 : 0);
            ties += (forwardNet == 0 ? 1 : 0) + (reverseNet == 0 ? 1 : 0);
        }

        var mean = perSeedBbPer100.Average();
        var sumSquares = perSeedBbPer100.Sum(value => (value - mean) * (value - mean));
        var sampleStandardDeviation = Math.Sqrt(sumSquares / (perSeedBbPer100.Length - 1));
        var halfWidth = MetricsCalculator.Z * sampleStandardDeviation / Math.Sqrt(perSeedBbPer100.Length);

        Assert.Equal(netChips, report.ChallengerNetChips);
        Assert.Equal(challengerWins, report.ChallengerWins);
        Assert.Equal(incumbentWins, report.IncumbentWins);
        Assert.Equal(ties, report.Ties);
        Assert.Equal(2 * report.Seeds, report.ChallengerWins + report.IncumbentWins + report.Ties);
        Assert.Equal(mean, report.MeanBbPer100, 9);
        Assert.Equal(mean - halfWidth, report.BbPer100Ci.Low, 9);
        Assert.Equal(mean + halfWidth, report.BbPer100Ci.High, 9);
        Assert.Equal(report.BbPer100Ci.Low, report.LowerBoundBbPer100, 9);
        Assert.True(report.BbPer100Ci.Low < report.MeanBbPer100);
        Assert.True(report.MeanBbPer100 < report.BbPer100Ci.High);
        Assert.Matches("^[0-9a-f]{64}$", report.ReportHash);
        Assert.Equal(
            report.ReportHash,
            LearningTestSupport.Sha256Hex(LearningTestSupport.Utf8(report.ToCanonicalJson())));
    }

    [Fact]
    public void Evaluate_GivenOneSeed_ExpectedDegenerateSymmetricIntervalAndHeldOutSeed()
    {
        var challengerGenome = new StrategyGenome(0.5, 0.45, 0.1, 1.0);
        var incumbentGenome = new StrategyGenome(0.5, 0.45, 0.1, 1.0);

        var report = MatchEvaluator.Evaluate(
            new StrategyAgent(challengerGenome, "challenger"),
            "challenger-1",
            new StrategyAgent(incumbentGenome, "incumbent"),
            "incumbent-1",
            baseSeed: 4242UL,
            seeds: 1,
            handsPerMatch: HandsPerMatch,
            startingStack: 1000,
            bigBlind: BigBlind);

        Assert.Equal(1, report.Seeds);
        Assert.Equal(report.MeanBbPer100, report.BbPer100Ci.Low, 12);
        Assert.Equal(report.MeanBbPer100, report.BbPer100Ci.High, 12);
        Assert.Equal(report.MeanBbPer100, report.LowerBoundBbPer100, 12);

        // The evaluation must use the held-out derivation, not the training seed.
        var seed = SeedDerivation.Derive(4242UL, 9_000, 0, 0, 0);
        Assert.NotEqual(4242UL, seed);
        var options = new SimulationOptions(seed, HandsPerMatch, 1000, 5, BigBlind, AlternateButton: true);
        var challenger = new StrategyAgent(challengerGenome, "challenger");
        var incumbent = new StrategyAgent(incumbentGenome, "incumbent");
        var forward = MatchSimulator.Run(challenger, incumbent, options);
        var reverse = MatchSimulator.Run(incumbent, challenger, options);
        Assert.Equal(forward.Agents[0].NetChips + reverse.Agents[1].NetChips, report.ChallengerNetChips);
        Assert.Equal(
            ((forward.Agents[0].NetChips + reverse.Agents[1].NetChips) / (double)BigBlind) / (2.0 * HandsPerMatch) * 100.0,
            report.MeanBbPer100,
            9);
    }
}
