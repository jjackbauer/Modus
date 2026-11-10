using System.Text.Json;
using Xunit;

namespace Wip.Tholdem.Tests.Host;

public sealed class TrainCommandTests
{
    [Fact]
    public void Run_GivenOneIteration_ExpectedIterationLineFinalChampionLedgerAndPointer()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-train");
        try
        {
            var result = HostCliTestSupport.Run(
                "train",
                "--iterations",
                "1",
                "--seed",
                "7",
                "--allow-weak-seed",
                "--data-dir",
                dataDirectory);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(string.Empty, result.Error);

            var iterationLines = result.Output
                .Split('\n')
                .Select(static line => line.TrimEnd('\r'))
                .Where(static line => line.StartsWith("iteration=", StringComparison.Ordinal))
                .ToArray();
            var iterationLine = Assert.Single(iterationLines);
            Assert.Contains("iteration=1", iterationLine, StringComparison.Ordinal);
            Assert.Contains("champion=", iterationLine, StringComparison.Ordinal);
            Assert.Contains("challenger=", iterationLine, StringComparison.Ordinal);
            Assert.Contains("promoted=", iterationLine, StringComparison.Ordinal);
            Assert.Contains("verified=", iterationLine, StringComparison.Ordinal);
            Assert.Contains("outcome=", iterationLine, StringComparison.Ordinal);
            Assert.Contains("meanBbPer100=", iterationLine, StringComparison.Ordinal);
            Assert.Contains("lcb=", iterationLine, StringComparison.Ordinal);
            Assert.Contains("seeds=", iterationLine, StringComparison.Ordinal);
            Assert.Contains("final champion:", result.Output, StringComparison.Ordinal);

            var ledgerPath = Path.Combine(dataDirectory, "ledger.json");
            Assert.True(File.Exists(ledgerPath));
            var ledger = ReadLedger(ledgerPath);
            var entry = Assert.Single(ledger);
            Assert.Equal(1, entry.GetProperty("iteration").GetInt32());
            Assert.Equal("rejected", entry.GetProperty("outcome").GetString());

            var championId = entry.GetProperty("championId").GetString()!;
            Assert.Contains(championId, result.Output, StringComparison.Ordinal);

            // A fresh data directory records the version-1 champion it actually used.
            var pointerPath = Path.Combine(dataDirectory, "active-champion.json");
            Assert.True(File.Exists(pointerPath));
            using var pointer = JsonDocument.Parse(File.ReadAllText(pointerPath));
            Assert.Equal(championId, pointer.RootElement.GetProperty("strategyId").GetString());
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenTwoRunsIntoOneDirectory_ExpectedLedgerGrowsWithBothRunsEntries()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-train-ledger");
        try
        {
            Assert.Equal(
                0,
                HostCliTestSupport.Run(
                    "train", "--iterations", "1", "--seed", "7", "--allow-weak-seed", "--data-dir", dataDirectory).ExitCode);

            var ledgerPath = Path.Combine(dataDirectory, "ledger.json");
            var firstRun = ReadLedger(ledgerPath);
            var firstEntry = Assert.Single(firstRun);

            Assert.Equal(
                0,
                HostCliTestSupport.Run(
                    "train", "--iterations", "1", "--seed", "9", "--allow-weak-seed", "--data-dir", dataDirectory).ExitCode);

            var secondRun = ReadLedger(ledgerPath);
            Assert.Equal(2, secondRun.Length);
            Assert.Equal(
                firstEntry.GetProperty("challengerId").GetString(),
                secondRun[0].GetProperty("challengerId").GetString());
            Assert.NotEqual(
                secondRun[0].GetProperty("challengerId").GetString(),
                secondRun[1].GetProperty("challengerId").GetString());
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static JsonElement[] ReadLedger(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        return document.RootElement.EnumerateArray().Select(static entry => entry.Clone()).ToArray();
    }
}
