using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wip.Tholdem.Agents;
using Wip.Tholdem.Agents.Baselines;
using Wip.Tholdem.Agents.Simulation;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;
using Xunit;

namespace Wip.Tholdem.Tests;

internal enum HandRecordSchema
{
    V1,
    V2,
}

internal sealed record GeneratedHistorySessions(
    string RootDirectory,
    string SessionsDirectory,
    string V1SessionDirectory,
    string TwoSeatV2SessionDirectory,
    string FourSeatV2SessionDirectory,
    int V1HandCount,
    int TwoSeatV2HandCount,
    int FourSeatV2HandCount)
{
    public int V2HandCount => TwoSeatV2HandCount + FourSeatV2HandCount;
}

/// <summary>
/// Builds the history fixtures the compatibility tests read. Every hand is produced by the public
/// <see cref="MatchSimulator.Run(IReadOnlyList{IPokerAgent}, SimulationOptions, CancellationToken)"/>
/// surface into a scratch directory beside the compiled test assembly, so the tests never depend on
/// git-ignored operator data that is not part of the checkout.
/// </summary>
internal static class HistorySessionFixture
{
    private const ulong V1Seed = 42UL;

    private const ulong TwoSeatV2Seed = 777UL;

    private const ulong FourSeatV2Seed = 9001UL;

    /// <summary>
    /// Runs three sessions into one sessions root with the recorded layout
    /// <c>sessions/&lt;session&gt;/hand-NNNNN.json</c>: a two-seat equal-stack session (the product
    /// selects v1 for it), a two-seat unequal-stack session (v2 by the unequal stacks) and a
    /// four-seat session (v2 by seat count). All shapes are produced by the public
    /// <see cref="MatchSimulator.Run(IReadOnlyList{IPokerAgent}, SimulationOptions, CancellationToken)"/>
    /// surface with explicit per-seat starting stacks.
    /// </summary>
    public static GeneratedHistorySessions CreateSessionsRoot(
        int v1Hands = 10,
        int twoSeatV2Hands = 3,
        int fourSeatV2Hands = 5)
    {
        if (v1Hands < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(v1Hands));
        }

