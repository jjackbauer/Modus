using System.Globalization;
using Wip.Tholdem.Agents.Evaluation;
using Wip.Tholdem.Agents.Learning;
using Xunit;

namespace Wip.Tholdem.Tests.Evaluation;

/// <summary>
/// Round-seven equal-budget proposer ablation contract: every arm spends exactly the same candidate
/// budget against the same task start and selection plan, candidates stay inside the genome domain,
/// the no-search control is exactly inert, reruns are byte-identical, and invalid options are refused
/// before any simulation.
/// </summary>
public sealed class SearchAblationTests
{
    private static readonly string[] Arms = { "NoSearch", "Random", "Grid", "Greedy" };

    [Fact]
    public void Run_GivenOneTask_EveryArmEvaluatesTheSameCandidateCount()
    {
        var report = SearchAblation.Run(TinyOptions());

        Assert.Equal("tholdem.search-ablation.v1", report.Schema);
        Assert.Equal(new[] { "tiny" }, report.TaskIds);
        Assert.Equal(16, report.Candidates.Count);
        foreach (var arm in Arms)
        {
            var candidates = report.Candidates
                .Where(candidate => candidate.Arm == arm)
                .OrderBy(candidate => candidate.Index)
                .ToArray();
            Assert.Equal(report.Budget.CandidatesPerArm, candidates.Length);
            Assert.Equal(new[] { 0, 1, 2, 3 }, candidates.Select(candidate => candidate.Index));
            Assert.All(candidates, candidate => Assert.Equal("tiny", candidate.TaskId));
        }

        Assert.Equal(4, report.Results.Count);
        Assert.Equal(Arms, report.Results.Select(result => result.Arm));
        Assert.All(report.Results, result => Assert.Equal("tiny", result.TaskId));

        Assert.Equal(4, report.Budget.CandidatesPerArm);
        Assert.Equal(2, report.Budget.SelectionBlocks);
        Assert.Equal(2, report.Budget.SelectionHandsPerMatch);
        Assert.Equal(2, report.Budget.HoldoutBlocks);
        Assert.Equal(2, report.Budget.HoldoutHandsPerMatch);
        Assert.Equal(4, report.Budget.RotationsPerBlock);
        Assert.Equal(64L, report.Budget.SelectionHandsPerArmPerTask);
        Assert.Equal(384L, report.Budget.TotalHands);

        foreach (var arm in Arms)
        {
            var means = report.Candidates
                .Where(candidate => candidate.Arm == arm)
                .OrderBy(candidate => candidate.Index)
                .Select(candidate => candidate.SelectionMeanBbPer100.ToString("R", CultureInfo.InvariantCulture));
            Console.WriteLine($"{arm} selection means: {string.Join(", ", means)}");
        }

        foreach (var result in report.Results)
        {
            Console.WriteLine(
                $"{result.Arm} best={result.BestSelectionMeanBbPer100:R} "
                + $"holdout={result.HoldoutMeanBbPer100:R} "
                + $"lower={result.HoldoutLowerBoundBbPer100:R} "
                + $"verdict={result.Verdict} reason={result.Reason}");
        }

        Console.WriteLine($"outcome={report.Outcome}");
    }

    [Fact]
    public void Run_GivenNoSearchArm_SelectionMeansAreZero()
    {
        var report = SearchAblation.Run(TinyOptions());

        var noSearch = report.Candidates
            .Where(candidate => candidate.Arm == "NoSearch")
            .ToArray();
        Assert.Equal(report.Budget.CandidatesPerArm, noSearch.Length);
        Assert.All(noSearch, candidate => Assert.Equal(0.0, candidate.SelectionMeanBbPer100));
    }

    [Fact]
    public void Run_GivenCandidates_AreInsideGenomeDomain()
    {
        var report = SearchAblation.Run(TinyOptions());

        Assert.NotEmpty(report.Candidates);
        foreach (var candidate in report.Candidates)
        {
            var genome = candidate.Genome;
            Assert.InRange(genome.RaiseThreshold, 0.0, 1.0);
            Assert.InRange(genome.CallThreshold, 0.0, 1.0);
            Assert.InRange(genome.BluffFrequency, 0.0, 1.0);
            Assert.InRange(
                genome.SizingMultiplier,
                StrategyGenome.MinimumSizingMultiplier,
                StrategyGenome.MaximumSizingMultiplier);
        }
    }

