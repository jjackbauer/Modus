using Wip.Tholdem.Agents.Learning;
using Xunit;

namespace Wip.Tholdem.Tests.Host;

/// <summary>
/// The operator entry points refuse a guessable session seed unless the caller explicitly opts in to
/// a reproducible demo/test seed. The deal is deterministic from the session seed, so a small seed
/// is recoverable by enumerating candidates against an observer's own cards.
/// </summary>
public sealed class SessionSeedGuardTests
{
    public const string MinimumSeedText = "1099511627776";

    public const string WeakSeedMessage =
        "seed 42 is below the minimum 1099511627776; use --allow-weak-seed for reproducible demos";

    [Fact]
    public void SessionSeedPolicy_GivenBoundary_ExpectedStrongOnlyAtMinimum()
    {
        Assert.Equal(1_099_511_627_776UL, Wip.Tholdem.Host.SessionSeedPolicy.MinimumSeed);
        Assert.False(Wip.Tholdem.Host.SessionSeedPolicy.IsStrong(0));
        Assert.False(
            Wip.Tholdem.Host.SessionSeedPolicy.IsStrong(
                Wip.Tholdem.Host.SessionSeedPolicy.MinimumSeed - 1));
        Assert.True(Wip.Tholdem.Host.SessionSeedPolicy.IsStrong(Wip.Tholdem.Host.SessionSeedPolicy.MinimumSeed));
        Assert.True(Wip.Tholdem.Host.SessionSeedPolicy.IsStrong(ulong.MaxValue));
    }

    [Fact]
    public void Play_GivenWeakSeedWithoutAllowFlag_ExpectedExit2WithMinimumMessage()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("seed-guard-play");
        try
        {
            var result = HostCliTestSupport.Run(
                "play", "--seed", "42", "--hands", "1", "--data-dir", dataDirectory);

            Assert.Equal(2, result.ExitCode);
            Assert.Equal(string.Empty, result.Output);
            Assert.Contains(WeakSeedMessage, result.Error, StringComparison.Ordinal);
            Assert.Empty(HostCliTestSupport.SessionDirectories(dataDirectory));
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Play_GivenWeakSeedWithAllowFlag_ExpectedExit0AndSessionWritten()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("seed-guard-play-optin");
        try
        {
            var result = HostCliTestSupport.Run(
                "play", "--seed", "42", "--hands", "1", "--allow-weak-seed", "--data-dir", dataDirectory);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(string.Empty, result.Error);
            Assert.Single(HostCliTestSupport.SessionDirectories(dataDirectory));
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Play_GivenStrongSeedWithoutAllowFlag_ExpectedExit0()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("seed-guard-play-strong");
        try
        {
            var result = HostCliTestSupport.Run(
                "play", "--seed", MinimumSeedText, "--hands", "1", "--data-dir", dataDirectory);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(string.Empty, result.Error);
            Assert.Single(HostCliTestSupport.SessionDirectories(dataDirectory));
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Train_GivenWeakSeedWithoutAllowFlag_ExpectedExit2WithMinimumMessage()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("seed-guard-train");
        try
        {
            var result = HostCliTestSupport.Run(
                "train", "--iterations", "1", "--seed", "7", "--data-dir", dataDirectory);

            Assert.Equal(2, result.ExitCode);
            Assert.Equal(string.Empty, result.Output);
            Assert.Contains(
                "seed 7 is below the minimum 1099511627776; use --allow-weak-seed for reproducible demos",
                result.Error,
                StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(dataDirectory, "ledger.json")));
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Train_GivenWeakSeedWithAllowFlag_ExpectedExit0()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("seed-guard-train-optin");
        try
        {
            var result = HostCliTestSupport.Run(
                "train", "--iterations", "1", "--seed", "7", "--allow-weak-seed", "--data-dir", dataDirectory);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(string.Empty, result.Error);
            Assert.True(File.Exists(Path.Combine(dataDirectory, "ledger.json")));
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Train_GivenStrongSeedWithoutAllowFlag_ExpectedExit0()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("seed-guard-train-strong");
        try
        {
            var result = HostCliTestSupport.Run(
                "train", "--iterations", "1", "--seed", MinimumSeedText, "--data-dir", dataDirectory);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(string.Empty, result.Error);
            Assert.True(File.Exists(Path.Combine(dataDirectory, "ledger.json")));
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Evaluate_GivenWeakSeedWithoutAllowFlag_ExpectedExit2WithMinimumMessage()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("seed-guard-evaluate");
        try
        {
            var (challengerId, _) = WriteStrategyPair(dataDirectory);

            var result = HostCliTestSupport.Run(
                "evaluate",
                "--challenger",
                challengerId,
                "--seeds",
                "1",
                "--hands",
                "1",
                "--seed",
                "42",
                "--data-dir",
                dataDirectory);

            Assert.Equal(2, result.ExitCode);
            Assert.Equal(string.Empty, result.Output);
            Assert.Contains(WeakSeedMessage, result.Error, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(dataDirectory, "evaluations")));
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Evaluate_GivenWeakSeedWithAllowFlag_ExpectedExit0AndEvaluationWritten()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("seed-guard-evaluate-optin");
        try
        {
            var (challengerId, incumbentId) = WriteStrategyPair(dataDirectory);

            var result = HostCliTestSupport.Run(
                "evaluate",
                "--challenger",
                challengerId,
                "--seeds",
                "1",
                "--hands",
                "1",
                "--seed",
                "42",
                "--allow-weak-seed",
                "--data-dir",
                dataDirectory);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(string.Empty, result.Error);
            var evaluationPath = Path.Combine(
                dataDirectory,
                "evaluations",
                "cli-" + challengerId + "-vs-" + incumbentId + ".json");
            Assert.True(File.Exists(evaluationPath), $"Missing evaluation file: {evaluationPath}");
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Evaluate_GivenStrongSeedWithoutAllowFlag_ExpectedExit0()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("seed-guard-evaluate-strong");
        try
        {
            var (challengerId, incumbentId) = WriteStrategyPair(dataDirectory);

            var result = HostCliTestSupport.Run(
                "evaluate",
                "--challenger",
                challengerId,
                "--seeds",
                "1",
                "--hands",
                "1",
                "--seed",
                MinimumSeedText,
                "--data-dir",
                dataDirectory);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(string.Empty, result.Error);
            var evaluationPath = Path.Combine(
                dataDirectory,
                "evaluations",
                "cli-" + challengerId + "-vs-" + incumbentId + ".json");
            Assert.True(File.Exists(evaluationPath), $"Missing evaluation file: {evaluationPath}");
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static (string ChallengerId, string IncumbentId) WriteStrategyPair(string dataDirectory)
    {
        var incumbent = StrategyArtifact.Create(
            new StrategyGenome(
                RaiseThreshold: 0.60,
                CallThreshold: 0.40,
                BluffFrequency: 0.10,
                SizingMultiplier: 1.0,
                PromptText: "seed guard incumbent"),
            iteration: 0,
            parent: null);
        var challenger = StrategyArtifact.Create(
            new StrategyGenome(
                RaiseThreshold: 0.80,
                CallThreshold: 0.55,
                BluffFrequency: 0.20,
                SizingMultiplier: 1.5,
                PromptText: "seed guard challenger"),
            iteration: 1,
            parent: incumbent);
        HostCliTestSupport.WriteStrategy(dataDirectory, incumbent);
        HostCliTestSupport.WriteStrategy(dataDirectory, challenger);
        HostCliTestSupport.WriteChampionPointer(dataDirectory, incumbent.Id);
        return (challenger.Id, incumbent.Id);
    }
}
