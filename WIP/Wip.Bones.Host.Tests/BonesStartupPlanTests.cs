using System.Text.Json;
using Wip.Bones.Host;
using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BonesStartupPlanTests
{
    [Fact]
    public void Compute_GivenCheckedInSafeDefaults_ExpectedPlanWithinCap()
    {
        var options = CreateSafeOptions();

        var plan = BonesStartupPlanner.Compute(options);

        Assert.Equal(66, plan.PlannedGamesPerIteration);
        Assert.True(plan.PlannedGamesPerIteration <= options.MaxGamesPerRun);
        Assert.Null(plan.CapWarning(options.MaxGamesPerRun));
    }

    [Fact]
    public void CapWarning_GivenUnboundedCap_ExpectedUnboundedWarning()
    {
        var options = CreateSafeOptions();
        options.MaxGamesPerRun = 0;

        var warning = BonesStartupPlanner.Compute(options).CapWarning(options.MaxGamesPerRun);

        Assert.NotNull(warning);
        Assert.Contains("UNBOUNDED", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void CapWarning_GivenPlanAboveCap_ExpectedPlanExceedsCapWarning()
    {
        var options = CreateSafeOptions();
        options.MaxGamesPerRun = 10;

        var warning = BonesStartupPlanner.Compute(options).CapWarning(options.MaxGamesPerRun);

        Assert.NotNull(warning);
        Assert.Contains("PLAN_EXCEEDS_CAP", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateOrThrow_GivenEffectiveZeroCapWithoutOptIn_ExpectedRefusal()
    {
        var options = CreateSafeOptions();
        options.MaxGamesPerRun = 0;
        options.AllowUnbounded = false;

        var exception = Assert.Throws<InvalidOperationException>(
            () => BonesStartupPlanner.ValidateOrThrow(options));

        Assert.Contains("MaxGamesPerRun", exception.Message, StringComparison.Ordinal);
        Assert.Contains("AllowUnbounded", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateOrThrow_GivenNegativeCapWithoutOptIn_ExpectedRefusal()
    {
        var options = CreateSafeOptions();
        options.MaxGamesPerRun = -1;
        options.AllowUnbounded = false;

        var exception = Assert.Throws<InvalidOperationException>(
            () => BonesStartupPlanner.ValidateOrThrow(options));

        Assert.Contains("MaxGamesPerRun=-1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateOrThrow_GivenEffectiveZeroCapWithOptIn_ExpectedAllowed()
    {
        var options = CreateSafeOptions();
        options.MaxGamesPerRun = 0;
        options.AllowUnbounded = true;

        BonesStartupPlanner.ValidateOrThrow(options);
    }

    [Fact]
    public void ValidateOrThrow_GivenPositiveCapWithoutOptIn_ExpectedAllowed()
    {
        var options = CreateSafeOptions();
        options.MaxGamesPerRun = 1;
        options.AllowUnbounded = false;

        BonesStartupPlanner.ValidateOrThrow(options);
    }

    [Fact]
    public void Describe_GivenSafeOptions_ExpectedPlanDiagnostics()
    {
        var lines = BonesStartupPlanner.Describe(CreateSafeOptions());

        Assert.Contains(lines, line => line.StartsWith("plan.gamesPerIteration=", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("plan.modelCallsPerIteration>=", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.StartsWith("plan.warning=", StringComparison.Ordinal));
    }

    [Fact]
    public void CheckedInAppSettings_GivenShippedConfiguration_ExpectedBoundedSafeDefaults()
    {
        var repositoryRoot = FindRepositoryRoot();
        var path = Path.Combine(repositoryRoot, "WIP", "Wip.Bones.Host", "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var bones = document.RootElement.GetProperty("BonesHost");

        Assert.True(bones.GetProperty("MaxGamesPerRun").GetInt32() > 0, "MaxGamesPerRun must be bounded by default.");
        Assert.False(bones.GetProperty("AllowUnbounded").GetBoolean(), "AllowUnbounded must default to false in checked-in configuration.");
        Assert.True(bones.GetProperty("MaxGamesPerRun").GetInt32() <= 500, "MaxGamesPerRun must stay small enough for bounded experiments.");
        Assert.InRange(bones.GetProperty("ParallelSessionCount").GetInt32(), 1, 2);
        Assert.InRange(bones.GetProperty("DefaultParameters").GetProperty("GameCount").GetInt32(), 1, 2);
        Assert.InRange(bones.GetProperty("DefaultParameters").GetProperty("TargetScore").GetInt32(), 12, 20);
        Assert.InRange(bones.GetProperty("Promotion").GetProperty("EvaluationMatchCount").GetInt32(), 1, 8);
        Assert.InRange(bones.GetProperty("Promotion").GetProperty("MaxConcurrentMatches").GetInt32(), 1, 4);
        Assert.InRange(bones.GetProperty("Promotion").GetProperty("MinimumCrossSeatCount").GetInt32(), 1, 1);
        Assert.Equal("ConfidenceComposite", bones.GetProperty("Promotion").GetProperty("GateMode").GetString());
        Assert.True(bones.GetProperty("Promotion").GetProperty("UsePairedEvaluationSeeds").GetBoolean());
    }

    private static BonesHostOptions CreateSafeOptions() => new()
    {
        MaxGamesPerRun = 200,
        ParallelSessionCount = 1,
        AllSeatsLearning = true,
        EvaluationTargetScore = 12,
        DefaultParameters = new Wip.Bones.Agents.Workflow.BonesLearningWorkflowParameters(
            GameCount: 1,
            Seed: 4242,
            TargetScore: 20,
            LearningPlayerId: new Wip.Bones.Identifiers.BonesPlayerId(1),
            EvaluationTargetScore: 12),
        Promotion = new Wip.Bones.Agents.Knowledge.BonesStrategyPromotionOptions
        {
            EvaluationMatchCount = 8,
            MaxConcurrentMatches = 4,
            MinimumCrossSeatCount = 1,
        },
    };

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Modus.slnx")))
                return current.FullName;
            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }
}
