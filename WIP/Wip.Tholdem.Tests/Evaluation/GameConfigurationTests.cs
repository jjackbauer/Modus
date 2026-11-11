using Wip.Tholdem.Agents.Evaluation;
using Wip.Tholdem.Agents.Learning;
using Xunit;

namespace Wip.Tholdem.Tests.Evaluation;

/// <summary>
/// Round-one versioned game contract: the four-seat cash default, the exact canonical JSON key
/// order and hash, the precise refusal codes and the mapping onto simulation options.
/// </summary>
public sealed class GameConfigurationTests
{
    private const string FourSeatCashJson =
        "{\"schema\":\"tholdem.game.v1\",\"seats\":4,\"smallBlind\":5,\"bigBlind\":10,\"startingStack\":1000,\"startingStacks\":null,\"ante\":0,\"buttonRule\":\"tholdem.button.per-hand-one-seat.v1\",\"actionCapPerHand\":1000,\"sidePotRule\":\"tholdem.sidepot.layered.v1\",\"mode\":\"CashIndependentHands\",\"metricId\":\"tholdem.cash.bb100.v1\"}";

    [Fact]
    public void FourSeatCash_GivenDefaults_DeclaresEverySetting()
    {
        var configuration = GameConfiguration.FourSeatCash();

        Assert.Equal(4, configuration.Seats);
        Assert.Equal(1000L, configuration.StartingStack);
        Assert.Equal(5L, configuration.SmallBlind);
        Assert.Equal(10L, configuration.BigBlind);
        Assert.Equal(GameConfiguration.CashMetricId, configuration.MetricId);
        Assert.Equal(1000, configuration.ActionCapPerHand);
        Assert.Equal(GameConfiguration.ButtonRulePerHand, configuration.ButtonRule);
        Assert.Equal(GameConfiguration.SidePotRuleLayered, configuration.SidePotRule);
        Assert.Equal(0L, configuration.Ante);
        Assert.Null(configuration.StartingStacks);
        Assert.Equal(SimulationMode.CashIndependentHands, configuration.Mode);

        var json = configuration.ToCanonicalJson();
        Assert.Equal(FourSeatCashJson, json);
        foreach (var key in new[]
                 {
                     "schema", "seats", "smallBlind", "bigBlind", "startingStack", "startingStacks",
                     "ante", "buttonRule", "actionCapPerHand", "sidePotRule", "mode", "metricId",
                 })
        {
            Assert.Contains("\"" + key + "\":", json);
        }

        var first = new GameConfiguration().ConfigHash;
        var second = new GameConfiguration().ConfigHash;
        Assert.Equal(first, second);
        Assert.Equal(StrategyArtifact.ComputeHash(FourSeatCashJson), first);
    }

    [Fact]
    public void Ante_GivenPositive_RefusedAsUnsupported()
    {
        var configuration = new GameConfiguration { Ante = 1 };

        var exception = Assert.Throws<GameConfigurationException>(() => configuration.Validate());

        Assert.Equal("ante-not-supported", exception.Code);
    }

    [Fact]
    public void TournamentMode_GivenPersistentStacks_RefusedAsNotImplemented()
    {
        var configuration = new GameConfiguration
        {
            Mode = SimulationMode.TournamentPersistentStacks,
            StartingStacks = new long[] { 1000, 1000, 1000, 1000 },
        };

        var exception = Assert.Throws<GameConfigurationException>(() => configuration.Validate());

        Assert.Equal("mode-tournament-not-implemented", exception.Code);
    }

    [Fact]
    public void StartingStacks_GivenWrongCount_Refused()
    {
        var configuration = new GameConfiguration { StartingStacks = new long[] { 1000, 1000, 1000 } };

        var exception = Assert.Throws<GameConfigurationException>(() => configuration.Validate());

        Assert.Equal("starting-stacks-count", exception.Code);
    }

    [Fact]
    public void SmallBlind_GivenAboveBigBlind_Refused()
    {
        var configuration = new GameConfiguration { SmallBlind = 20, BigBlind = 10 };

        var exception = Assert.Throws<GameConfigurationException>(() => configuration.Validate());

        Assert.Equal("small-blind-above-big-blind", exception.Code);
    }

    [Fact]
    public void ToSimulationOptions_GivenConfig_MapsEveryField()
    {
        var configuration = new GameConfiguration
        {
            Seats = 4,
            SmallBlind = 4,
            BigBlind = 8,
            StartingStack = 800,
            StartingStacks = new long[] { 800, 900, 1000, 1100 },
            ActionCapPerHand = 333,
        };

        var options = configuration.ToSimulationOptions(777UL, 12, "history");

        Assert.Equal(777UL, options.Seed);
        Assert.Equal(12, options.Hands);
        Assert.Equal(4L, options.SmallBlind);
        Assert.Equal(8L, options.BigBlind);
        Assert.Equal(800L, options.StartingStack);
        Assert.NotNull(options.StartingStacks);
        Assert.Equal(configuration.Seats, options.StartingStacks!.Count);
        Assert.Equal(new long[] { 800, 900, 1000, 1100 }, options.StartingStacks);
        Assert.Equal(333, options.ActionCapPerHand);
        Assert.Equal("history", options.HistoryDirectory);
        Assert.True(options.AlternateButton);
    }

    [Fact]
    public void ConfigHash_GivenDifferentSeats_Changes()
    {
        var fourSeats = new GameConfiguration();
        var threeSeats = new GameConfiguration
        {
            Seats = 3,
            StartingStacks = new long[] { 1000, 1000, 1000 },
        };

        Assert.NotEqual(fourSeats.ConfigHash, threeSeats.ConfigHash);
        Assert.Equal(64, fourSeats.ConfigHash.Length);
        Assert.Equal(64, threeSeats.ConfigHash.Length);
    }
}
