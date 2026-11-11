using Wip.Tholdem.Agents.Equity;
using Xunit;

namespace Wip.Tholdem.Tests.Equity;

public sealed class CallEvCalculatorTests
{
    [Fact]
    public void Compute_GivenHeadsUpCall_ReturnsLayeredEvAndBreakEven()
    {
        var result = CallEvCalculator.Compute(new CallEvInput(
            Pot: 100,
            OwnTotalCommitted: 20,
            OwnStackBehind: 100,
            AmountToCall: 60,
            Opponents: new[] { new CallEvOpponent(1, 80, false) },
            LayerEquities: new[] { 0.4 }));

        Assert.Equal(60L, result.EffectiveCall);
        Assert.Equal(160L, result.ContestablePot);
        Assert.Equal(4.0, result.CallEv, 12);
        Assert.Equal(0.0, result.FoldEv);
        Assert.Equal(0.375, result.BreakEvenEquity, 12);

        var layer = Assert.Single(result.Layers);
        Assert.Equal(80, layer.Level);
        Assert.Equal(160L, layer.Amount);
        Assert.Equal(new[] { 1 }, layer.EligibleSeats);
        Assert.True(layer.CallerEligible);
        Assert.Equal(0.4, layer.Equity);
    }

    [Fact]
    public void Compute_GivenShortAllInSidePot_UsesEligibility()
    {
        var result = CallEvCalculator.Compute(new CallEvInput(
            Pot: 170,
            OwnTotalCommitted: 20,
            OwnStackBehind: 500,
            AmountToCall: 80,
            Opponents: new[] { new CallEvOpponent(1, 50, false), new CallEvOpponent(2, 100, false) },
            LayerEquities: new[] { 0.3, 0.4 }));

        Assert.Equal(80L, result.EffectiveCall);
        Assert.Equal(250L, result.ContestablePot);
        Assert.Equal(5.0, result.CallEv, 12);
        Assert.Equal(0.0, result.FoldEv);
        Assert.Equal(0.32, result.BreakEvenEquity, 12);

        Assert.Equal(2, result.Layers.Count);
        Assert.Equal(50, result.Layers[0].Level);
        Assert.Equal(150L, result.Layers[0].Amount);
        Assert.Equal(new[] { 1, 2 }, result.Layers[0].EligibleSeats);
        Assert.True(result.Layers[0].CallerEligible);
        Assert.Equal(0.3, result.Layers[0].Equity);

        Assert.Equal(100, result.Layers[1].Level);
        Assert.Equal(100L, result.Layers[1].Amount);
        Assert.Equal(new[] { 2 }, result.Layers[1].EligibleSeats);
        Assert.True(result.Layers[1].CallerEligible);
        Assert.Equal(0.4, result.Layers[1].Equity);
    }

    [Fact]
    public void Compute_GivenFoldedDeadMoney_IncludesIt()
    {
        var result = CallEvCalculator.Compute(new CallEvInput(
            Pot: 130,
            OwnTotalCommitted: 20,
            OwnStackBehind: 500,
            AmountToCall: 60,
            Opponents: new[] { new CallEvOpponent(1, 30, true), new CallEvOpponent(2, 80, false) },
            LayerEquities: new[] { 0.25, 0.4 }));

        Assert.Equal(60L, result.EffectiveCall);
        Assert.Equal(190L, result.ContestablePot);
        Assert.Equal(2.5, result.CallEv, 12);
        Assert.Equal(0.3157894736842105, result.BreakEvenEquity, 12);

        Assert.Equal(2, result.Layers.Count);
        Assert.Equal(30, result.Layers[0].Level);
        Assert.Equal(90L, result.Layers[0].Amount);
        Assert.Equal(new[] { 2 }, result.Layers[0].EligibleSeats);
        Assert.True(result.Layers[0].CallerEligible);
        Assert.Equal(0.25, result.Layers[0].Equity);

        Assert.Equal(80, result.Layers[1].Level);
        Assert.Equal(100L, result.Layers[1].Amount);
        Assert.Equal(new[] { 2 }, result.Layers[1].EligibleSeats);
        Assert.True(result.Layers[1].CallerEligible);
        Assert.Equal(0.4, result.Layers[1].Equity);
    }

