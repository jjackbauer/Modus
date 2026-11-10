using System.Text;
using Xunit;

namespace Wip.Tholdem.Tests;

/// <summary>
/// Compatibility coverage for the hand-history files written by <c>MatchSimulator</c>. Every file
/// is generated into a scratch sessions root, so these tests are independent of git-ignored
/// operator data that is not part of the checkout.
/// </summary>
public sealed class HistorySessionCompatibilityTests
{
    [Fact]
    public void Parse_GivenGeneratedV1HandFile_ExpectedCanonicalHashVerifiesAndReplayMatches()
    {
        var generated = HistorySessionFixture.CreateSessionsRoot(
            v1Hands: 2,
            twoSeatV2Hands: 1,
            fourSeatV2Hands: 1);
        try
        {
            var handPaths = HistorySessionFixture.EnumerateHandFiles(generated.SessionsDirectory);
            var v1HandPath = handPaths.First(
                path => HistorySessionFixture.ReadSchema(path) == HandRecordSchema.V1);
            var text = File.ReadAllText(v1HandPath, Encoding.UTF8);

            Assert.DoesNotContain("\"schema\"", text, StringComparison.Ordinal);
            Assert.StartsWith("{\"handNumber\":1,", text, StringComparison.Ordinal);
            Assert.Contains("\"holeCardsSeat0\":[", text, StringComparison.Ordinal);
            Assert.Contains("\"holeCardsSeat1\":[", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\"holeCards\":[[", text, StringComparison.Ordinal);

            var record = HistorySessionFixture.VerifyHandFile(v1HandPath);

            Assert.Equal(2, record.SeatCount);
            Assert.Equal(2, record.HoleCards.Count);
            Assert.Equal(2, record.StartingStacks.Count);
            Assert.Equal(record.FinalStacks.Sum(), record.StartingStacks.Sum());
        }
        finally
        {
            Directory.Delete(generated.RootDirectory, recursive: true);
        }
    }

    [Fact]
    public void Parse_GivenGeneratedV1AndV2SessionsInOneRoot_ExpectedSchemaAwareVerificationOfEveryHand()
    {
        var generated = HistorySessionFixture.CreateSessionsRoot(
            v1Hands: 10,
            twoSeatV2Hands: 3,
            fourSeatV2Hands: 5);
        try
        {
            var handPaths = HistorySessionFixture.EnumerateHandFiles(generated.SessionsDirectory);
            Assert.NotEmpty(handPaths);

            var schemaByHand = handPaths
                .Select(path => (Path: path, Schema: HistorySessionFixture.ReadSchema(path)))
                .ToArray();
            Assert.Contains(schemaByHand, hand => hand.Schema == HandRecordSchema.V1);
            Assert.Contains(schemaByHand, hand => hand.Schema == HandRecordSchema.V2);

            var v1Count = 0;
            var twoSeatV2Count = 0;
            var fourSeatV2Count = 0;
            foreach (var hand in schemaByHand)
            {
                var record = HistorySessionFixture.VerifyHandFile(hand.Path);
                switch (hand.Schema)
                {
                    case HandRecordSchema.V1:
                        Assert.Equal(2, record.SeatCount);
                        v1Count++;
                        break;
                    case HandRecordSchema.V2 when record.SeatCount == 2:
                        Assert.NotEqual(record.StartingStacks[0], record.StartingStacks[1]);
                        twoSeatV2Count++;
                        break;
                    case HandRecordSchema.V2 when record.SeatCount == 4:
                        fourSeatV2Count++;
                        break;
                    case HandRecordSchema.V2:
                        throw new InvalidOperationException(
                            $"The scratch root must not contain a {record.SeatCount}-seat v2 hand: "
                            + hand.Path);
                    default:
                        throw new InvalidOperationException(
                            $"Unexpected schema {hand.Schema} for {hand.Path}.");
                }
            }

            Assert.True(
                v1Count > 0,
                "The scratch sessions root must contain a 2-seat equal-stack v1 hand file.");
            Assert.True(
                twoSeatV2Count > 0,
                "The scratch sessions root must contain a 2-seat unequal-stack v2 hand file.");
            Assert.True(
                fourSeatV2Count > 0,
                "The scratch sessions root must contain a 4-seat v2 hand file.");
            Assert.Equal(generated.V1HandCount, v1Count);
            Assert.Equal(generated.TwoSeatV2HandCount, twoSeatV2Count);
            Assert.Equal(generated.FourSeatV2HandCount, fourSeatV2Count);
            Assert.Equal(generated.V2HandCount, twoSeatV2Count + fourSeatV2Count);
        }
        finally
        {
            Directory.Delete(generated.RootDirectory, recursive: true);
        }
    }

