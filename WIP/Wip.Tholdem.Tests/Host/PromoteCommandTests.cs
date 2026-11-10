using System.Text.Json;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Simulation;
using Xunit;

namespace Wip.Tholdem.Tests.Host;

public sealed class PromoteCommandTests
{
    private const string ChallengerId = "tholdem-strategy-v2-11111111";
    private const string IncumbentId = "tholdem-strategy-v1-22222222";

    [Fact]
    public void Run_GivenVerifyingEvaluationWithoutYes_ExpectedExit3AndUnchangedPointer()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-promote-no-yes");
        try
        {
            HostCliTestSupport.WriteCliEvaluation(
                dataDirectory,
                BuildReport(lowerBoundBbPer100: 40.0, seeds: 30),
                marginBbPer100: 5.0,
                minSeeds: 24);
            var pointerPath = HostCliTestSupport.WriteChampionPointer(dataDirectory, IncumbentId);
            var pointerBefore = File.ReadAllBytes(pointerPath);

            var result = HostCliTestSupport.Run(
                "promote",
                "--challenger",
                ChallengerId,
                "--incumbent",
                IncumbentId,
                "--data-dir",
                dataDirectory);

            Assert.Equal(3, result.ExitCode);
            Assert.Contains(
                "promotion requires explicit confirmation; re-run with --yes",
                result.Output,
                StringComparison.Ordinal);
            Assert.Equal(pointerBefore, File.ReadAllBytes(pointerPath));
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenVerifyingEvaluationWithYes_ExpectedExit0AndChallengerPointer()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-promote-yes");
        try
        {
            HostCliTestSupport.WriteCliEvaluation(
                dataDirectory,
                BuildReport(lowerBoundBbPer100: 40.0, seeds: 30),
                marginBbPer100: 5.0,
                minSeeds: 24);
            var pointerPath = HostCliTestSupport.WriteChampionPointer(dataDirectory, IncumbentId);

            var result = HostCliTestSupport.Run(
                "promote",
                "--challenger",
                ChallengerId,
                "--incumbent",
                IncumbentId,
                "--yes",
                "--data-dir",
                dataDirectory);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(string.Empty, result.Error);
            Assert.Contains("promoted", result.Output, StringComparison.OrdinalIgnoreCase);

            using var pointer = JsonDocument.Parse(File.ReadAllBytes(pointerPath));
            Assert.Equal(ChallengerId, pointer.RootElement.GetProperty("strategyId").GetString());
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenBelowMarginEvaluationWithYes_ExpectedExit3AndUnchangedPointer()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-promote-below");
        try
        {
            HostCliTestSupport.WriteCliEvaluation(
                dataDirectory,
                BuildReport(lowerBoundBbPer100: -40.0, seeds: 30),
                marginBbPer100: 5.0,
                minSeeds: 24);
            var pointerPath = HostCliTestSupport.WriteChampionPointer(dataDirectory, IncumbentId);
            var pointerBefore = File.ReadAllBytes(pointerPath);

            var result = HostCliTestSupport.Run(
                "promote",
                "--challenger",
                ChallengerId,
                "--incumbent",
                IncumbentId,
                "--yes",
                "--data-dir",
                dataDirectory);

            Assert.Equal(3, result.ExitCode);
            Assert.Contains("lcb-below-margin", result.Output, StringComparison.Ordinal);
            Assert.Equal(pointerBefore, File.ReadAllBytes(pointerPath));
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenMissingEvaluationFile_ExpectedExitCode4()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-promote-missing");
        try
        {
            var result = HostCliTestSupport.Run(
                "promote",
                "--challenger",
                ChallengerId,
                "--incumbent",
                IncumbentId,
                "--data-dir",
                dataDirectory);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains("evaluations", result.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static EvaluationReport BuildReport(double lowerBoundBbPer100, int seeds) =>
        new(
            ChallengerId,
            IncumbentId,
            seeds,
            HandsPerMatch: 10,
            BigBlind: 10,
            MeanBbPer100: lowerBoundBbPer100 + 10.0,
            BbPer100Ci: new ConfidenceInterval(lowerBoundBbPer100, lowerBoundBbPer100 + 20.0),
            LowerBoundBbPer100: lowerBoundBbPer100,
            ChallengerWins: 40,
            IncumbentWins: 10,
            Ties: 10,
            ChallengerNetChips: 5000);
}
