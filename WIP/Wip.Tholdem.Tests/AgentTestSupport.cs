using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wip.Tholdem.Cards;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;

namespace Wip.Tholdem.Tests;

internal sealed record ManifestData(
    string Schema,
    string SessionId,
    ulong Seed,
    int Hands,
    long StartingStack,
    long SmallBlind,
    long BigBlind,
    string[] Agents,
    string[] HandHashes);

internal static class AgentTestSupport
{
    public static ulong FindSeedForHoleCards(int seat, string first, string second)
    {
        var expectedFirst = Card.Parse(first);
        var expectedSecond = Card.Parse(second);
        for (ulong seed = 1; seed < 200000; seed++)
        {
            var state = HeadsUpHandEngine.Start(seed);
            var hole = seat == 0 ? state.HoleCardsSeat0 : state.HoleCardsSeat1;
            if (hole.Count == 2 && hole[0] == expectedFirst && hole[1] == expectedSecond)
            {
                return seed;
            }
        }

        throw new InvalidOperationException(
            $"No seed below 200000 deals {first} {second} to seat {seat} in that order.");
    }

    public static string CreateTempDirectory(string prefix)
    {
        // The supplementary runner is launched as an apphost from inside the repository, and this
        // machine's sandbox denies apphost writes to %TEMP%. Test scratch therefore lives beside the
        // compiled test assembly (inside the workspace and outside source control).
        var path = Path.Combine(AppContext.BaseDirectory, "test-temp", prefix + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Wip.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root (Wip.slnx was not found above the test output).");
    }

    public static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static string[] HandFileNames(string directory) =>
        Directory.GetFiles(directory, "hand-*.json")
            .Select(static path => Path.GetFileName(path)!)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

    public static HandRecord ReadRecord(string directory, int handNumber) =>
        ParseRecord(File.ReadAllText(Path.Combine(directory, $"hand-{handNumber:D5}.json")));

    public static HandRecord[] ReadAllRecords(string directory) =>
        HandFileNames(directory)
            .Select(name => ParseRecord(File.ReadAllText(Path.Combine(directory, name))))
            .ToArray();

    public static int DistinctDeals(string directory) =>
        ReadAllRecords(directory)
            .Select(record => string.Join(
                "/",
                record.HoleCardsSeat0.Select(static card => card.ToString())
                    .Concat(record.HoleCardsSeat1.Select(static card => card.ToString()))))
            .Distinct(StringComparer.Ordinal)
            .Count();

    public static ManifestData ReadManifest(string directory)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "session.json")));
        var root = document.RootElement;
        return new ManifestData(
            root.GetProperty("schema").GetString()!,
            root.GetProperty("sessionId").GetString()!,
            root.GetProperty("seed").GetUInt64(),
            root.GetProperty("hands").GetInt32(),
            root.GetProperty("startingStack").GetInt64(),
            root.GetProperty("smallBlind").GetInt64(),
            root.GetProperty("bigBlind").GetInt64(),
            root.GetProperty("agents").EnumerateArray().Select(static item => item.GetString()!).ToArray(),
            root.GetProperty("handHashes").EnumerateArray().Select(static item => item.GetString()!).ToArray());
    }

    public static HandRecord ParseRecord(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var actions = new List<HandActionRecord>();
        foreach (var item in root.GetProperty("actions").EnumerateArray())
        {
            actions.Add(new HandActionRecord(
                item.GetProperty("street").GetInt32(),
                item.GetProperty("seat").GetInt32(),
                item.GetProperty("kind").GetString()!,
                item.GetProperty("amount").GetInt64(),
                item.GetProperty("potAfter").GetInt64()));
        }

        return new HandRecord(
            root.GetProperty("handNumber").GetInt32(),
            root.GetProperty("seed").GetUInt64(),
            root.GetProperty("smallBlind").GetInt64(),
            root.GetProperty("bigBlind").GetInt64(),
            root.GetProperty("startingStack").GetInt64(),
            root.GetProperty("buttonSeat").GetInt32(),
            Cards(root, "holeCardsSeat0"),
            Cards(root, "holeCardsSeat1"),
            Cards(root, "board"),
            actions,
            root.GetProperty("showdown").GetBoolean(),
            Ints(root, "winners"),
            root.GetProperty("potAwarded").GetInt64(),
            Longs(root, "finalStacks"));
    }

    public static string[] AgentSourceFiles()
    {
        var project = Path.Combine(RepositoryRoot(), "WIP", "Wip.Tholdem.Agents");
        return Directory
            .EnumerateFiles(Path.Combine(project, "Baselines"), "*.cs", SearchOption.AllDirectories)
            .Concat(new[]
            {
                Path.Combine(project, "IPokerAgent.cs"),
                Path.Combine(project, "AgentTurnContext.cs"),
                Path.Combine(project, "Learning", "StrategyAgent.cs"),
            })
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static Card[] Cards(JsonElement root, string name) =>
        root.GetProperty(name).EnumerateArray().Select(static item => Card.Parse(item.GetString()!)).ToArray();

    private static int[] Ints(JsonElement root, string name) =>
        root.GetProperty(name).EnumerateArray().Select(static item => item.GetInt32()).ToArray();

    private static long[] Longs(JsonElement root, string name) =>
        root.GetProperty(name).EnumerateArray().Select(static item => item.GetInt64()).ToArray();
}