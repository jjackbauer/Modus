using Wip.Tholdem.Engine;
using Wip.Tholdem.History;
using Xunit;

namespace Wip.Tholdem.Tests;

/// <summary>
/// Observation truthfulness (OBS-003, OBS-004): per-seat total commitments and the normalized
/// public action history are available at the decision boundary, while the canonical observation
/// bytes stay unchanged.
/// </summary>
public sealed class ObservationTruthfulnessTests
{
    private const string TwoSeatStartSeat0 =
        "{\"handNumber\":1,\"street\":0,\"buttonSeat\":0,\"actingSeat\":0,\"board\":[],"
        + "\"ownHoleCards\":[\"7d\",\"9c\"],\"ownStack\":995,\"opponentStack\":990,\"pot\":15,"
        + "\"ownStreetCommitment\":5,\"opponentStreetCommitment\":10,\"amountToCall\":5,"
        + "\"minRaiseTo\":20,\"maxRaiseTo\":1000,"
        + "\"legalActionKinds\":[\"Fold\",\"Call\",\"Raise\",\"AllIn\"],\"isComplete\":false,\"winners\":[]}";

    [Fact]
    public void ForSeat_GivenFourSeatHand_ExposesTotalCommitmentsThatSumToPot()
    {
        var state = PlayFourSeatSequence();

        Assert.Equal(Street.Flop, state.Street);
        Assert.Equal(60L, state.Pot);

        var observation = PlayerObservation.ForSeat(state, 0);

        Assert.Equal(state.TotalCommitted[0], observation.OwnTotalCommitted);
        Assert.Equal(state.SeatCount, observation.Seats.Count);
        for (var seat = 0; seat < state.SeatCount; seat++)
        {
            Assert.Equal(state.TotalCommitted[seat], observation.Seats[seat].TotalCommitted);
        }

        Assert.Equal(state.Pot, observation.Seats.Sum(publicSeat => publicSeat.TotalCommitted));
    }

    [Fact]
    public void ForSeat_GivenActionSequence_HistoryMatchesTheActionLog()
    {
        var state = PlayFourSeatSequence();
        var observation = PlayerObservation.ForSeat(state, 0);

        Assert.Equal(6, state.ActionLog.Count);
        Assert.Equal(state.ActionLog.Count, observation.ActionHistory.Count);

        for (var index = 0; index < state.ActionLog.Count; index++)
        {
            var logged = state.ActionLog[index];
            var action = observation.ActionHistory[index];
            Assert.Equal((Street)logged.Street, action.Street);
            Assert.Equal(logged.Seat, action.Seat);
            Assert.Equal(logged.Kind, action.Kind);
            Assert.Equal(logged.Amount, action.AmountTo);
            Assert.Equal(logged.PotAfter, action.PotAfter);
        }
    }

    [Fact]
    public void ToCanonicalJson_GivenFourSeatObservation_DoesNotSerializeTheNewFields()
    {
        var observation = PlayerObservation.ForSeat(PlayFourSeatSequence(), 0);
        var json = observation.ToCanonicalJson();

        Assert.Contains("\"seats\":[", json);
        Assert.DoesNotContain("totalCommitted", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ownTotalCommitted", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("actionHistory", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ForSeat_GivenTwoSeatObservation_CanonicalBytesStillMatchTheRecordedFixture()
    {
        var state = HeadsUpHandEngine.Start(42);
        var observation = PlayerObservation.ForSeat(state, 0);

        Assert.Equal(TwoSeatStartSeat0, observation.ToCanonicalJson());
        Assert.Equal(5L, observation.OwnTotalCommitted);
        Assert.Equal(5L, observation.Seats[0].TotalCommitted);
        Assert.Equal(10L, observation.Seats[1].TotalCommitted);
        Assert.Empty(observation.ActionHistory);
    }

    private static TableState PlayFourSeatSequence()
    {
        var state = HoldemHandEngine.Start(20261110UL, 4);
        state = HoldemHandEngine.Apply(state, PokerAction.Call());
        state = HoldemHandEngine.Apply(state, PokerAction.Call());
        state = HoldemHandEngine.Apply(state, PokerAction.Call());
        state = HoldemHandEngine.Apply(state, PokerAction.Check());
        state = HoldemHandEngine.Apply(state, PokerAction.BetTo(10));
        return HoldemHandEngine.Apply(state, PokerAction.Call());
    }
}
