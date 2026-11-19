using Wip.Abstractions.Identifiers;
using Wip.Factory.Contracts.Events;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Work;
using Wip.Factory.Core;
using Wip.Factory.Core.Fanout;
using Wip.Factory.Host.Factory;
using Wip.Factory.Wip.Governance;
using Xunit;

namespace Wip.Factory.Host.Tests;

/// <summary>
/// FAC-P1-07 / K-13: regressions and the frozen HTTP contract are complementary. A green contract cannot outvote a
/// failed pre-existing test, and a merged revision whose regressions fail is never released.
/// </summary>
public sealed class VerificationIntegrityTests
{
    private const string ContractJson = """
        {
          "schemaVersion": 1,
          "supportedClass": "aspnetcore-http-net10",
          "scenarios": [
            { "id": "AC-1", "method": "POST", "path": "/books",
              "request": { "json": { "title": "Dune" } },
              "expect": { "status": 201, "jsonSubset": { "title": "Dune" } } }
          ]
        }
        """;

    private sealed class StubBuild : ICandidateBuildVerifier
    {
        public ValueTask<(bool Ok, string Detail)> VerifyAsync(string worktreePath, CancellationToken cancellationToken = default)
            => ValueTask.FromResult((true, "build passed"));
    }

    private sealed class FailingRegression : IRegressionTestRunner
    {
        public ValueTask<RegressionTestResult> RunAsync(string worktreePath, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new RegressionTestResult(
                true, false, 1, "BooksTests.Get_Returns404 expected 200", "sha256:regression-failure"));
    }

    private sealed class NoRegression : IRegressionTestRunner
    {
        public ValueTask<RegressionTestResult> RunAsync(string worktreePath, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(RegressionTestResult.NotApplicable("sha256:no-existing-tests"));
    }

    private sealed class StubCandidateHost : ICandidateHost
    {
        public ValueTask<CandidateSession> StartAsync(string worktreePath, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new CandidateSession(new Uri("http://127.0.0.1:1/"), 6001, true, static () => ValueTask.CompletedTask));
    }

    private sealed class StubMerge : IMergeEngine
    {
        public int MergeCalls { get; private set; }

        public ValueTask<MergeResult> MergeAsync(string repositoryPath, string headSha, CancellationToken cancellationToken = default)
        {
            MergeCalls++;
            return ValueTask.FromResult(MergeResult.Ok_("merged-sha-1"));
        }

        public ValueTask<string> MaterialiseAsync(string repositoryPath, string mergedSha, string destination, CancellationToken cancellationToken = default)
        {
            FactoryTestWorkspace.Write(destination);
            return ValueTask.FromResult(destination);
        }
    }

    [Fact]
    public async Task Verification_GivenNoExistingTests_RecordsExplicitNotApplicable()
    {
        var worktree = Path.Combine(Path.GetTempPath(), "factory-notests-" + Guid.NewGuid().ToString("N"));
        FactoryTestWorkspace.Write(worktree);

        var result = await new DotnetRegressionTestRunner().RunAsync(worktree);

        Assert.False(result.Ran);
        Assert.True(result.Passed);
        Assert.Equal(0, result.Projects);
        Assert.StartsWith("not-applicable", result.Detail, StringComparison.Ordinal);
        Assert.StartsWith("sha256:", result.ArtifactDigest, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verification_GivenRegressionFailsButHttpPasses_Rejects()
    {
        var worktree = Path.Combine(Path.GetTempPath(), "factory-regress-" + Guid.NewGuid().ToString("N"));
        FactoryTestWorkspace.Write(worktree);

        var check = new ValidationReviewCheck(new StubBuild(), new FailingRegression());
        var context = new ReviewCheckContext(
            "W-1",
            worktree,
            "head-1",
            "base-1",
            AcceptanceContract.Parse(ContractJson).Contract!);

        var validation = await check.ExecuteAsync(context);
        Assert.False(validation.Passed);
        Assert.Contains("regressions=failed", validation.Detail, StringComparison.Ordinal);

        // Even with a green HTTP contract and green architecture/security, the review refuses.
        var executions = new[]
        {
            validation,
            Passed(ReviewCheckKind.RequirementCoverage, "head-1", context.Contract.Digest),
            Passed(ReviewCheckKind.ArchitecturalConformance, "head-1", context.Contract.Digest),
            Passed(ReviewCheckKind.Security, "head-1", context.Contract.Digest)
        };

        var review = new FactoryReviewService().EvaluateWithEvidence(
            new ReviewRequirement("W-1", "req-1", ["AC-1"]),
            new ReviewBaseline("head-1", "base-1", "main", "policy", "guidelines"),
            new Dictionary<string, string>(),
            executions,
            findings: [],
            exclusions: [],
            riskClassification: "alpha-standard",
            reviewerProfileVersion: new AgentProfileVersion(1),
            reviewerSessionId: new SessionId("review-1"),
            requiredHeadSha: "head-1",
            requiredContractDigest: context.Contract.Digest);

        Assert.False(review.Accepted);
        Assert.StartsWith(FactoryReviewService.EvidenceCheckFailedCategory + ":validation", review.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verification_GivenPostMergeRegressionsFail_RefusesRelease()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "factory-postregress-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(basePath, "target");
        Directory.CreateDirectory(repository);

        var merge = new StubMerge();
        var verifier = new PostMergeReleaseVerifier(
            merge,
            new StubBuild(),
            new FailingRegression(),
            new StubCandidateHost(),
            new AcceptanceContractRunner(),
            Path.Combine(basePath, "verify"));

        var contract = AcceptanceContract.Parse(ContractJson).Contract!;
        var verification = await verifier.VerifyAsync(new ReleaseVerificationRequest(
            "W-1", repository, "merged-sha-1", "head-1", "base-1", "op-1", contract));

        Assert.False(verification.Ok);
        Assert.StartsWith("regression-failed", verification.Detail, StringComparison.Ordinal);

        var ledger = new FactoryOperationLedger(Path.Combine(basePath, "control"));
        var handler = new FactoryStageDispatchHandler(
            candidates: new StubCandidateHost(),
            builds: new StubBuild(),
            merges: merge,
            verificationRoot: Path.Combine(basePath, "verify"),
            operations: ledger,
            regressions: new FailingRegression(),
            releaseVerifier: verifier);

        var snapshot = new WorkItemSnapshot(
            new WorkItemId("W-1"), FactoryWorkState.ReleaseCandidate, 5, repository, [], DateTimeOffset.UtcNow);

        var trigger = new FactoryEventEnvelope(
            new FactoryEventId("E-release"),
            OperatorEvents.CandidateReady,
            "test",
            "W-1",
            DateTimeOffset.UtcNow,
            null,
            null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["workItemId"] = "W-1",
                ["headSha"] = "head-1",
                ["baseSha"] = "base-1",
                ["requirementDigest"] = "sha256:requirement",
                ["acceptanceContract"] = ContractJson
            });

        var outcome = await handler.HandleAsync(snapshot, trigger);

        Assert.Equal(StageDispatchKind.Failed, outcome.Kind);
        Assert.DoesNotContain(outcome.Events, static e => e.Type == OperatorEvents.ReleaseCompleted);
        Assert.Equal(1, merge.MergeCalls);
    }

    private static ReviewCheckExecution Passed(ReviewCheckKind kind, string sha, string contractDigest)
        => new(
            kind,
            kind.ToString(),
            "test-checker",
            "1.0.0",
            sha,
            "policy://test@1",
            ReviewCheckExecution.PassedOutcome,
            contractDigest,
            "sha256:artifact-" + kind,
            DateTimeOffset.UtcNow,
            "ok");
}
