using Wip.Tholdem.Engine;
using Wip.Tholdem.History;
using Xunit;

namespace Wip.Tholdem.Tests;

public sealed class MultiwayHistoryTests
{
    private const string CheckDownRecordHash =
        "c953464b065b991bcda5c550c3ff5167b7fef6ccbd417636e822cf1c58a842c5";

    [Fact]
    public void From_GivenTwoSeatHand_ExpectedV1CanonicalJsonAndBaselineHashUnchanged()
    {
        var record = HandRecord.From(EngineTestSupport.CheckDown(42));

        Assert.Equal(2, record.SeatCount);
        Assert.Equal(2, record.HoleCards.Count);
        Assert.NotEmpty(record.Pots);
        Assert.Equal(record.PotAwarded, record.Pots.Sum(pot => pot.Amount));

        var json = record.ToCanonicalJson();
        Assert.DoesNotContain("\"schema\"", json);
        Assert.DoesNotContain("\"startingStacks\"", json);
        Assert.StartsWith("{\"handNumber\":1,\"seed\":42,", json);
        Assert.Contains("\"holeCardsSeat0\":[\"7d\",\"9c\"]", json);
        Assert.Contains("\"holeCardsSeat1\":[\"Qc\",\"4s\"]", json);
        Assert.Equal(CheckDownRecordHash, record.Hash);
        Assert.Equal(json, HandRecordJson.Parse(json).ToCanonicalJson());
        Assert.Equal(record.Hash, HandRecordJson.Parse(json).Hash);
    }

    [Fact]
    public void From_GivenFourSeatHand_ExpectedV2JsonRoundTripHashAndReplay()
    {
        var finalState = MultiwayTestSupport.PlayRandom(9001, 4);
        Assert.True(finalState.IsComplete);

        var record = HandRecord.From(finalState);

        Assert.Equal(4, record.SeatCount);
        Assert.Equal(4, record.HoleCards.Count);
        Assert.NotEmpty(record.Pots);
        Assert.Equal(finalState.Pots.Count, record.Pots.Count);
        for (var seat = 0; seat < 4; seat++)
        {
            Assert.Equal(finalState.HoleCards[seat].ToArray(), record.HoleCards[seat].ToArray());
        }

        var json = record.ToCanonicalJson();
        Assert.StartsWith(
            "{\"schema\":\"tholdem.hand.v2\",\"handNumber\":1,\"seed\":9001,",
            json);
        Assert.Contains("\"buttonSeat\":", json);
        Assert.Contains(",\"seats\":4,\"holeCards\":[[", json);
        Assert.Contains(",\"pots\":[{\"amount\":", json);
        Assert.Contains(",\"finalStacks\":[", json);

        var parsed = HandRecordJson.Parse(json);

        Assert.Equal(record.SeatCount, parsed.SeatCount);
        Assert.Equal(json, parsed.ToCanonicalJson());
        Assert.Equal(record.Hash, parsed.Hash);
        Assert.Equal(record.HoleCards.Count, parsed.HoleCards.Count);
        for (var seat = 0; seat < 4; seat++)
        {
            Assert.Equal(record.HoleCards[seat].ToArray(), parsed.HoleCards[seat].ToArray());
        }

        Assert.Equal(record.Pots.Count, parsed.Pots.Count);
        for (var pot = 0; pot < record.Pots.Count; pot++)
        {
            Assert.Equal(record.Pots[pot].Amount, parsed.Pots[pot].Amount);
            Assert.Equal(record.Pots[pot].Winners.ToArray(), parsed.Pots[pot].Winners.ToArray());
        }

        var replayed = HoldemHandEngine.Replay(parsed);
        Assert.Equal(record.FinalStacks.ToArray(), replayed.Stacks.ToArray());
        Assert.Equal(record.Winners.ToArray(), replayed.Winners.ToArray());
        Assert.Equal(record.Showdown, replayed.ShowdownReached);
        Assert.Equal(record.PotAwarded, replayed.AwardedPot);
        Assert.Equal(
            record.Pots.Select(pot => pot.Amount).ToArray(),
            replayed.Pots.Select(pot => pot.Amount).ToArray());

        var replayedThroughWrapper = HeadsUpHandEngine.Replay(parsed);
        Assert.Equal(replayed.Stacks.ToArray(), replayedThroughWrapper.Stacks.ToArray());
        Assert.Equal(replayed.Winners.ToArray(), replayedThroughWrapper.Winners.ToArray());
    }