    [Fact]
    public void Compute_GivenCallAboveCover_ReturnsExcess()
    {
        var input = new CallEvInput(
            Pot: 180,
            OwnTotalCommitted: 80,
            OwnStackBehind: 1000,
            AmountToCall: 50,
            Opponents: new[] { new CallEvOpponent(1, 100, false) },
            LayerEquities: new[] { 0.5 });

        var result = CallEvCalculator.Compute(input);

        Assert.Equal(20L, result.EffectiveCall);
        Assert.Equal(30L, input.AmountToCall - result.EffectiveCall);
        Assert.Equal(200L, result.ContestablePot);
        Assert.Equal(80.0, result.CallEv, 12);
        Assert.Equal(0.10, result.BreakEvenEquity, 12);

        var layer = Assert.Single(result.Layers);
        Assert.Equal(100, layer.Level);
        Assert.Equal(200L, layer.Amount);
        Assert.Equal(new[] { 1 }, layer.EligibleSeats);
        Assert.True(layer.CallerEligible);
        Assert.Equal(0.5, layer.Equity);
    }

    [Fact]
    public void Compute_GivenFutureCost_SubtractsIt()
    {
        var result = CallEvCalculator.Compute(new CallEvInput(
            Pot: 170,
            OwnTotalCommitted: 20,
            OwnStackBehind: 500,
            AmountToCall: 80,
            Opponents: new[] { new CallEvOpponent(1, 50, false), new CallEvOpponent(2, 100, false) },
            LayerEquities: new[] { 0.3, 0.4 },
            FutureCost: 10));

        Assert.Equal(80L, result.EffectiveCall);
        Assert.Equal(250L, result.ContestablePot);
        Assert.Equal(-5.0, result.CallEv, 12);
        Assert.Equal(0.0, result.FoldEv);
        Assert.Equal(0.36, result.BreakEvenEquity, 12);
        Assert.Equal(2, result.Layers.Count);
    }

    [Fact]
    public void Compute_GivenInvalidInputs_Throws()
    {
        var valid = new CallEvInput(
            Pot: 100,
            OwnTotalCommitted: 20,
            OwnStackBehind: 100,
            AmountToCall: 60,
            Opponents: new[] { new CallEvOpponent(1, 80, false) },
            LayerEquities: new[] { 0.5 });

        Assert.Throws<ArgumentNullException>(() => CallEvCalculator.Compute(null!));
        Assert.Throws<ArgumentException>(
            () => CallEvCalculator.Compute(valid with { LayerEquities = Array.Empty<double>() }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CallEvCalculator.Compute(valid with { LayerEquities = new[] { 1.5 } }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CallEvCalculator.Compute(valid with { LayerEquities = new[] { -0.1 } }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CallEvCalculator.Compute(valid with { LayerEquities = new[] { double.NaN } }));
        Assert.Throws<ArgumentException>(
            () => CallEvCalculator.Compute(valid with { AmountToCall = 101 }));
        Assert.Throws<ArgumentException>(
            () => CallEvCalculator.Compute(valid with { Opponents = new[] { new CallEvOpponent(1, 80, true) } }));
        Assert.Throws<ArgumentException>(() => CallEvCalculator.Compute(valid with
        {
            Opponents = new[] { new CallEvOpponent(1, 80, false), new CallEvOpponent(1, 60, false) },
            LayerEquities = new[] { 0.5, 0.5 },
        }));
        Assert.Throws<ArgumentException>(
            () => CallEvCalculator.Compute(valid with { Opponents = Array.Empty<CallEvOpponent>() }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CallEvCalculator.Compute(valid with { Pot = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CallEvCalculator.Compute(valid with { FutureCost = -1 }));
    }
}
