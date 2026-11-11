using System.Text.Json;
using Wip.Tholdem.Agents.Evaluation;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Learning.Refinement;
using Xunit;

namespace Wip.Tholdem.Tests.Evaluation;

/// <summary>
/// The additive four-seat training/promotion loop: an inert challenger must be refused at the
/// selection stage before any holdout spend, a rerun must be byte-identical, invalid options must
/// refuse before writing, and a run must stay inside the isolated <c>four-seat/</c> directory.
/// </summary>
public sealed class FourSeatTrainingTests
{
    [Fact]
    public void Run_GivenInertChallenger_RejectsAtSelectionAndLeavesChampion()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-four-seat-inert");
        var options = TinyOptions(dataDirectory) with
        {
            Proposer = (genome, _) => genome with
            {
                PromptText = "inert challenger: a prompt-only change cannot alter play",
            },
        };

        var run = FourSeatTraining.Run(options);

        var initialChampion = StrategyArtifact.Create(new StrategyGenome(), 0, null);
        Assert.Equal(initialChampion.Id, run.FinalChampionId);
        var iteration = Assert.Single(run.Iterations);
        Assert.Equal("rejected-selection", iteration.Outcome);
        Assert.Equal(0.0, iteration.SelectionMeanBbPer100);
        Assert.Equal(initialChampion.Id, iteration.ChampionId);
        Assert.NotEqual(initialChampion.Id, iteration.ChallengerId);
        Console.WriteLine(
            $"observed four-seat inert: outcome={iteration.Outcome} "
            + $"selectionMeanBbPer100={iteration.SelectionMeanBbPer100:R} "
            + $"verdict={iteration.Verdict} reason={iteration.Reason} "
            + $"finalChampionId={run.FinalChampionId} challengerId={iteration.ChallengerId} "
            + $"runHash={run.RunHash}");

        var strategies = Path.Combine(dataDirectory, FourSeatTraining.DirectoryName, "strategies");
        Assert.True(File.Exists(Path.Combine(strategies, initialChampion.Id + ".json")));
        Assert.True(File.Exists(Path.Combine(strategies, iteration.ChallengerId + ".json")));

