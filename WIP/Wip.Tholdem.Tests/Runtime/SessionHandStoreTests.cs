using System.Globalization;
using System.Text.Json;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Tholdem.Agents.Runtime;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;
using Xunit;

namespace Wip.Tholdem.Tests.Runtime;

public sealed class SessionHandStoreTests
{
    private const ulong Seed1 = 987654321UL;
    private const ulong Seed2 = 987654322UL;
    private const ulong Seed3 = 987654323UL;

    [Fact]
    public async Task BuildFor_GivenThreePlayedHands_ExpectedNewestLastViewsWithoutHiddenInformation()
    {
        var store = new SessionHandStore();
        var records = new[]
        {
            RaiseCallToShowdown(Seed1, 1),
            RaiseFoldPreflop(Seed2, 2),
            CallBetCallToShowdown(Seed3, 3),
        };

        foreach (var record in records)
        {
            store.Record(record);
        }

        var seat0Views = store.BuildFor(0, 10);
        var seat1Views = store.BuildFor(1, 10);

        Assert.Equal(3, seat0Views.Count);
        Assert.Equal(3, seat1Views.Count);
        Assert.Equal(new[] { 1, 2, 3 }, seat0Views.Select(view => view.HandNumber).ToArray());
        Assert.Equal(new[] { 1, 2, 3 }, seat1Views.Select(view => view.HandNumber).ToArray());
        Assert.Equal(new[] { 2, 3 }, store.BuildFor(0, 2).Select(view => view.HandNumber).ToArray());
        Assert.Empty(store.BuildFor(0, 0));

        for (var index = 0; index < records.Length; index++)
        {
            var record = records[index];
            foreach (var (seat, views) in new[] { (0, seat0Views), (1, seat1Views) })
            {
                var view = views[index];
                Assert.Equal(record.HandNumber, view.HandNumber);
                var own = seat == 0 ? record.HoleCardsSeat0 : record.HoleCardsSeat1;
                var opponent = seat == 0 ? record.HoleCardsSeat1 : record.HoleCardsSeat0;

                Assert.Equal(own.Select(card => card.ToString()).ToArray(), view.OwnHoleCards);
                Assert.Equal(record.Board.Select(card => card.ToString()).ToArray(), view.Board);
                Assert.Equal(record.Actions.Count, view.Actions.Count);
                Assert.Equal(record.Winners.ToArray(), view.Winners);
                Assert.Equal(record.Showdown, view.Showdown);
                Assert.Equal(record.PotAwarded, view.PotAwarded);
                Assert.Equal(record.FinalStacks[seat] - record.StartingStack, view.NetChips);
                Assert.True(view.Actions.Count > 0);

                var json = JsonSerializer.Serialize(view);
                foreach (var card in opponent)
                {
                    Assert.DoesNotContain($"\"{card}\"", json, StringComparison.Ordinal);
                }

                Assert.DoesNotContain(record.Seed.ToString(CultureInfo.InvariantCulture), json, StringComparison.Ordinal);
                Assert.DoesNotContain("seed", json, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("deck", json, StringComparison.OrdinalIgnoreCase);
            }
        }

        Assert.Equal(8, seat0Views[0].Actions.Count);
        Assert.Equal("Preflop seat0 Raise 20", seat0Views[0].Actions[0]);
        Assert.Equal("Flop seat1 Check 0", seat0Views[0].Actions[2]);
        Assert.Equal(8, seat1Views[2].Actions.Count);
        Assert.Equal("Preflop seat0 Call 0", seat1Views[2].Actions[0]);
        Assert.Equal("Flop seat1 Bet 10", seat1Views[2].Actions[2]);

        var context = new CapabilityContext(new SessionId("tool-contract-test"), AppContext.BaseDirectory);
        var history = await new HandHistoryTool(0, store)
            .ExecuteAsync(new HandHistoryRequest(5), context, CancellationToken.None);
        Assert.Equal(0, history.Seat);
        Assert.Equal(new[] { 1, 2, 3 }, history.Hands.Select(hand => hand.HandNumber).ToArray());

        var limited = await new HandHistoryTool(1, store)
            .ExecuteAsync(new HandHistoryRequest(2), context, CancellationToken.None);
        Assert.Equal(1, limited.Seat);
        Assert.Equal(new[] { 2, 3 }, limited.Hands.Select(hand => hand.HandNumber).ToArray());

        var opponentStats = await new OpponentStatsTool(0, store)
            .ExecuteAsync(new OpponentStatsRequest(), context, CancellationToken.None);
        Assert.Equal(1, opponentStats.Seat);
        Assert.Equal(3, opponentStats.Statistics.Hands);
        Assert.Equal(2.0 / 3.0, opponentStats.Statistics.Vpip, 3);
        Assert.Equal(2.0, opponentStats.Statistics.AggressionFactor, 3);
    }

    [Fact]
    public void BuildStatisticsFor_GivenCraftedHands_ExpectedHandAppropriateRates()
    {
        var store = new SessionHandStore();
        Assert.Equal(0, store.BuildStatisticsFor(0).Hands);

        store.Record(RaiseCallToShowdown(Seed1, 1));
        store.Record(RaiseFoldPreflop(Seed2, 2));
        store.Record(CallBetCallToShowdown(Seed3, 3));

        var seat0 = store.BuildStatisticsFor(0);
        Assert.Equal(3, seat0.Hands);
        Assert.Equal(1.0, seat0.Vpip, 3);
        Assert.Equal(2.0 / 3.0, seat0.Pfr, 3);
        Assert.Equal(1.0, seat0.AggressionFactor, 3);
        Assert.Equal(2.0 / 3.0, seat0.ShowdownRate, 3);

        var seat1 = store.BuildStatisticsFor(1);
        Assert.Equal(3, seat1.Hands);
        Assert.Equal(2.0 / 3.0, seat1.Vpip, 3);
        Assert.Equal(1.0 / 3.0, seat1.Pfr, 3);
        Assert.Equal(2.0, seat1.AggressionFactor, 3);
        Assert.Equal(2.0 / 3.0, seat1.ShowdownRate, 3);
    }

    private static HandRecord RaiseCallToShowdown(ulong seed, int handNumber)
    {
        var state = HeadsUpHandEngine.Start(seed, handNumber: handNumber);
        state = HeadsUpHandEngine.Apply(state, PokerAction.RaiseTo(20));
        state = HeadsUpHandEngine.Apply(state, PokerAction.Call());
        state = CheckDown(state);
        Assert.True(state.IsComplete);
        Assert.True(state.ShowdownReached);
        return HandRecord.From(state);
    }

    private static HandRecord RaiseFoldPreflop(ulong seed, int handNumber)
    {
        var state = HeadsUpHandEngine.Start(seed, handNumber: handNumber);
        state = HeadsUpHandEngine.Apply(state, PokerAction.RaiseTo(20));
        state = HeadsUpHandEngine.Apply(state, PokerAction.RaiseTo(40));
        state = HeadsUpHandEngine.Apply(state, PokerAction.Fold());
        Assert.True(state.IsComplete);
        Assert.False(state.ShowdownReached);
        return HandRecord.From(state);
    }

    private static HandRecord CallBetCallToShowdown(ulong seed, int handNumber)
    {
        var state = HeadsUpHandEngine.Start(seed, handNumber: handNumber);
        state = HeadsUpHandEngine.Apply(state, PokerAction.Call());
        state = HeadsUpHandEngine.Apply(state, PokerAction.Check());
        state = HeadsUpHandEngine.Apply(state, PokerAction.BetTo(10));
        state = HeadsUpHandEngine.Apply(state, PokerAction.Call());
        state = CheckDown(state);
        Assert.True(state.IsComplete);
        Assert.True(state.ShowdownReached);
        return HandRecord.From(state);
    }

    private static TableState CheckDown(TableState state)
    {
        while (!state.IsComplete)
        {
            var legal = HeadsUpHandEngine.GetLegalActions(state);
            Assert.True(legal.CanCheck, $"A check must be legal on {state.Street} while checking down.");
            state = HeadsUpHandEngine.Apply(state, PokerAction.Check());
        }

        return state;
    }
}
