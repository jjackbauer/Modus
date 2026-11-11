using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Cards;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;
using Xunit;

namespace Wip.Tholdem.Tests.Learning;

/// <summary>
/// Contract for the preflop situation classifier: the situation is derived from the public action
/// history only, and blind defense additionally requires the big-blind or button seat facing one
/// raise.
/// </summary>
public sealed class PreflopSituationTests
{
    [Fact]
    public void Classify_GivenEmptyHistory_IsUnopened()
    {
        var observation = PreflopObservation(seat: 2, Array.Empty<PublicAction>());

        var context = PreflopSituationClassifier.Classify(observation);

        Assert.Equal(PreflopSituation.Unopened, context.Situation);
        Assert.Equal(0, context.Raises);
        Assert.Equal(0, context.Calls);
        Assert.False(context.IsBlindDefense);
    }

    [Fact]
    public void Classify_GivenOneCall_IsFacingLimp()
    {
        var observation = PreflopObservation(
            seat: 2,
            new[] { Action(1, ActionKind.Call, 10) });

        var context = PreflopSituationClassifier.Classify(observation);

        Assert.Equal(PreflopSituation.FacingLimp, context.Situation);
        Assert.Equal(0, context.Raises);
        Assert.Equal(1, context.Calls);
        Assert.False(context.IsBlindDefense);
    }

    [Fact]
    public void Classify_GivenOneRaise_IsFacingOpenAndBlindDefenseForButton()
    {
        var button = PreflopObservation(
            seat: 0,
            new[] { Action(2, ActionKind.Raise, 30) });

        var buttonContext = PreflopSituationClassifier.Classify(button);

        Assert.Equal(PreflopSituation.FacingOpen, buttonContext.Situation);
        Assert.Equal(1, buttonContext.Raises);
        Assert.Equal(0, buttonContext.Calls);
        Assert.True(buttonContext.IsBlindDefense);

        var bigBlind = PreflopObservation(
            seat: 1,
            new[] { Action(2, ActionKind.Raise, 30) });
        Assert.True(PreflopSituationClassifier.Classify(bigBlind).IsBlindDefense);

        var underTheGun = PreflopObservation(
            seat: 2,
            new[] { Action(3, ActionKind.Raise, 30) });
        Assert.False(PreflopSituationClassifier.Classify(underTheGun).IsBlindDefense);
    }

    [Fact]
    public void Classify_GivenTwoRaises_IsFacingThreeBet()
    {
        var observation = PreflopObservation(
            seat: 2,
            new[]
            {
                Action(1, ActionKind.Raise, 30),
                Action(3, ActionKind.AllIn, 500),
            });

        var context = PreflopSituationClassifier.Classify(observation);

        Assert.Equal(PreflopSituation.FacingThreeBet, context.Situation);
        Assert.Equal(2, context.Raises);
        Assert.Equal(0, context.Calls);
        Assert.False(context.IsBlindDefense);
    }

    private static PublicAction Action(int seat, ActionKind kind, long amountTo) =>
        new(Street.Preflop, seat, kind, amountTo, PotAfter: 0);

    private static PlayerObservation PreflopObservation(
        int seat,
        IReadOnlyList<PublicAction> actions) => new()
    {
        HandNumber = 1,
        Street = Street.Preflop,
        ButtonSeat = 0,
        ActingSeat = seat,
        SeatCount = 4,
        Seat = seat,
        Board = Array.Empty<Card>(),
        OwnHoleCards = new[] { Card.Parse("As"), Card.Parse("Ah") },
        OwnStack = 500,
        OpponentStack = 1500,
        Pot = 30,
        OwnStreetCommitment = 0,
        OpponentStreetCommitment = 30,
        AmountToCall = 10,
        MinRaiseTo = 20,
        MaxRaiseTo = 500,
        LegalActionKinds = new[] { ActionKind.Fold, ActionKind.Call, ActionKind.Raise },
        IsComplete = false,
        Winners = Array.Empty<int>(),
        Seats = new[]
        {
            new SeatPublicState(0, 500, 0, false, false),
            new SeatPublicState(1, 500, 10, false, false),
            new SeatPublicState(2, 500, 10, false, false),
            new SeatPublicState(3, 500, 10, false, false),
        },
        OwnTotalCommitted = 0,
        ActionHistory = actions,
    };
}
