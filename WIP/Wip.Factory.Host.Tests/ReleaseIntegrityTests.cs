using Wip.Factory.Contracts.Events;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Work;
using Wip.Factory.Core;
using Wip.Factory.Core.Fanout;
using Wip.Factory.Host.Factory;
using Xunit;

namespace Wip.Factory.Host.Tests;

/// <summary>
/// FAC-P0-01 / K-01 / K-02: a merge effect and a verified release are distinct durable facts. Replaying an applied
/// merge must resume verification, never publish a release, and never run the merge twice.
/// </summary>
public sealed class ReleaseIntegrityTests
{
    private const string ContractA = """
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

    private const string ContractB = """
        {
          "schemaVersion": 1,
          "supportedClass": "aspnetcore-http-net10",
          "scenarios": [
            { "id": "AC-1", "method": "POST", "path": "/books",
              "request": { "json": { "title": "Dune" } },
              "expect": { "status": 201, "jsonSubset": { "title": "Dune", "id": "b1" } } }
          ]
        }
        """;

    private static readonly string Requirement = """
        ---
        title: Book catalog HTTP API
        supportedClass: aspnetcore-http-net10
        ---

        Create an HTTP JSON API to add books.

        ## Acceptance expectations
        - Creating a book returns 201 with an identifier.
        """;

    private sealed class StubBuild : ICandidateBuildVerifier
    {
        public ValueTask<(bool Ok, string Detail)> VerifyAsync(string worktreePath, CancellationToken cancellationToken = default)
            => ValueTask.FromResult((true, "build passed"));
    }

    private sealed class StubCandidateHost : ICandidateHost
    {
        public ValueTask<CandidateSession> StartAsync(string worktreePath, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new CandidateSession(new Uri("http://127.0.0.1:1/"), 4242, true, static () => ValueTask.CompletedTask));
    }

    private sealed class StubMerge : IMergeEngine
    {
        public int MergeCalls { get; private set; }

        public int ContainsCalls { get; private set; }

        public bool ContainsResult { get; set; } = true;

        public ValueTask<MergeResult> MergeAsync(string repositoryPath, string headSha, CancellationToken cancellationToken = default)
        {
            MergeCalls++;
            return ValueTask.FromResult(MergeResult.Ok_("merged-sha-1"));
        }

        public ValueTask<string> MaterialiseAsync(string repositoryPath, string mergedSha, string destination, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(destination);

        public ValueTask<bool> ContainsAsync(string repositoryPath, string mergedSha, CancellationToken cancellationToken = default)
        {
            ContainsCalls++;
            return ValueTask.FromResult(ContainsResult);
        }

        public ValueTask<string?> ResolveTreeAsync(string repositoryPath, string mergedSha, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>("tree-sha-1");
    }

    private sealed class FakeVerifier : IReleaseVerifier
    {
        public int Calls { get; private set; }

        public bool Succeed { get; set; } = true;

        public bool Throw { get; set; }

        public ValueTask<ReleaseVerificationResult> VerifyAsync(
            ReleaseVerificationRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Throw)
            {
                throw new InvalidOperationException("simulated process kill after merge");
            }

            var detail = Succeed
                ? "post-merge-contract-passed"
                : "post-merge-contract-failed:AC-1:status-mismatch";

            return ValueTask.FromResult(new ReleaseVerificationResult(
                Succeed,
                detail,
                "sha256:build-evidence",
                "sha256:regression-evidence",
                "sha256:contract-evidence",
                Succeed ? "AC-1=pass" : "AC-1=fail",
                "fake-verifier@1",
                "tree-sha-1"));
        }
    }

    private sealed record Setup(
        string BasePath,
        string Repository,
        FactoryOperationLedger Ledger,
        ReleaseVerificationStore Store,
        WorkItemSnapshot Snapshot,
        FactoryEventEnvelope Trigger);

    private static Setup Create(string contract = ContractA, string headSha = "head-sha-1")
    {
        var basePath = Path.Combine(Path.GetTempPath(), "factory-release-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(basePath, "target");
        Directory.CreateDirectory(repository);

        var verificationRoot = Path.Combine(basePath, "verify");
        var ledger = new FactoryOperationLedger(Path.Combine(basePath, "control"));
        var store = new ReleaseVerificationStore(Path.Combine(verificationRoot, "release-verifications.log"));

        var workItemId = new WorkItemId("WORK-release");
        var snapshot = new WorkItemSnapshot(
            workItemId, FactoryWorkState.ReleaseCandidate, 7, repository, [], DateTimeOffset.UtcNow);

        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["workItemId"] = workItemId.Value,
            ["headSha"] = headSha,
            ["baseSha"] = "base-sha-1",
            ["requirementDigest"] = "sha256:requirement",
            ["acceptanceContract"] = contract
        };

        var trigger = new FactoryEventEnvelope(
            new FactoryEventId("E-release-1"),
            OperatorEvents.CandidateReady,
            "test",
            workItemId.Value,
            DateTimeOffset.UtcNow,
            null,
            null,
            data);

        return new Setup(basePath, repository, ledger, store, snapshot, trigger);
    }

    private static FactoryStageDispatchHandler Handler(
        Setup setup,
        StubMerge merge,
        FakeVerifier verifier)
        => new(
            implementations: null,
            candidates: new StubCandidateHost(),
            builds: new StubBuild(),
            merges: merge,
            verificationRoot: Path.Combine(setup.BasePath, "verify"),
            operations: setup.Ledger,
            releaseVerifier: verifier);

    private static string OperationId(Setup setup)
        => FactoryOperationLedger.DeriveOperationId(
            setup.Snapshot.WorkItemId.Value,
            setup.Trigger.Id.Value,
            "release",
            setup.Trigger.Id.Value,
            1,
            "git-merge",
            setup.Repository,
            "head-sha-1");

    [Fact]
    public async Task Release_GivenMergeAppliedButPostMergeVerificationAbsent_DoesNotRelease()
    {
        var setup = Create();
        var merge = new StubMerge();

        // Seed exactly what a crash after the merge leaves behind: an applied effect, no verification fact.
        var operationId = OperationId(setup);
        setup.Ledger.Begin(operationId, setup.Snapshot.WorkItemId.Value, setup.Trigger.Id.Value, "release",
            setup.Trigger.Id.Value, 1, "git-merge", setup.Repository, "head-sha-1");
        setup.Ledger.MarkApplied(operationId, "merged-sha-1", "git:rev-parse");

        var verifier = new FakeVerifier { Succeed = false };
        var outcome = await Handler(setup, merge, verifier).HandleAsync(setup.Snapshot, setup.Trigger);

        Assert.Equal(0, merge.MergeCalls);
        Assert.Equal(1, verifier.Calls);
        Assert.Equal(StageDispatchKind.Failed, outcome.Kind);
        Assert.DoesNotContain(outcome.Events, static e => e.Type == OperatorEvents.ReleaseCompleted);
        Assert.NotEqual(true, setup.Store.Find(setup.Snapshot.WorkItemId.Value, "merged-sha-1", ContractA.Digest(), "head-sha-1", "base-sha-1")?.Verified);
    }

    [Fact]
    public async Task Release_GivenCrashAfterMergeBeforeVerification_ReconcilesGitAndReverifies()
    {
        var setup = Create();
        var merge = new StubMerge();
        var crashing = new FakeVerifier { Throw = true };

        var interrupted = await Handler(setup, merge, crashing).HandleAsync(setup.Snapshot, setup.Trigger);

        // The merge is a durable fact; the release is not.
        Assert.Equal(StageDispatchKind.Failed, interrupted.Kind);
        Assert.Equal(1, merge.MergeCalls);
        Assert.Equal(OperationState.Applied, setup.Ledger.Latest(OperationId(setup))!.State);
        Assert.Equal(VerificationState.Unverified, setup.Ledger.Latest(OperationId(setup))!.VerificationState);

        // A fresh process (new handler over the same durable state) reconciles and verifies without merging again.
        var restarted = new FakeVerifier();
        var outcome = await Handler(setup, merge, restarted).HandleAsync(setup.Snapshot, setup.Trigger);

        Assert.Equal(1, merge.MergeCalls);
        Assert.True(merge.ContainsCalls >= 1);
        Assert.Equal(1, restarted.Calls);
        Assert.Equal(StageDispatchKind.Advanced, outcome.Kind);
        Assert.Equal(FactoryWorkState.Released, outcome.NextState);
        Assert.Single(outcome.Events, static e => e.Type == OperatorEvents.ReleaseCompleted);
        Assert.True(setup.Store.Find(setup.Snapshot.WorkItemId.Value, "merged-sha-1", ContractA.Digest(), "head-sha-1", "base-sha-1")!.Verified);
    }

    [Fact]
    public async Task Release_GivenPostMergeContractFailureAndRestart_NeverReportsReleased()
    {
        var setup = Create();
        var merge = new StubMerge();
        var failing = new FakeVerifier { Succeed = false };

        var first = await Handler(setup, merge, failing).HandleAsync(setup.Snapshot, setup.Trigger);
        Assert.Equal(StageDispatchKind.Failed, first.Kind);

        // Even after restart, the failed verification record cannot bless a release.
        var second = await Handler(setup, merge, new FakeVerifier { Succeed = false }).HandleAsync(setup.Snapshot, setup.Trigger);
        Assert.Equal(StageDispatchKind.Failed, second.Kind);
        Assert.Equal(1, merge.MergeCalls);
        Assert.False(setup.Store.Find(setup.Snapshot.WorkItemId.Value, "merged-sha-1", ContractA.Digest(), "head-sha-1", "base-sha-1")!.Verified);
    }

    [Fact]
    public async Task Release_GivenVerifiedSameMergedShaAndContract_EmitsReleaseOnce()
    {
        var setup = Create();
        var merge = new StubMerge();
        var verifier = new FakeVerifier();

        var first = await Handler(setup, merge, verifier).HandleAsync(setup.Snapshot, setup.Trigger);
        var second = await Handler(setup, merge, verifier).HandleAsync(setup.Snapshot, setup.Trigger);

        Assert.Equal(1, merge.MergeCalls);
        Assert.Equal(1, verifier.Calls); // the second attempt reuses the verified fact, it does not re-merge
        Assert.Single(first.Events, static e => e.Type == OperatorEvents.ReleaseCompleted);
        Assert.Single(second.Events, static e => e.Type == OperatorEvents.ReleaseCompleted);
        Assert.Equal(OperationState.Applied, setup.Ledger.Latest(OperationId(setup))!.State);
        Assert.Equal(VerificationState.Verified, setup.Ledger.Latest(OperationId(setup))!.VerificationState);
        Assert.Single(setup.Store.ReadAll());
    }

    [Fact]
    public async Task Release_GivenPreviouslyAppliedMergeAndDifferentContract_RequiresNewValidProof()
    {
        var setup = Create();
        var merge = new StubMerge();

        Assert.Equal(
            StageDispatchKind.Advanced,
            (await Handler(setup, merge, new FakeVerifier()).HandleAsync(setup.Snapshot, setup.Trigger)).Kind);

        // Same merged revision, different frozen contract: the old proof must not be reused.
        var different = new FakeVerifier { Succeed = false };
        var trigger = setup.Trigger with
        {
            Data = new Dictionary<string, string>(setup.Trigger.Data, StringComparer.Ordinal)
            {
                ["acceptanceContract"] = ContractB
            }
        };

        var outcome = await Handler(setup, merge, different).HandleAsync(setup.Snapshot, trigger);

        Assert.Equal(1, merge.MergeCalls);
        Assert.Equal(1, different.Calls);
        Assert.Equal(StageDispatchKind.Failed, outcome.Kind);
        Assert.Equal(2, setup.Store.ReadAll().Count);
    }

    [Fact]
    public async Task Operation_GivenUncertainMergeState_ReconcilesGitBeforeRetry()
    {
        var setup = Create();
        var merge = new StubMerge { ContainsResult = false };

        var operationId = OperationId(setup);
        setup.Ledger.Begin(operationId, setup.Snapshot.WorkItemId.Value, setup.Trigger.Id.Value, "release",
            setup.Trigger.Id.Value, 1, "git-merge", setup.Repository, "head-sha-1");
        setup.Ledger.MarkApplied(operationId, "merged-sha-1", "git:rev-parse");

        var verifier = new FakeVerifier();
        var outcome = await Handler(setup, merge, verifier).HandleAsync(setup.Snapshot, setup.Trigger);

        Assert.Equal(StageDispatchKind.Failed, outcome.Kind);
        Assert.Contains("merge-reconciliation-failed", outcome.FailureCategory, StringComparison.Ordinal);
        Assert.Equal(0, merge.MergeCalls);
        Assert.Equal(0, verifier.Calls);
    }

    [Fact]
    public async Task Lifecycle_GivenMergeAlreadyApplied_DoesNotUsePreMergeReworkEdge()
    {
        var setup = Create();
        var merge = new StubMerge();

        var operationId = OperationId(setup);
        setup.Ledger.Begin(operationId, setup.Snapshot.WorkItemId.Value, setup.Trigger.Id.Value, "release",
            setup.Trigger.Id.Value, 1, "git-merge", setup.Repository, "head-sha-1");
        setup.Ledger.MarkApplied(operationId, "merged-sha-1", "git:rev-parse");

        var outcome = await Handler(setup, merge, new FakeVerifier { Succeed = false })
            .HandleAsync(setup.Snapshot, setup.Trigger);

        // Post-merge failure is terminal; the D-08 A rework edge is pre-merge only.
        Assert.Equal(StageDispatchKind.Failed, outcome.Kind);
        Assert.DoesNotContain(outcome.Events, static e => e.Type == OperatorEvents.ReworkRequested);
    }

    private static class ContractDigestExtensions
    {
    }
}

internal static class ContractTextExtensions
{
    public static string Digest(this string contractJson)
        => AcceptanceContract.Parse(contractJson).Contract!.Digest;
}
