using System.Text.Json;
using Wip.Tholdem.Agents.Learning;
using Xunit;

namespace Wip.Tholdem.Tests.Host;

public sealed class EvaluateCommandTests
{
    [Fact]
    public void Run_GivenArtifactsAndChampionPointer_ExpectedCanonicalCliFileAndDecision()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-evaluate");
        try
        {
            var incumbent = StrategyArtifact.Create(
                new StrategyGenome(
                    RaiseThreshold: 0.60,
                    CallThreshold: 0.40,
                    BluffFrequency: 0.10,
                    SizingMultiplier: 1.0,
                    PromptText: "incumbent prompt"),
                iteration: 0,
                parent: null);
            var challenger = StrategyArtifact.Create(
                new StrategyGenome(
                    RaiseThreshold: 0.80,
                    CallThreshold: 0.55,
                    BluffFrequency: 0.20,
                    SizingMultiplier: 1.5,
                    PromptText: "challenger prompt"),
                iteration: 1,
                parent: incumbent);
            HostCliTestSupport.WriteStrategy(dataDirectory, incumbent);
            HostCliTestSupport.WriteStrategy(dataDirectory, challenger);
            var pointerPath = HostCliTestSupport.WriteChampionPointer(dataDirectory, incumbent.Id);
            var pointerBefore = File.ReadAllBytes(pointerPath);

            var result = HostCliTestSupport.Run(
                "evaluate",
                "--challenger",
                challenger.Id,
                "--seeds",
                "2",
                "--hands",
                "2",
                "--seed",
                "123",
                "--allow-weak-seed",
                "--data-dir",
                dataDirectory);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(string.Empty, result.Error);

            var evaluationPath = Path.Combine(
                dataDirectory,
                "evaluations",
                "cli-" + challenger.Id + "-vs-" + incumbent.Id + ".json");
            Assert.True(File.Exists(evaluationPath), $"Missing CLI evaluation file: {evaluationPath}");
            var text = File.ReadAllText(evaluationPath);
            Assert.DoesNotContain("\n", text);
            Assert.StartsWith(
                "{\"schema\":\"tholdem.cli-evaluation.v1\",\"criteria\":{\"marginBbPer100\":5,\"minSeeds\":24},\"report\":{\"challengerId\":\""
                    + challenger.Id
                    + "\"",
                text);

            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            Assert.Equal("tholdem.cli-evaluation.v1", root.GetProperty("schema").GetString());
            Assert.Equal(5.0, root.GetProperty("criteria").GetProperty("marginBbPer100").GetDouble());
            Assert.Equal(24, root.GetProperty("criteria").GetProperty("minSeeds").GetInt32());
            var report = root.GetProperty("report");
            Assert.Equal(challenger.Id, report.GetProperty("challengerId").GetString());
            Assert.Equal(incumbent.Id, report.GetProperty("incumbentId").GetString());
            Assert.Equal(2, report.GetProperty("seeds").GetInt32());
            Assert.Equal(2, report.GetProperty("handsPerMatch").GetInt32());
            Assert.True(double.IsFinite(report.GetProperty("meanBbPer100").GetDouble()));
            Assert.True(double.IsFinite(report.GetProperty("lowerBoundBbPer100").GetDouble()));

            Assert.Contains("decision:", result.Output, StringComparison.Ordinal);
            Assert.Contains("verifier:", result.Output, StringComparison.Ordinal);

            // --json emits the same canonical document that was persisted.
            var jsonResult = HostCliTestSupport.Run(
                "evaluate",
                "--challenger",
                challenger.Id,
                "--seeds",
                "2",
                "--hands",
                "2",
                "--seed",
                "123",
                "--allow-weak-seed",
                "--json",
                "--data-dir",
                dataDirectory);
            Assert.Equal(0, jsonResult.ExitCode);
            using (var jsonDocument = JsonDocument.Parse(jsonResult.Output.Trim()))
            {
                Assert.Equal("tholdem.cli-evaluation.v1", jsonDocument.RootElement.GetProperty("schema").GetString());
                Assert.Equal(challenger.Id, jsonDocument.RootElement.GetProperty("report").GetProperty("challengerId").GetString());
            }

            // Evaluating is not promoting: the champion pointer is byte-identical after the run.
            Assert.Equal(pointerBefore, File.ReadAllBytes(pointerPath));
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenMissingChallengerArtifact_ExpectedExitCode4()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-evaluate-missing");
        try
        {
            var result = HostCliTestSupport.Run(
                "evaluate",
                "--challenger",
                "tholdem-strategy-v1-00000000",
                "--seeds",
                "2",
                "--data-dir",
                dataDirectory);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains("tholdem-strategy-v1-00000000", result.Error, StringComparison.Ordinal);
            Assert.Contains("strategies", result.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }
}
