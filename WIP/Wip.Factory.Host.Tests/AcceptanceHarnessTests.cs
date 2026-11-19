using Wip.Factory.Host.Factory;
using Xunit;

namespace Wip.Factory.Host.Tests;

/// <summary>
/// FAC-P0-04 / K-07 / K-08: the evaluator owns the expected scenario set, refuses an uninspected contract, and
/// cannot pass on missing, empty, duplicated or differently-frozen evidence.
/// </summary>
public sealed class AcceptanceHarnessTests
{
    private const string ApprovedContract = "sha256:approved-contract";
    private const string HostDigest = "sha256:frozen-host";
    private const string ConfigDigest = "sha256:frozen-config";

    private static AcceptanceEvidence Evidence(
        IReadOnlyList<AcceptanceScenarioResult>? pre = null,
        IReadOnlyList<AcceptanceScenarioResult>? post = null,
        string contract = ApprovedContract,
        string host = HostDigest,
        string modelKind = "live",
        bool knownInput = false,
        bool blind = true,
        bool released = true)
        => new(
            "PA-01",
            host,
            ConfigDigest,
            "sha256:requirement",
            contract,
            modelKind,
            "deepseek",
            knownInput,
            blind,
            pre ?? DefaultScenarios(),
            post ?? DefaultScenarios(),
            released,
            "merged-sha",
            "merged-sha",
            OperationallyValidated: true,
            DateTimeOffset.UtcNow);

    private static IReadOnlyList<AcceptanceScenarioResult> DefaultScenarios()
        =>
        [
            new("AC-1", "pre-merge", true),
            new("AC-2", "pre-merge", true),
            new("AC-1", "post-merge", true),
            new("AC-2", "post-merge", true)
        ];

    private static AcceptanceExpectations Expectations(
        IReadOnlyList<string>? ids = null,
        string contract = ApprovedContract,
        string host = HostDigest,
        bool evaluatorOwned = true)
        => new(
            "PA-01",
            ids ?? ["AC-1", "AC-2"],
            contract,
            host,
            RequireLiveModel: true,
            EvaluatorOwned: evaluatorOwned,
            EvaluatorId: "evaluator-1");

    [Fact]
    public void Harness_GivenCompletePassingEvidence_AllowsPass()
    {
        var verdict = AcceptanceEvaluator.Evaluate(Evidence(), Expectations());

        Assert.True(verdict.Passed, string.Join(";", verdict.Reasons));
        Assert.Null(verdict.FailureCategory);
    }

    [Fact]
    public void Harness_GivenMissingScenarioResult_RefusesPass()
    {
        var scenarios = DefaultScenarios().Where(static scenario => scenario.Id != "AC-2").ToArray();

        var verdict = AcceptanceEvaluator.Evaluate(Evidence(scenarios, scenarios), Expectations());

        Assert.False(verdict.Passed);
        Assert.Contains("acceptance-scenario-missing:pre-merge:AC-2", verdict.Reasons);
        Assert.Contains("acceptance-scenario-missing:post-merge:AC-2", verdict.Reasons);
    }

    [Fact]
    public void Harness_GivenEmptyScenarioList_RefusesPass()
    {
        var verdict = AcceptanceEvaluator.Evaluate(
            Evidence([], []),
            Expectations(ids: []));

        Assert.False(verdict.Passed);
        Assert.Contains(AcceptanceEvaluator.ExpectationsEmpty, verdict.Reasons);
        Assert.Contains(AcceptanceEvaluator.ScenarioMissing + ":pre-merge:all", verdict.Reasons);
    }

    [Fact]
    public void Harness_GivenProposedContractDifferentFromEvaluatorExpectation_RefusesApproval()
    {
        var verdict = AcceptanceEvaluator.Evaluate(
            Evidence(contract: "sha256:model-generated-weaker-contract"),
            Expectations());

        Assert.False(verdict.Passed);
        Assert.Equal(AcceptanceEvaluator.ContractNotInspected, verdict.FailureCategory);
    }