    [Fact]
    public void Run_GivenSameOptions_IsByteIdentical()
    {
        var first = SearchAblation.Run(TinyOptions());
        var second = SearchAblation.Run(TinyOptions());

        Assert.Equal(first.ToCanonicalJson(), second.ToCanonicalJson());
        Assert.Equal(first.ReportHash, second.ReportHash);
        Assert.Equal(StrategyArtifact.ComputeHash(first.ToCanonicalJson()), first.ReportHash);
        Assert.Matches("^[0-9a-f]{64}$", first.ReportHash);
    }

    [Fact]
    public void Run_GivenInvalidOptions_Throws()
    {
        var options = TinyOptions();

        Assert.ThrowsAny<ArgumentException>(
            () => SearchAblation.Run(options with { CandidatesPerArm = 2 }));
        Assert.ThrowsAny<ArgumentException>(
            () => SearchAblation.Run(options with { Tasks = Array.Empty<SearchTask>() }));
        Assert.ThrowsAny<ArgumentException>(
            () => SearchAblation.Run(options with
            {
                Tasks = new[]
                {
                    new SearchTask("tiny", options.Tasks[0].Start, "missing-composition"),
                },
            }));
    }


    [Fact]
    public void ClassifyOutcome_GivenTwoGreedyImprovementsAndControlsBelow_ReturnsBenefit()
    {
        var results = new[]
        {
            Result("NoSearch", "Rejected", "task-a"),
            Result("Random", "VerifiedImprovement", "task-a"),
            Result("Grid", "VerifiedImprovement", "task-a"),
            Result("Greedy", "VerifiedImprovement", "task-a"),
            Result("Greedy", "VerifiedImprovement", "task-b"),
        };

        Assert.Equal("benefit", SearchAblation.ClassifyOutcome(results));
    }

    [Fact]
    public void ClassifyOutcome_GivenOneGreedyImprovement_ReturnsNoBenefit()
    {
        var results = new[]
        {
            Result("NoSearch", "Rejected", "task-a"),
            Result("Random", "Rejected", "task-a"),
            Result("Grid", "Rejected", "task-a"),
            Result("Greedy", "VerifiedImprovement", "task-a"),
        };

        Assert.Equal("no-benefit", SearchAblation.ClassifyOutcome(results));
    }

    [Fact]
    public void ClassifyOutcome_GivenGreedyImprovementButGreedyUnsafe_ReturnsNoBenefit()
    {
        var results = new[]
        {
            Result("NoSearch", "Rejected", "task-a"),
            Result("Random", "Rejected", "task-a"),
            Result("Grid", "Rejected", "task-a"),
            Result("Greedy", "VerifiedImprovement", "task-a"),
            Result("Greedy", "VerifiedImprovement", "task-b"),
            Result("Greedy", "Unsafe", "task-c"),
        };

        Assert.Equal("no-benefit", SearchAblation.ClassifyOutcome(results));
    }

    private static SearchArmResult Result(string arm, string verdict, string taskId) =>
        new(arm, taskId, new string('0', 64), 0.0, 0.0, 0.0, verdict, "synthetic");

    private static SearchAblationOptions TinyOptions() =>
        new(
            GameConfiguration.FourSeatCash(),
            FrozenOpponentPool.SelectionV1,
            new[]
            {
                new SearchTask("tiny", new StrategyGenome(0.72, 0.45, 0.12, 1.0), "primary-mixed"),
            },
            CandidatesPerArm: 4,
            SelectionBlocks: 2,
            SelectionHandsPerMatch: 2,
            HoldoutBlocks: 2,
            HoldoutHandsPerMatch: 2,
            MinimumPracticalEffectBbPer100: 5.0,
            ConfidenceLevel: 0.95,
            AnchorRegressionToleranceBbPer100: 5.0,
            Seed: 424242UL);
}