        if (twoSeatV2Hands < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(twoSeatV2Hands));
        }

        if (fourSeatV2Hands < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(fourSeatV2Hands));
        }

        var root = Path.Combine(
            Path.GetDirectoryName(typeof(HistorySessionFixture).Assembly.Location)!,
            "test-temp",
            "tholdem-history-sessions-" + Guid.NewGuid().ToString("N"));
        var sessions = Path.Combine(root, "sessions");
        var v1 = Path.Combine(sessions, "heads-up-v1");
        var twoSeatV2 = Path.Combine(sessions, "heads-up-unequal-v2");
        var fourSeatV2 = Path.Combine(sessions, "four-seat-v2");
        Directory.CreateDirectory(v1);
        Directory.CreateDirectory(twoSeatV2);
        Directory.CreateDirectory(fourSeatV2);

        MatchSimulator.Run(
            new RandomLegalAgent("history-v1-seat-0"),
            new RandomLegalAgent("history-v1-seat-1"),
            new SimulationOptions(
                V1Seed,
                v1Hands,
                HistoryDirectory: v1,
                StartingStacks: new long[] { 1000, 1000 }));

        MatchSimulator.Run(
            new IPokerAgent[]
            {
                new RandomLegalAgent("history-two-seat-v2-seat-0"),
                new RandomLegalAgent("history-two-seat-v2-seat-1"),
            },
            new SimulationOptions(
                TwoSeatV2Seed,
                twoSeatV2Hands,
                HistoryDirectory: twoSeatV2,
                StartingStacks: new long[] { 1000, 600 }));

        MatchSimulator.Run(
            new IPokerAgent[]
            {
                new RandomLegalAgent("history-v2-seat-0"),
                new RandomLegalAgent("history-v2-seat-1"),
                new RandomLegalAgent("history-v2-seat-2"),
                new RandomLegalAgent("history-v2-seat-3"),
            },
            new SimulationOptions(
                FourSeatV2Seed,
                fourSeatV2Hands,
                HistoryDirectory: fourSeatV2,
                StartingStacks: new long[] { 1000, 1000, 1000, 1000 }));

        return new GeneratedHistorySessions(
            root,
            sessions,
            v1,
            twoSeatV2,
            fourSeatV2,
            v1Hands,
            twoSeatV2Hands,
            fourSeatV2Hands);
    }

    public static string[] EnumerateHandFiles(string sessionsDirectory) =>
        Directory
            .EnumerateFiles(sessionsDirectory, "hand-*.json", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

    public static HandRecordSchema ReadSchema(string handPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(handPath));
        var root = document.RootElement;
        if (!root.TryGetProperty("schema", out var schema))
        {
            return HandRecordSchema.V1;
        }

        var value = schema.GetString();
        return value switch
        {
            "tholdem.hand.v2" => HandRecordSchema.V2,
            _ => throw new InvalidOperationException(
                $"Unrecognized hand schema '{value}' in {handPath}."),
        };
    }

    /// <summary>
    /// Parses one hand file from disk, checks its canonical round-trip and SHA-256 fingerprint,
    /// applies the seat-count rule for its schema, and replays it to the recorded result.
    /// </summary>
    public static HandRecord VerifyHandFile(string handPath)
    {
        var bytes = File.ReadAllBytes(handPath);
        Assert.False(
            bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            $"{handPath} must be UTF-8 without a BOM.");
        var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
            .GetString(bytes);
        var schema = ReadSchema(handPath);
        var record = HandRecordJson.Parse(text);

        Assert.Equal(text, record.ToCanonicalJson());
        Assert.Equal(Sha256Hex(bytes), record.Hash);
        Assert.Equal(record.Hash, Sha256Hex(new UTF8Encoding(false).GetBytes(text)));
        AssertSeatCountMatchesSchema(schema, record);

        var replayed = HoldemHandEngine.Replay(record);
        Assert.Equal(record.StartingStacks.Sum(), record.FinalStacks.Sum());
        Assert.Equal(record.FinalStacks.ToArray(), replayed.Stacks.ToArray());
        Assert.Equal(record.Winners.ToArray(), replayed.Winners.ToArray());
        Assert.Equal(record.Showdown, replayed.ShowdownReached);
        Assert.Equal(record.PotAwarded, replayed.AwardedPot);

        if (schema == HandRecordSchema.V2)
        {
            Assert.Equal(record.Pots.Count, replayed.Pots.Count);
            for (var pot = 0; pot < record.Pots.Count; pot++)
            {
                Assert.Equal(record.Pots[pot].Amount, replayed.Pots[pot].Amount);
                Assert.Equal(record.Pots[pot].Winners.ToArray(), replayed.Pots[pot].Winners.ToArray());
            }
        }

        return record;
    }

    /// <summary>
    /// Applies the product's schema invariant, which the canonical writer derives from the record:
    /// v1 is a two-seat hand whose starting stacks are all equal; v2 covers two to six seats, and a
    /// two-seat v2 hand must have unequal starting stacks, because equal stacks select v1.
    /// </summary>
    public static void AssertSeatCountMatchesSchema(HandRecordSchema schema, HandRecord record)
    {
        Assert.Equal(record.SeatCount, record.StartingStacks.Count);

        switch (schema)
        {
            case HandRecordSchema.V1:
                Assert.Equal(2, record.SeatCount);
                AssertStartingStacksEqual(record);
                break;
            case HandRecordSchema.V2:
                Assert.InRange(record.SeatCount, 2, 6);
                if (record.SeatCount == 2)
                {
                    Assert.NotEqual(record.StartingStacks[0], record.StartingStacks[1]);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(schema), schema, "Unknown hand schema.");
        }
    }

    private static void AssertStartingStacksEqual(HandRecord record)
    {
        Assert.NotEmpty(record.StartingStacks);
        var first = record.StartingStacks[0];
        Assert.All(record.StartingStacks, stack => Assert.Equal(first, stack));
    }

    public static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
