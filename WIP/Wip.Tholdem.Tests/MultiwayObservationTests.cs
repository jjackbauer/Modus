using Wip.Tholdem.Cards;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;
using Xunit;

namespace Wip.Tholdem.Tests;

public sealed class MultiwayObservationTests
{
    private const string TwoSeatStartSeat0 =
        "{\"handNumber\":1,\"street\":0,\"buttonSeat\":0,\"actingSeat\":0,\"board\":[],"
        + "\"ownHoleCards\":[\"7d\",\"9c\"],\"ownStack\":995,\"opponentStack\":990,\"pot\":15,"
        + "\"ownStreetCommitment\":5,\"opponentStreetCommitment\":10,\"amountToCall\":5,"
        + "\"minRaiseTo\":20,\"maxRaiseTo\":1000,"
        + "\"legalActionKinds\":[\"Fold\",\"Call\",\"Raise\",\"AllIn\"],\"isComplete\":false,\"winners\":[]}";

    private const string TwoSeatStartSeat1 =
        "{\"handNumber\":1,\"street\":0,\"buttonSeat\":0,\"actingSeat\":0,\"board\":[],"
        + "\"ownHoleCards\":[\"Qc\",\"4s\"],\"ownStack\":990,\"opponentStack\":995,\"pot\":15,"
        + "\"ownStreetCommitment\":10,\"opponentStreetCommitment\":5,\"amountToCall\":5,"
        + "\"minRaiseTo\":20,\"maxRaiseTo\":1000,"
        + "\"legalActionKinds\":[\"Fold\",\"Call\",\"Raise\",\"AllIn\"],\"isComplete\":false,\"winners\":[]}";

    private const string TwoSeatFlopSeat0 =
        "{\"handNumber\":1,\"street\":1,\"buttonSeat\":0,\"actingSeat\":1,"
        + "\"board\":[\"8c\",\"7h\",\"2s\"],\"ownHoleCards\":[\"7d\",\"9c\"],\"ownStack\":990,"
        + "\"opponentStack\":990,\"pot\":20,\"ownStreetCommitment\":0,"
        + "\"opponentStreetCommitment\":0,\"amountToCall\":0,\"minRaiseTo\":10,\"maxRaiseTo\":990,"
        + "\"legalActionKinds\":[\"Check\",\"Bet\",\"AllIn\"],\"isComplete\":false,\"winners\":[]}";

    [Fact]
    public void ToCanonicalJson_GivenTwoSeats_ExpectedPreChangeBytesAndNoNewSeatFields()
    {
        var start = HeadsUpHandEngine.Start(42);
        Assert.Equal(TwoSeatStartSeat0, PlayerObservation.ForSeat(start, 0).ToCanonicalJson());
        Assert.Equal(TwoSeatStartSeat1, PlayerObservation.ForSeat(start, 1).ToCanonicalJson());

        var flop = HeadsUpHandEngine.Apply(
            HeadsUpHandEngine.Apply(start, PokerAction.Call()),
            PokerAction.Check());
        Assert.Equal(TwoSeatFlopSeat0, PlayerObservation.ForSeat(flop, 0).ToCanonicalJson());

        for (var seat = 0; seat < 2; seat++)
        {
            var json = PlayerObservation.ForSeat(flop, seat).ToCanonicalJson();
            Assert.DoesNotContain("\"seats\"", json);
            Assert.DoesNotContain("\"seatCount\"", json);
            Assert.DoesNotContain("\"seat\":", json);
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    public void ForSeat_GivenThreeOrSixSeats_ExpectedNoSeedDeckOrForeignHoleCards(int seats)
    {
        var state = CheckCallToFlop(HoldemHandEngine.Start(4242, seats));
        Assert.Equal(Street.Flop, state.Street);

        for (var seat = 0; seat < seats; seat++)
        {
            var observation = PlayerObservation.ForSeat(state, seat);
            var json = observation.ToCanonicalJson();

            Assert.Equal(seats, observation.SeatCount);
            Assert.Equal(seat, observation.Seat);
            Assert.Equal(state.HoleCards[seat].ToArray(), observation.OwnHoleCards.ToArray());
            Assert.DoesNotContain("seed", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"deck\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"seatCount\":" + seats, json);
            Assert.Contains("\"seats\":[", json);

            for (var other = 0; other < seats; other++)
            {
                if (other == seat)
                {
                    continue;
                }

                foreach (var card in state.HoleCards[other])
                {
                    Assert.DoesNotContain("\"" + card + "\"", json);
                }
            }

            var allowed = new HashSet<Card>(state.Board);
            foreach (var card in state.HoleCards[seat])
            {
                allowed.Add(card);
            }

            for (var suit = 0; suit <= 3; suit++)
            {
                for (var rank = 2; rank <= 14; rank++)
                {
                    var card = new Card((Rank)rank, (Suit)suit);
                    if (!allowed.Contains(card))
                    {
                        Assert.DoesNotContain("\"" + card + "\"", json);
                    }
                }
            }

            foreach (var card in observation.OwnHoleCards)
            {
                Assert.Contains("\"" + card + "\"", json);
            }

            Assert.Equal(seats, observation.Seats.Count);
            for (var publicSeat = 0; publicSeat < seats; publicSeat++)
            {
                var publicState = observation.Seats[publicSeat];
                Assert.Equal(publicSeat, publicState.Seat);
                Assert.Equal(state.Stacks[publicSeat], publicState.Stack);
                Assert.Equal(state.StreetCommitted[publicSeat], publicState.StreetCommitment);
                Assert.Equal(state.Folded[publicSeat], publicState.Folded);
                Assert.Equal(state.AllIn[publicSeat], publicState.AllIn);
            }
        }
    }

    [Fact]
    public void ForSeat_GivenFoldedSeatAtThreeSeats_ExpectedPublicFoldedFlagAndNoHiddenLeak()
    {
        var state = HoldemHandEngine.Start(555, 3);
        state = HoldemHandEngine.Apply(state, PokerAction.Call());
        state = HoldemHandEngine.Apply(state, PokerAction.RaiseTo(30));
        state = HoldemHandEngine.Apply(state, PokerAction.Fold());

        Assert.True(state.Folded[1]);
        for (var seat = 0; seat < 3; seat++)
        {
            var observation = PlayerObservation.ForSeat(state, seat);
            Assert.True(observation.Seats[1].Folded);
            Assert.Equal(state.Stacks[1], observation.Seats[1].Stack);
            Assert.Equal(state.StreetCommitted[1], observation.Seats[1].StreetCommitment);
            if (seat != 1)
            {
                Assert.DoesNotContain("\"" + state.HoleCards[1][0] + "\"", observation.ToCanonicalJson());
                Assert.DoesNotContain("\"" + state.HoleCards[1][1] + "\"", observation.ToCanonicalJson());
            }
        }
    }

    [Fact]
    public void ForSeat_GivenSeatOutsideTable_ExpectedArgumentOutOfRange()
    {
        var state = HoldemHandEngine.Start(1, 3);
        Assert.Throws<ArgumentOutOfRangeException>(() => PlayerObservation.ForSeat(state, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlayerObservation.ForSeat(state, 3));
    }

    private static TableState CheckCallToFlop(TableState state)
    {
        while (state.Street == Street.Preflop)
        {
            var legal = HoldemHandEngine.GetLegalActions(state);
            state = HoldemHandEngine.Apply(
                state,
                legal.CanCheck ? PokerAction.Check() : PokerAction.Call());
        }

        return state;
    }
}
