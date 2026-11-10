using System.Security.Cryptography;
using System.Text;
using Wip.Tholdem.Cards;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;
using Xunit;

namespace Wip.Tholdem.Tests;

public sealed class HandRecordTests
{
    [Fact]
    public void From_GivenCompleteHand_ExpectedFieldsMatchFinalState()
    {
        var finalState = EngineTestSupport.PlayAllInHand(42);
        var record = HandRecord.From(finalState);

        Assert.Equal(1, record.HandNumber);
        Assert.Equal(42UL, record.Seed);
        Assert.Equal(5L, record.SmallBlind);
        Assert.Equal(10L, record.BigBlind);
        Assert.Equal(1000L, record.StartingStack);
        Assert.Equal(0, record.ButtonSeat);
        Assert.Equal(new[] { Card.Parse("7d"), Card.Parse("9c") }, record.HoleCardsSeat0.ToArray());
        Assert.Equal(new[] { Card.Parse("Qc"), Card.Parse("4s") }, record.HoleCardsSeat1.ToArray());
        Assert.Equal(
            new[]
            {
                Card.Parse("8c"), Card.Parse("7h"), Card.Parse("2s"), Card.Parse("3d"), Card.Parse("5c"),
            },
            record.Board.ToArray());
        Assert.Equal(2, record.Actions.Count);

        Assert.Equal(0, record.Actions[0].Street);
        Assert.Equal(0, record.Actions[0].Seat);
        Assert.Equal("AllIn", record.Actions[0].Kind);
        Assert.Equal(0L, record.Actions[0].Amount);
        Assert.Equal(1010L, record.Actions[0].PotAfter);

        Assert.Equal(0, record.Actions[1].Street);
        Assert.Equal(1, record.Actions[1].Seat);
        Assert.Equal("Call", record.Actions[1].Kind);
        Assert.Equal(0L, record.Actions[1].Amount);
        Assert.Equal(0L, record.Actions[1].PotAfter);

        Assert.True(record.Showdown);
        Assert.Equal(new[] { 0 }, record.Winners.ToArray());
        Assert.Equal(2000L, record.PotAwarded);
        Assert.Equal(new long[] { 2000, 0 }, record.FinalStacks.ToArray());
    }

    [Fact]
    public void From_GivenIncompleteState_ExpectedInvalidOperationException()
    {
        var state = HeadsUpHandEngine.Start(42);

        var exception = Assert.Throws<InvalidOperationException>(
            () => { _ = HandRecord.From(state); });

        Assert.Contains("complete", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToCanonicalJson_GivenRecord_ExpectedStableWhitespaceFreeJsonAndIndependentHash()
    {
        var record = EngineTestSupport.CheckDownRecord(42);
        var json = record.ToCanonicalJson();
        var second = record.ToCanonicalJson();

        Assert.Equal(json, second);
        Assert.DoesNotContain("\r", json);
        Assert.DoesNotContain("\n", json);
        Assert.DoesNotContain("\t", json);
        Assert.DoesNotContain(": ", json);
        Assert.DoesNotContain(" ", json);
        Assert.DoesNotContain("hash", json, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("{\"handNumber\":1,\"seed\":42,\"smallBlind\":5,\"bigBlind\":10,\"startingStack\":1000,\"buttonSeat\":0,", json);
        Assert.Contains("\"holeCardsSeat0\":[\"7d\",\"9c\"]", json);
        Assert.Contains("\"holeCardsSeat1\":[\"Qc\",\"4s\"]", json);
        Assert.Contains("\"board\":[\"8c\",\"7h\",\"2s\",\"3d\",\"5c\"]", json);
        Assert.Contains(
            "\"actions\":[{\"street\":0,\"seat\":0,\"kind\":\"Call\",\"amount\":0,\"potAfter\":20}",
            json);
        Assert.EndsWith("\"potAwarded\":20,\"finalStacks\":[1010,990]}", json);

        var expectedHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();

        Assert.Equal(expectedHash, record.Hash);
        Assert.Equal(64, record.Hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", record.Hash);
        Assert.Equal(record.Hash, record.Hash);
    }
}
