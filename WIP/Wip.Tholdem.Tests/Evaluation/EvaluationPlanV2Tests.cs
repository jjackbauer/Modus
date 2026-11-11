using Wip.Tholdem.Agents.Evaluation;
using Wip.Tholdem.Agents.Learning;
using Xunit;

namespace Wip.Tholdem.Tests.Evaluation;

/// <summary>
/// Round-one pre-registered evaluation plan: one protected seed domain per purpose, a faithful
/// embedding of the game contract and a stable content address over every declared setting.
/// </summary>
public sealed class EvaluationPlanV2Tests
{
    [Fact]
    public void Create_GivenValidDomains_DerivesSeedBlocksFromHoldout()
    {
        var plan = Plan(domains: new[]
        {
            new SeedDomain("audit", SeedPurpose.Audit, 400UL, 4),
            new SeedDomain("holdout", SeedPurpose.Holdout, 300UL, 12),
            new SeedDomain("training", SeedPurpose.Training, 100UL, 4),
            new SeedDomain("selection", SeedPurpose.Selection, 200UL, 4),
        });

        Assert.Equal(12, plan.SeedBlocks);
        Assert.Equal(12, plan.Holdout.Count);
        Assert.Equal(4, plan.Training.Count);
        Assert.Equal(4, plan.Selection.Count);
        Assert.Equal(4, plan.Audit.Count);
        Assert.Equal(
            new[]
            {
                SeedPurpose.Training, SeedPurpose.Selection, SeedPurpose.Holdout, SeedPurpose.Audit,
            },
            plan.Domains.Select(domain => domain.Purpose).ToArray());
        Assert.Equal("training", plan.Domains[0].DomainId);
    }

    [Fact]
    public void Create_GivenDuplicatePurpose_Throws()
    {
        var domains = DefaultDomains().ToList();
        domains.Add(new SeedDomain("holdout-extra", SeedPurpose.Holdout, 500UL, 4));

        Assert.Throws<InvalidOperationException>(() => Plan(domains: domains));
    }

    [Fact]
    public void Create_GivenMissingAuditDomain_Throws()
    {
        var domains = DefaultDomains()
            .Where(domain => domain.Purpose != SeedPurpose.Audit)
            .ToArray();

        Assert.Throws<InvalidOperationException>(() => Plan(domains: domains));
    }

    [Fact]
    public void Create_GivenEmptyCompositions_Throws()
    {
        Assert.Throws<ArgumentException>(() => Plan(compositions: Array.Empty<string>()));
    }

    [Fact]
    public void PlanHash_GivenSameInputs_Stable_AndChangesWithMargin()
    {
        var first = Plan();
        var second = Plan();
        var five = Plan(minimumPracticalEffectBbPer100: 5.0);
        var six = Plan(minimumPracticalEffectBbPer100: 6.0);

        Assert.Equal(first.PlanHash, second.PlanHash);
        Assert.Equal(64, first.PlanHash.Length);
        Assert.Equal(StrategyArtifact.ComputeHash(first.ToCanonicalJson()), first.PlanHash);
        Assert.NotEqual(five.PlanHash, six.PlanHash);
    }

    [Fact]
    public void ToCanonicalJson_GivenPlan_EmbedsGameObjectAndAllDomains()
    {
        var game = GameConfiguration.FourSeatCash();
        var plan = Plan();
        var json = plan.ToCanonicalJson();

        Assert.Contains("\"game\":" + game.ToCanonicalJson(), json);
        Assert.Contains("\"schema\":\"tholdem.evaluation-plan.v2\"", json);
        Assert.Contains("\"evaluatorVersion\":\"tholdem.population-evaluator.v2.0.0\"", json);
        Assert.Contains("\"opponentPoolId\":\"population-a\"", json);
        Assert.Contains("\"handsPerSeatMatch\":20", json);
        Assert.Contains("\"minimumPracticalEffectBbPer100\":2", json);
        Assert.Contains("\"confidenceLevel\":0.95", json);
        Assert.Contains("\"plannedCandidates\":5", json);
        Assert.Contains("\"anchorRegressionToleranceBbPer100\":2", json);
        foreach (var domain in plan.Domains)
        {
            Assert.Contains(domain.ToCanonicalJson(), json);
            Assert.Contains("\"" + domain.DomainId + "\"", json);
        }

        Assert.Equal(4, plan.Domains.Count);
        Assert.True(
            json.IndexOf("\"game\":", StringComparison.Ordinal)
            < json.IndexOf("\"domains\":", StringComparison.Ordinal));
    }

    private static EvaluationPlanV2 Plan(
        IReadOnlyList<SeedDomain>? domains = null,
        IReadOnlyList<string>? compositions = null,
        double minimumPracticalEffectBbPer100 = 2.0)
    {
        return EvaluationPlanV2.Create(
            GameConfiguration.FourSeatCash(),
            "population-a",
            compositions ?? new[] { "baseline", "candidate" },
            domains ?? DefaultDomains(),
            20,
            minimumPracticalEffectBbPer100,
            0.95,
            5,
            2.0);
    }

    private static SeedDomain[] DefaultDomains() =>
        new[]
        {
            new SeedDomain("training", SeedPurpose.Training, 100UL, 4),
            new SeedDomain("selection", SeedPurpose.Selection, 200UL, 4),
            new SeedDomain("holdout", SeedPurpose.Holdout, 300UL, 12),
            new SeedDomain("audit", SeedPurpose.Audit, 400UL, 4),
        };
}
