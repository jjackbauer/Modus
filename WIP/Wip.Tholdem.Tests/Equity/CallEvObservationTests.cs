using Wip.Tholdem.Agents.Equity;
using Wip.Tholdem.Cards;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;
using Xunit;

namespace Wip.Tholdem.Tests.Equity;

/// <summary>
/// Contract for the observation-to-call-EV bridge: the caller-eligible side-pot layers are counted
/// from the public totals, and the layered calculator supplies the effective call and contestable
/// pot. The fixture is a four-seat side pot (own 20, live 50/100, folded 30) with a pot of 200.
/// </summary>
public sealed class CallEvObservationTests
{
    [Fact]
    public void Compute_GivenSidePotObservation_ReturnsLayersAndEffectiveCall()
    {
        var observation = SidePotObservation(amountToCall: 80);

        var result = CallEvObservation.Compute(observation, 0.25);

        Assert.Equal(80L, result.EffectiveCall);
        Assert.Equal(280L, result.ContestablePot);
        Assert.Equal(-10.0, result.CallEv, 12);
        Assert.Equal(0.0, result.FoldEv);
        Assert.Equal(80.0 / 280.0, result.BreakEvenEquity, 12);
        Assert.Equal(3, result.Layers.Count);
        Assert.Equal(
            new[] { 30, 50, 100 },
            result.Layers.Select(static layer => layer.Level).ToArray());
        Assert.Equal(
            new long[] { 120, 60, 100 },
            result.Layers.Select(static layer => layer.Amount).ToArray());
        Assert.All(result.Layers, layer => Assert.True(layer.CallerEligible));
        Assert.All(result.Layers, layer => Assert.Equal(0.25, layer.Equity, 12));
    }

    [Fact]
    public void Compute_GivenNoAmountToCall_IsZeroEv()
    {
        var observation = SidePotObservation(amountToCall: 0);

        var result = CallEvObservation.Compute(observation, 0.25);

        Assert.Equal(0L, result.EffectiveCall);
        Assert.Equal(80L, result.ContestablePot);
        Assert.Equal(0.0, result.BreakEvenEquity, 12);
        Assert.Equal(0.25 * result.ContestablePot, result.CallEv, 12);
    }

    [Fact]
    public void Compute_GivenUniformEquityOutOfRange_Throws()
    {
        var observation = SidePotObservation(amountToCall: 80);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => CallEvObservation.Compute(observation, -0.01));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CallEvObservation.Compute(observation, 1.01));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CallEvObservation.Compute(observation, double.NaN));
    }

    private static PlayerObservation SidePotObservation(long amountToCall) => new()
    {
        HandNumber = 1,
        Street = Street.Preflop,
        ButtonSeat = 0,
        ActingSeat = 0,
        SeatCount = 4,
        Seat = 0,
        Board = Array.Empty<Card>(),
        OwnHoleCards = new[] { Card.Parse("As"), Card.Parse("Ah") },
        OwnStack = 500,
        OpponentStack = 180,
        Pot = 200,
        OwnStreetCommitment = 20,
        OpponentStreetCommitment = 180,
        AmountToCall = amountToCall,
        MinRaiseTo = 160,
        MaxRaiseTo = 500,
        LegalActionKinds = new[] { ActionKind.Fold, ActionKind.Call, ActionKind.Raise },
        IsComplete = false,
        Winners = Array.Empty<int>(),
        Seats = new[]
        {
            new SeatPublicState(0, 500, 20, false, false) { TotalCommitted = 20 },
            new SeatPublicState(1, 450, 50, false, false) { TotalCommitted = 50 },
            new SeatPublicState(2, 400, 100, false, false) { TotalCommitted = 100 },
            new SeatPublicState(3, 470, 30, true, false) { TotalCommitted = 30 },
        },
        OwnTotalCommitted = 20,
        ActionHistory = Array.Empty<PublicAction>(),
    };
}
