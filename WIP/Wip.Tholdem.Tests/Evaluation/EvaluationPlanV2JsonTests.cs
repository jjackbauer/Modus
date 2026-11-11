using System.Globalization;
using Wip.Tholdem.Agents.Evaluation;
using Wip.Tholdem.Agents.Learning;
using Xunit;

namespace Wip.Tholdem.Tests.Evaluation;

/// <summary>
/// Round-thirteen persistence: the pre-registered plan crosses a file boundary as strict canonical
/// JSON. Whitespace, extra or missing members and a forged seed list are refused, while a semantic
/// margin change parses and changes the content address, so the hash (not the parser) is the
/// integrity mechanism a verifier compares.
/// </summary>
public sealed class EvaluationPlanV2JsonTests
{
    private const string NotCanonicalMessage =
        "plan-not-canonical: the plan is not in its canonical form.";

    private const string MissingAuditMessage =
        "plan-shape-invalid: the plan is missing the required 'Audit' seed domain.";

    [Fact]
    public void Parse_GivenCanonicalPlan_RoundTrips()
    {
        var plan = Plan();
        var json = EvaluationPlanV2Json.Write(plan);

        var parsed = EvaluationPlanV2Json.Parse(json);

        Assert.Equal(json, parsed.ToCanonicalJson());
        Assert.Equal(plan.PlanHash, parsed.PlanHash);
        Assert.Equal(64, parsed.PlanHash.Length);
        Assert.Equal(StrategyArtifact.ComputeHash(json), parsed.PlanHash);
    }

    [Fact]
    public void Parse_GivenWhitespaceOrExtraKey_RefusesAsNotCanonical()
    {
        var json = EvaluationPlanV2Json.Write(Plan());

        var whitespace = json.Replace("\"schema\":", "\"schema\": ", StringComparison.Ordinal);
        Assert.NotEqual(json, whitespace);
        var whitespaceException = Assert.Throws<FormatException>(
            () => EvaluationPlanV2Json.Parse(whitespace));
        Assert.Equal(NotCanonicalMessage, whitespaceException.Message);

        var extraKey = json[..^1] + ",\"unexpected\":true}";
        var extraKeyException = Assert.Throws<FormatException>(
            () => EvaluationPlanV2Json.Parse(extraKey));
        Assert.Equal(NotCanonicalMessage, extraKeyException.Message);
    }

    [Fact]
    public void Parse_GivenMissingDomain_RefusesAsShapeInvalid()
    {
        var plan = Plan();
        var json = EvaluationPlanV2Json.Write(plan);
        var auditJson = plan.Audit.ToCanonicalJson();
        var withoutAudit = json.Replace("," + auditJson, string.Empty, StringComparison.Ordinal);
        Assert.False(withoutAudit.Contains(auditJson, StringComparison.Ordinal));

        var exception = Assert.Throws<FormatException>(
            () => EvaluationPlanV2Json.Parse(withoutAudit));

        Assert.Equal(MissingAuditMessage, exception.Message);
    }

    [Fact]
    public void Parse_GivenStoredSeedMismatch_RefusesAsSeedMismatch()
    {
        var plan = Plan();
        var json = EvaluationPlanV2Json.Write(plan);
        var holdout = plan.Holdout;
        var stored = holdout.Seeds[0];
        var forged = stored ^ 1UL;
        var originalSeeds = "\"seeds\":[" + string.Join(",", holdout.Seeds.Select(Format)) + "]";
        var forgedSeeds = "\"seeds\":[" + Format(forged) + ","
            + string.Join(",", holdout.Seeds.Skip(1).Select(Format)) + "]";
        var forgedJson = json.Replace(originalSeeds, forgedSeeds, StringComparison.Ordinal);
        Assert.NotEqual(json, forgedJson);

        var exception = Assert.Throws<FormatException>(
            () => EvaluationPlanV2Json.Parse(forgedJson));

        Assert.Equal(
            $"plan-seed-mismatch: domain 'holdout' stored seed 0 is {Format(forged)}, "
            + $"but the derivation yields {Format(stored)}.",
            exception.Message);
    }

    [Fact]
    public void Parse_GivenSemanticallyChangedMargin_HasDifferentPlanHash()
    {
        var original = Plan();
        var changed = Plan(minimumPracticalEffectBbPer100: 5.0);
        var changedJson = EvaluationPlanV2Json.Write(changed);

        var parsed = EvaluationPlanV2Json.Parse(changedJson);

        Assert.Equal(changedJson, parsed.ToCanonicalJson());
        Assert.Equal(5.0, parsed.MinimumPracticalEffectBbPer100);
        Assert.Equal(changed.PlanHash, parsed.PlanHash);
        Assert.NotEqual(original.PlanHash, parsed.PlanHash);
    }

    private static EvaluationPlanV2 Plan(
        IReadOnlyList<SeedDomain>? domains = null,
        double minimumPracticalEffectBbPer100 = 2.0)
    {
        return EvaluationPlanV2.Create(
            GameConfiguration.FourSeatCash(),
            "population-a",
            new[] { "baseline", "candidate" },
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

    private static string Format(ulong seed) => seed.ToString(CultureInfo.InvariantCulture);
}
