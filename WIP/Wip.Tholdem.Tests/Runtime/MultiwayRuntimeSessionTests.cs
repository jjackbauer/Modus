using System.Globalization;
using System.Text.Json;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Tholdem.Agents.Runtime;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;
using Xunit;

namespace Wip.Tholdem.Tests.Runtime;

/// <summary>
/// Round-two runtime session-store contract for two to six seats: per-seat projections without
/// hidden information and opponent statistics aggregated over every other seat.
/// </summary>
public sealed class MultiwayRuntimeSessionTests
{
    private static readonly CapabilityContext Context =
        new(new SessionId("multiway-runtime-test"), AppContext.BaseDirectory);

    [Fact]
    public void BuildFor_GivenFourSeatHand_ExpectedOnlyOwnHoleCardsAndNoHiddenInformation()
    {
        var record = PlayScriptedHand(24680UL, 4, 1, (_, observation) => CheckOrCall(observation));
        Assert.Equal(4, record.SeatCount);
        Assert.True(record.Showdown);

        var store = new SessionHandStore();
        store.Record(record);
        Assert.Equal(4, store.SeatCount);

        var view = Assert.Single(store.BuildFor(1, 10));
        Assert.Equal(record.HandNumber, view.HandNumber);
        Assert.Equal(
            record.HoleCards[1].Select(card => card.ToString()).ToArray(),
            view.OwnHoleCards);
        Assert.Equal(record.Board.Select(card => card.ToString()).ToArray(), view.Board);
        Assert.Equal(record.Winners.ToArray(), view.Winners);
        Assert.Equal(record.Showdown, view.Showdown);
        Assert.Equal(record.PotAwarded, view.PotAwarded);
        Assert.Equal(record.FinalStacks[1] - record.StartingStacks[1], view.NetChips);

        var json = JsonSerializer.Serialize(view);
        for (var other = 0; other < record.SeatCount; other++)
        {
            if (other == 1)
            {
                continue;
            }

            foreach (var card in record.HoleCards[other])
            {
                Assert.DoesNotContain("\"" + card + "\"", json, StringComparison.Ordinal);
            }
        }

        foreach (var card in record.HoleCards[1])
        {
            Assert.Contains("\"" + card + "\"", json, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(record.Seed.ToString(CultureInfo.InvariantCulture), json, StringComparison.Ordinal);
        Assert.DoesNotContain("seed", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("deck", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpponentStatsTool_GivenFourSeatHands_ExpectedHandWeightedAggregateOfOtherSeats()
    {
        // Seat 0 raises/bets, seat 1 (the requesting seat) folds preflop, seat 2 calls, seat 3 folds.
        // Every hand therefore reaches a showdown between seats 0 and 2.
        var policies = new Func<PlayerObservation, PokerAction>[]
        {
            RaiseOrBet,
            FoldOrCheck,
            CheckOrCall,
            FoldOrCheck,
        };
        var store = new SessionHandStore(4);
        store.Record(PlayScriptedHand(13579UL, 4, 1, (seat, observation) => policies[seat](observation)));
        store.Record(PlayScriptedHand(97531UL, 4, 2, (seat, observation) => policies[seat](observation)));

        var own = store.BuildStatisticsFor(1);
        Assert.Equal(2, own.Hands);
        Assert.Equal(0.0, own.Vpip);
        Assert.Equal(0.0, own.Pfr);
        Assert.Equal(0.0, own.AggressionFactor);
        Assert.Equal(0.0, own.ShowdownRate);

        var result = await new OpponentStatsTool(1, store)
            .ExecuteAsync(new OpponentStatsRequest(), Context, CancellationToken.None);

        Assert.Equal(-1, result.Seat);
        Assert.Equal(new[] { 0, 2, 3 }, result.Opponents.ToArray());
        Assert.Equal(6, result.Statistics.Hands);
        Assert.Equal(2.0 / 3.0, result.Statistics.Vpip, 12);
        Assert.Equal(1.0 / 3.0, result.Statistics.Pfr, 12);
        Assert.Equal(0.8, result.Statistics.AggressionFactor, 12);
        Assert.Equal(2.0 / 3.0, result.Statistics.ShowdownRate, 12);

        // Cross-check the aggregate against the recorded actions, independently of the store's rates.
        var records = new[]
        {
            PlayScriptedHand(13579UL, 4, 1, (seat, observation) => policies[seat](observation)),
            PlayScriptedHand(97531UL, 4, 2, (seat, observation) => policies[seat](observation)),
        };
        var opponentActions = records
            .SelectMany(record => record.Actions)
            .Where(action => action.Seat != 1)
            .ToArray();
        var aggressive = opponentActions.Count(
            action => action.Kind is "Bet" or "Raise" or "AllIn");
        var calls = opponentActions.Count(action => action.Kind == "Call");
        Assert.Equal(8, aggressive);
        Assert.Equal(10, calls);
        Assert.Equal(aggressive / (double)Math.Max(1, calls), result.Statistics.AggressionFactor, 12);
        Assert.Equal(
            opponentActions.Count(action => action.Kind == "Call" && action.Seat == 2),
            calls);

        var history = await new HandHistoryTool(2, store)
            .ExecuteAsync(new HandHistoryRequest(10), Context, CancellationToken.None);
        Assert.Equal(2, history.Seat);
        Assert.Equal(new[] { 1, 2 }, history.Hands.Select(hand => hand.HandNumber).ToArray());
        Assert.Equal(
            records[1].HoleCards[2].Select(card => card.ToString()).ToArray(),
            history.Hands[1].OwnHoleCards);
    }

    private static PokerAction CheckOrCall(PlayerObservation observation) =>
        observation.LegalActionKinds.Contains(ActionKind.Call)
            ? PokerAction.Call()
            : PokerAction.Check();

    private static PokerAction RaiseOrBet(PlayerObservation observation)
    {
        if (observation.LegalActionKinds.Contains(ActionKind.Bet))
        {
            return PokerAction.BetTo(observation.MinRaiseTo);
        }

        if (observation.LegalActionKinds.Contains(ActionKind.Raise))
        {
            return PokerAction.RaiseTo(observation.MinRaiseTo);
        }

        return CheckOrCall(observation);
    }

    private static PokerAction FoldOrCheck(PlayerObservation observation) =>
        observation.LegalActionKinds.Contains(ActionKind.Fold)
            ? PokerAction.Fold()
            : PokerAction.Check();

    private static HandRecord PlayScriptedHand(
        ulong seed,
        int seats,
        int handNumber,
        Func<int, PlayerObservation, PokerAction> decide)
    {
        var state = HoldemHandEngine.Start(
            seed,
            seats,
            handNumber,
            startingStack: 1000,
            smallBlind: 5,
            bigBlind: 10,
            buttonSeat: 0);
        var actions = 0;
        while (!state.IsComplete)
        {
            var seat = state.ActingSeat;
            var observation = PlayerObservation.ForSeat(state, seat);
            state = HoldemHandEngine.Apply(state, decide(seat, observation));
            actions++;
            if (actions > 500)
            {
                throw new InvalidOperationException("The scripted hand did not complete within 500 actions.");
            }
        }

        return HandRecord.From(state);
    }
}