    [Fact]
    public void From_GivenFourSeatFold_ExpectedV2RoundTripAndReplay()
    {
        var state = HoldemHandEngine.Start(700, 4, buttonSeat: 0);
        state = HoldemHandEngine.Apply(state, PokerAction.RaiseTo(30));
        state = HoldemHandEngine.Apply(state, PokerAction.Fold());
        state = HoldemHandEngine.Apply(state, PokerAction.Fold());
        state = HoldemHandEngine.Apply(state, PokerAction.Fold());

        Assert.True(state.IsComplete);
        Assert.False(state.ShowdownReached);
        Assert.Equal(new[] { 2 }, state.Winners.ToArray());
        var award = Assert.Single(state.Pots);
        Assert.Equal(45L, award.Amount);
        Assert.Equal(new[] { 2 }, award.Winners.ToArray());

        var record = HandRecord.From(state);
        Assert.Equal(4, record.SeatCount);
        Assert.False(record.Showdown);
        var json = record.ToCanonicalJson();
        Assert.StartsWith(
            "{\"schema\":\"tholdem.hand.v2\",\"handNumber\":1,\"seed\":700,\"smallBlind\":5,"
            + "\"bigBlind\":10,\"startingStack\":1000,\"startingStacks\":[1000,1000,1000,1000],"
            + "\"buttonSeat\":0,\"seats\":4,\"holeCards\":[[",
            json);
        Assert.Contains(
            "],\"board\":[],\"actions\":[{\"street\":0,\"seat\":2,\"kind\":\"Raise\",\"amount\":30,\"potAfter\":45}",
            json);
        Assert.Contains(
            "],\"pots\":[{\"amount\":45,\"winners\":[2]}],\"showdown\":false,\"winners\":[2],"
            + "\"potAwarded\":45,\"finalStacks\":[",
            json);
        var parsed = HandRecordJson.Parse(json);

        Assert.Equal(record.Hash, parsed.Hash);
        Assert.Equal(json, parsed.ToCanonicalJson());
        var replayed = HoldemHandEngine.Replay(parsed);
        Assert.Equal(record.FinalStacks.ToArray(), replayed.Stacks.ToArray());
        Assert.Equal(record.Winners.ToArray(), replayed.Winners.ToArray());
        Assert.Equal(record.PotAwarded, replayed.AwardedPot);
        Assert.Equal(45L, replayed.Pots.Sum(pot => pot.Amount));
    }

    [Fact]
    public void From_GivenThreeSeatUnequalStacks_ExpectedV2StartingStacksRoundTripAndReplay()
    {
        var state = HoldemHandEngine.Start(
            424242,
            3,
            1,
            1000,
            5,
            10,
            0,
            new long[] { 1000, 600, 300 });
        state = HoldemHandEngine.Apply(state, PokerAction.AllIn());
        state = HoldemHandEngine.Apply(state, PokerAction.AllIn());
        state = HoldemHandEngine.Apply(state, PokerAction.AllIn());
        Assert.True(state.IsComplete);
        Assert.Equal(new long[] { 900, 600, 400 }, state.Pots.Select(pot => pot.Amount).ToArray());

        var record = HandRecord.From(state);

        Assert.Equal(3, record.SeatCount);
        Assert.Equal(new long[] { 1000, 600, 300 }, record.StartingStacks.ToArray());
        Assert.Equal(1000L, record.StartingStack);

        var json = record.ToCanonicalJson();
        Assert.StartsWith(
            "{\"schema\":\"tholdem.hand.v2\",\"handNumber\":1,\"seed\":424242,\"smallBlind\":5,"
            + "\"bigBlind\":10,\"startingStack\":1000,\"startingStacks\":[1000,600,300],"
            + "\"buttonSeat\":0,\"seats\":3,",
            json);

        var parsed = HandRecordJson.Parse(json);
        Assert.Equal(new long[] { 1000, 600, 300 }, parsed.StartingStacks.ToArray());
        Assert.Equal(json, parsed.ToCanonicalJson());
        Assert.Equal(record.Hash, parsed.Hash);

        var replayed = HoldemHandEngine.Replay(parsed);
        Assert.Equal(record.FinalStacks.ToArray(), replayed.Stacks.ToArray());
        Assert.Equal(record.Winners.ToArray(), replayed.Winners.ToArray());
        Assert.Equal(record.PotAwarded, replayed.AwardedPot);
        Assert.Equal(
            record.Pots.Select(pot => pot.Amount).ToArray(),
            replayed.Pots.Select(pot => pot.Amount).ToArray());
        Assert.Equal(new long[] { 1000, 600, 300 }, replayed.StartingStacks.ToArray());
    }

    [Fact]
    public void From_GivenTwoSeatUnequalStacks_ExpectedV2StartingStacksRoundTripAndReplay()
    {
        var state = HoldemHandEngine.Start(
            5150,
            2,
            1,
            1000,
            5,
            10,
            0,
            new long[] { 1000, 600 });
        state = HoldemHandEngine.Apply(state, PokerAction.AllIn());
        state = HoldemHandEngine.Apply(state, PokerAction.AllIn());
        Assert.True(state.IsComplete);

        var record = HandRecord.From(state);

        Assert.Equal(2, record.SeatCount);
        Assert.Equal(new long[] { 1000, 600 }, record.StartingStacks.ToArray());
        Assert.Equal(1000L, record.StartingStack);

        var json = record.ToCanonicalJson();
        Assert.StartsWith("{\"schema\":\"tholdem.hand.v2\",", json);
        Assert.Contains("\"startingStacks\":[1000,600]", json);
        Assert.DoesNotContain("\"holeCardsSeat0\"", json);

        var parsed = HandRecordJson.Parse(json);
        Assert.Equal(2, parsed.SeatCount);
        Assert.Equal(new long[] { 1000, 600 }, parsed.StartingStacks.ToArray());
        Assert.Equal(json, parsed.ToCanonicalJson());
        Assert.Equal(record.Hash, parsed.Hash);

        var replayed = HoldemHandEngine.Replay(parsed);
        Assert.Equal(record.FinalStacks.ToArray(), replayed.Stacks.ToArray());
        Assert.Equal(record.Winners.ToArray(), replayed.Winners.ToArray());
        Assert.Equal(record.Showdown, replayed.ShowdownReached);
        Assert.Equal(record.PotAwarded, replayed.AwardedPot);
    }

    [Fact]
    public void Parse_GivenV2MalformedSeatCount_ExpectedFormatException()
    {
        var finalState = MultiwayTestSupport.PlayRandom(9002, 3);
        var json = HandRecord.From(finalState).ToCanonicalJson();

        Assert.Throws<FormatException>(() => HandRecordJson.Parse(json.Replace(
            "\"seats\":3",
            "\"seats\":6",
            StringComparison.Ordinal)));
    }
}