    [Fact]
    public void Parse_GivenFourSeatV2HandInSessionsRoot_ExpectedV2VerificationWithoutV1OnlySeatExpectation()
    {
        var generated = HistorySessionFixture.CreateSessionsRoot(
            v1Hands: 2,
            twoSeatV2Hands: 1,
            fourSeatV2Hands: 3);
        try
        {
            var handPaths = HistorySessionFixture.EnumerateHandFiles(generated.FourSeatV2SessionDirectory);
            var v2HandPaths = handPaths
                .Where(path => HistorySessionFixture.ReadSchema(path) == HandRecordSchema.V2)
                .ToArray();
            Assert.Equal(generated.FourSeatV2HandCount, v2HandPaths.Length);

            var text = File.ReadAllText(v2HandPaths[0], Encoding.UTF8);
            Assert.StartsWith("{\"schema\":\"tholdem.hand.v2\",", text, StringComparison.Ordinal);

            var record = HistorySessionFixture.VerifyHandFile(v2HandPaths[0]);

            Assert.Equal(4, record.SeatCount);
            Assert.Equal(4, record.HoleCards.Count);
            Assert.Equal(4, record.StartingStacks.Count);
            Assert.Equal(4, record.FinalStacks.Count);
            Assert.NotEmpty(record.Pots);
            Assert.Equal(HandRecordSchema.V2, HistorySessionFixture.ReadSchema(v2HandPaths[0]));
        }
        finally
        {
            Directory.Delete(generated.RootDirectory, recursive: true);
        }
    }

    [Fact]
    public void Parse_GivenTwoSeatUnequalStackV2HandInSessionsRoot_ExpectedSharedVerificationAcceptsV2SeatCount()
    {
        var generated = HistorySessionFixture.CreateSessionsRoot(
            v1Hands: 2,
            twoSeatV2Hands: 3,
            fourSeatV2Hands: 1);
        try
        {
            var handPaths = HistorySessionFixture.EnumerateHandFiles(generated.SessionsDirectory)
                .Where(path => path.StartsWith(
                    generated.TwoSeatV2SessionDirectory + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(generated.TwoSeatV2HandCount, handPaths.Length);
            Assert.NotEmpty(handPaths);

            var handPath = handPaths[0];
            var text = File.ReadAllText(handPath, Encoding.UTF8);
            Assert.StartsWith("{\"schema\":\"tholdem.hand.v2\",", text, StringComparison.Ordinal);
            Assert.Contains("\"seats\":2,", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\"holeCardsSeat0\"", text, StringComparison.Ordinal);

            var record = HistorySessionFixture.VerifyHandFile(handPath);

            Assert.Equal(HandRecordSchema.V2, HistorySessionFixture.ReadSchema(handPath));
            Assert.Equal(2, record.SeatCount);
            Assert.Equal(2, record.HoleCards.Count);
            Assert.Equal(2, record.StartingStacks.Count);
            Assert.Equal(new long[] { 1000, 600 }, record.StartingStacks.ToArray());
            Assert.NotEqual(record.StartingStacks[0], record.StartingStacks[1]);
        }
        finally
        {
            Directory.Delete(generated.RootDirectory, recursive: true);
        }
    }
}