    [Fact]
    public void Harness_GivenPostMergeScenarioNotRun_RefusesPass()
    {
        var verdict = AcceptanceEvaluator.Evaluate(
            Evidence(post: []),
            Expectations());

        Assert.False(verdict.Passed);
        Assert.Contains(AcceptanceEvaluator.ScenarioMissing + ":post-merge:all", verdict.Reasons);
    }

    [Fact]
    public void Harness_GivenDuplicateScenarioResult_RefusesPass()
    {
        var duplicated = DefaultScenarios().Append(new AcceptanceScenarioResult("AC-1", "post-merge", true)).ToArray();

        var verdict = AcceptanceEvaluator.Evaluate(Evidence(post: duplicated), Expectations());

        Assert.False(verdict.Passed);
        Assert.Contains(AcceptanceEvaluator.ScenarioDuplicate + ":post-merge", verdict.Reasons);
    }

    [Fact]
    public void Harness_GivenFactoryBinaryChangedBetweenBlindTasks_RefusesPA02()
    {
        var first = Evidence();
        var second = Evidence(host: "sha256:rebuilt-host");

        var verdict = AcceptanceEvaluator.EvaluateBlindPair(first, Expectations(), second, Expectations());

        Assert.False(verdict.Passed);
        Assert.Contains(AcceptanceEvaluator.HostChanged + ":between-tasks", verdict.Reasons);
    }

    [Fact]
    public void Harness_GivenUnchangedBinaryAndTwoBlindRuns_AllowsPair()
    {
        var second = Evidence() with { Gate = "PA-02" };
        var secondExpectations = Expectations() with { Gate = "PA-02" };

        var verdict = AcceptanceEvaluator.EvaluateBlindPair(Evidence(), Expectations(), second, secondExpectations);

        Assert.True(verdict.Passed, string.Join(";", verdict.Reasons));
    }

    [Fact]
    public void Harness_GivenKnownInputLabelledBlind_RefusesCertification()
    {
        var verdict = AcceptanceEvaluator.Evaluate(Evidence(knownInput: true, blind: true), Expectations());

        Assert.False(verdict.Passed);
        Assert.Contains(AcceptanceEvaluator.KnownInputLabelledBlind, verdict.Reasons);
    }

    [Fact]
    public void Harness_GivenDeterministicFakeModel_RefusesBlindPass()
    {
        var verdict = AcceptanceEvaluator.Evaluate(Evidence(modelKind: "mock"), Expectations());

        Assert.False(verdict.Passed);
        Assert.Contains(AcceptanceEvaluator.LiveModelMissing + ":mock", verdict.Reasons);
    }

    [Fact]
    public async Task Harness_GivenFailingEvidence_CliReturnsNonzero()
    {
        var directory = Path.Combine(Path.GetTempPath(), "factory-acceptance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var manifest = Path.Combine(directory, "evidence.json");
        var expectations = Path.Combine(directory, "expectations.json");

        await File.WriteAllTextAsync(manifest, System.Text.Json.JsonSerializer.Serialize(Evidence(post: []), AcceptanceJson.Options));
        await File.WriteAllTextAsync(expectations, System.Text.Json.JsonSerializer.Serialize(Expectations(), AcceptanceJson.Options));

        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await FactoryCli.ExecuteAsync(
            ["factory", "acceptance", "verify", "--manifest", manifest, "--expectations", expectations],
            output,
            error);

        Assert.Equal(FactoryCli.Refused, exit);
        Assert.Contains("acceptance: fail", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Harness_GivenPassingEvidence_CliReturnsZero()
    {
        var directory = Path.Combine(Path.GetTempPath(), "factory-acceptance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var manifest = Path.Combine(directory, "evidence.json");
        var expectations = Path.Combine(directory, "expectations.json");

        await File.WriteAllTextAsync(manifest, System.Text.Json.JsonSerializer.Serialize(Evidence(), AcceptanceJson.Options));
        await File.WriteAllTextAsync(expectations, System.Text.Json.JsonSerializer.Serialize(Expectations(), AcceptanceJson.Options));

        var exit = await FactoryCli.ExecuteAsync(
            ["factory", "acceptance", "verify", "--manifest", manifest, "--expectations", expectations],
            new StringWriter(),
            new StringWriter());

        Assert.Equal(FactoryCli.Ok, exit);
    }
}