        var reports = Path.Combine(dataDirectory, FourSeatTraining.DirectoryName, "reports");
        var selectionPath = Path.Combine(reports, "iter-1-selection.json");
        Assert.True(File.Exists(selectionPath));
        Assert.Contains(
            "\"schema\":\"tholdem.population-report.v2\"",
            File.ReadAllText(selectionPath),
            StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(reports, "iter-1-holdout.json")));
        Assert.False(File.Exists(Path.Combine(reports, "iter-1-decision.json")));

        Assert.False(
            File.Exists(
                Path.Combine(
                    dataDirectory,
                    FourSeatTraining.DirectoryName,
                    FourSeatTraining.ChampionFileName)));
        Assert.False(File.Exists(Path.Combine(dataDirectory, "active-champion.json")));
    }

    [Fact]
    public void Run_GivenSameOptions_IsByteIdentical()
    {
        var firstDirectory = AgentTestSupport.CreateTempDirectory("tholdem-four-seat-repeat-a");
        var secondDirectory = AgentTestSupport.CreateTempDirectory("tholdem-four-seat-repeat-b");

        var first = FourSeatTraining.Run(TinyOptions(firstDirectory));
        var second = FourSeatTraining.Run(TinyOptions(secondDirectory));

        Assert.Equal(first.RunHash, second.RunHash);
        Console.WriteLine(
            $"observed four-seat repeat: runHash={first.RunHash} "
            + $"files={RelativeFiles(firstDirectory).Length}");
        Assert.Equal(RelativeFiles(firstDirectory), RelativeFiles(secondDirectory));
        foreach (var relativePath in RelativeFiles(firstDirectory))
        {
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(firstDirectory, relativePath)),
                File.ReadAllBytes(Path.Combine(secondDirectory, relativePath)));
        }
    }

    [Fact]
    public void Run_GivenInvalidOptions_Throws()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-four-seat-invalid");

        Assert.Throws<ArgumentOutOfRangeException>(
            () => FourSeatTraining.Run(TinyOptions(dataDirectory) with { Iterations = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FourSeatTraining.Run(TinyOptions(dataDirectory) with { SelectionBlocks = 0 }));
        Assert.Throws<ArgumentException>(
            () => FourSeatTraining.Run(
                TinyOptions(dataDirectory) with
                {
                    Compositions = new[] { "missing-composition" },
                }));

        Assert.False(
            Directory.Exists(
                Path.Combine(dataDirectory, FourSeatTraining.DirectoryName)));
    }

    [Fact]
    public void Run_GivenOneIteration_WritesOnlyFourSeatArtifacts()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-four-seat-artifacts");
        var options = TinyOptions(dataDirectory) with
        {
            Proposer = (genome, _) => genome with { PromptText = "prompt-only challenger" },
        };

        var run = FourSeatTraining.Run(options);

        Assert.Single(run.Iterations);
        var relativeFiles = RelativeFiles(dataDirectory);
        Assert.NotEmpty(relativeFiles);
        foreach (var relativePath in relativeFiles)
        {
            Assert.StartsWith("four-seat/", relativePath, StringComparison.Ordinal);
            var bytes = File.ReadAllBytes(Path.Combine(dataDirectory, relativePath));
            Assert.False(
                bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                relativePath + " must not carry a UTF-8 BOM");
        }

        Console.WriteLine(
            "observed four-seat layout: " + string.Join(", ", relativeFiles));
        Assert.True(Directory.Exists(Path.Combine(dataDirectory, "four-seat", "strategies")));
        Assert.True(Directory.Exists(Path.Combine(dataDirectory, "four-seat", "reports")));
        Assert.False(File.Exists(Path.Combine(dataDirectory, "active-champion.json")));
        Assert.False(File.Exists(Path.Combine(dataDirectory, "ledger.json")));
        Assert.False(Directory.Exists(Path.Combine(dataDirectory, "strategies")));
        Assert.False(Directory.Exists(Path.Combine(dataDirectory, "evaluations")));
    }

    [Fact]
    public void Run_GivenDiagnosisSignatureSource_UsesDiagnosisProposal()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-four-seat-diagnosis-source");
        var options = TinyOptions(dataDirectory) with
        {
            DiagnosisDriven = true,
            SignatureSource = _ => new SessionSignatures(
            [
                new FailureSignal(FailureSignalNames.FoldToRaise, 10, 10, 1.0, FailureSignalDirections.High),
            ]),
        };

        var run = FourSeatTraining.Run(options);

        var iteration = Assert.Single(run.Iterations);
        var strategies = Path.Combine(dataDirectory, FourSeatTraining.DirectoryName, "strategies");
        var strategyPath = Path.Combine(strategies, iteration.ChallengerId + ".json");
        Assert.True(File.Exists(strategyPath));
        using var document = JsonDocument.Parse(File.ReadAllText(strategyPath));
        var callThreshold = document.RootElement
            .GetProperty("parameters")
            .GetProperty("callThreshold")
            .GetDouble();
        Assert.Equal(0.37, callThreshold, 12);

        var signaturesPath = Path.Combine(
            dataDirectory,
            FourSeatTraining.DirectoryName,
            "reports",
            "iter-1-signatures.json");
        Assert.True(File.Exists(signaturesPath));
        Assert.Contains(
            "\"schema\":\"tholdem.signatures.v1\"",
            File.ReadAllText(signaturesPath),
            StringComparison.Ordinal);
        Console.WriteLine(
            $"observed four-seat diagnosis source: challengerId={iteration.ChallengerId} "
            + $"callThreshold={callThreshold:R} signaturesReport=iter-1-signatures.json");
    }

    [Fact]
    public void Run_GivenDiagnosisCapture_WritesSignaturesAndProposes()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-four-seat-diagnosis-capture");
        var options = TinyOptions(dataDirectory) with
        {
            DiagnosisDriven = true,
            DiagnosisHands = 2,
        };

        var run = FourSeatTraining.Run(options);

        var iteration = Assert.Single(run.Iterations);
        var strategies = Path.Combine(dataDirectory, FourSeatTraining.DirectoryName, "strategies");
        Assert.True(File.Exists(Path.Combine(strategies, iteration.ChallengerId + ".json")));

        var reports = Path.Combine(dataDirectory, FourSeatTraining.DirectoryName, "reports");
        var signaturesPath = Path.Combine(reports, "iter-1-signatures.json");
        Assert.True(File.Exists(signaturesPath));
        var signaturesJson = File.ReadAllText(signaturesPath);
        Assert.Contains("\"schema\":\"tholdem.signatures.v1\"", signaturesJson, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(dataDirectory, "active-champion.json")));
        Console.WriteLine(
            $"observed four-seat diagnosis capture: challengerId={iteration.ChallengerId} "
            + $"signatures={signaturesJson}");
    }

    private static FourSeatTrainingOptions TinyOptions(string dataDirectory) =>
        new(
            Seed: 17UL,
            Iterations: 1,
            DataDirectory: dataDirectory,
            SelectionBlocks: 1,
            SelectionHandsPerMatch: 1,
            HoldoutBlocks: 1,
            HoldoutHandsPerMatch: 1);

    private static string[] RelativeFiles(string dataDirectory) =>
        Directory
            .GetFiles(dataDirectory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(dataDirectory, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
}
