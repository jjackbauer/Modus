using System.Net;
using System.Net.Sockets;
using System.Text;
using Wip.Factory.Host.Factory;
using Wip.Factory.Wip.Governance;
using Xunit;

namespace Wip.Factory.Host.Tests;

/// <summary>
/// FAC-P0-02 / K-03 / K-04: passing HTTP scenarios prove requirement coverage and nothing else. Validation,
/// architectural conformance and security are separate executed checks, and a missing, stale, incomplete or failed
/// record refuses ReadyForDelivery.
/// </summary>
public sealed class ReviewEvidenceTests
{
    private static readonly string ContractJson = """
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

    private const string Requirement = """
        ---
        title: Book catalog HTTP API
        supportedClass: aspnetcore-http-net10
        ---

        Create an HTTP JSON API to add books.

        ## Acceptance expectations
        - Creating a book returns 201 with an identifier.
        """;

    private sealed class StubServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();

        public StubServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            BaseAddress = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
            _ = Task.Run(LoopAsync);
        }

        public Uri BaseAddress { get; }

        private async Task LoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token);
                }
                catch (Exception)
                {
                    return;
                }

                _ = Task.Run(async () =>
                {
                    using (client)
                    {
                        try
                        {
                            const string body = """{"id":"b1","title":"Dune"}""";
                            var response = $"HTTP/1.1 201 Created\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
                            var stream = client.GetStream();
                            var buffer = new byte[8192];
                            await stream.ReadAsync(buffer, _cts.Token);
                            await stream.WriteAsync(Encoding.UTF8.GetBytes(response), _cts.Token);
                        }
                        catch (Exception)
                        {
                        }
                    }
                });
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _cts.Dispose();
        }
    }

    private sealed class StubCandidateHost : ICandidateHost
    {
        private readonly Uri _address;

        public StubCandidateHost(Uri address) => _address = address;

        public ValueTask<CandidateSession> StartAsync(string worktreePath, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new CandidateSession(_address, 4242, true, static () => ValueTask.CompletedTask, "test-identity@1"));
    }

    private sealed class StubBuild : ICandidateBuildVerifier
    {
        public ValueTask<(bool Ok, string Detail)> VerifyAsync(string worktreePath, CancellationToken cancellationToken = default)
            => ValueTask.FromResult((true, "build passed"));
    }

    private sealed class NoRegressionTests : IRegressionTestRunner
    {
        public ValueTask<RegressionTestResult> RunAsync(string worktreePath, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(RegressionTestResult.NotApplicable("sha256:no-existing-tests"));
    }

    private sealed class FailingRegressionTests : IRegressionTestRunner
    {
        public ValueTask<RegressionTestResult> RunAsync(string worktreePath, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new RegressionTestResult(true, false, 1, "BooksTests.Get_Returns404 expected 200", "sha256:regression-failure"));
    }

    private sealed class ScriptedRunner : IImplementationStageRunner
    {
        private readonly string _worktree;

        public ScriptedRunner(string worktree) => _worktree = worktree;

        public ValueTask<ImplementationOutcome> RunAsync(ImplementationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(ImplementationOutcome.Ok("base-1", "head-1", 2, "sha256:diff", _worktree));
    }

    /// <summary>Executes the real check and then mutates exactly one field, simulating an unbound or stale record.</summary>
    private sealed class MutatingCheck : IReviewCheck
    {
        private readonly IReviewCheck _inner;
        private readonly Func<ReviewCheckExecution, ReviewCheckExecution> _mutate;

        public MutatingCheck(IReviewCheck inner, Func<ReviewCheckExecution, ReviewCheckExecution> mutate)
        {
            _inner = inner;
            _mutate = mutate;
        }

        public ReviewCheckKind Kind => _inner.Kind;

        public async ValueTask<ReviewCheckExecution> ExecuteAsync(ReviewCheckContext context, CancellationToken cancellationToken = default)
            => _mutate(await _inner.ExecuteAsync(context, cancellationToken));
    }

    private static async Task<(FactoryRuntime Runtime, string WorkItemId)> RunToReviewAsync(
        StubServer server,
        FactoryReviewCheckSet checks,
        IRegressionTestRunner? regressions = null)
    {
        var basePath = Path.Combine(Path.GetTempPath(), "factory-review-evidence-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(basePath, "target");
        var worktree = Path.Combine(basePath, "worktree");
        Directory.CreateDirectory(repository);
        FactoryTestWorkspace.Write(worktree);

        var (root, error) = FactoryControlRoot.Resolve(repository, Path.Combine(basePath, "control"));
        Assert.NotNull(root);
        Assert.Null(error);

        var runner = new ScriptedRunner(worktree);
        var build = new StubBuild();
        var host = new StubCandidateHost(server.BaseAddress);
        var handler = new FactoryStageDispatchHandler(
            runner,
            host,
            build,
            verificationRoot: Path.Combine(root!.Root, "verify"),
            regressions: regressions ?? new NoRegressionTests(),
            reviewChecks: checks);

        var runtime = new FactoryRuntime(root!, runner, host, build, handler: handler);
        var submit = await runtime.SubmitAsync(Requirement, null, ContractJson);
        Assert.True(submit.Accepted, string.Join(";", submit.Errors));

        await runtime.RunAsync(16);
        var (proposal, _) = await runtime.ContractShowAsync(submit.WorkItemId!);
        Assert.True((await runtime.ContractApproveAsync(submit.WorkItemId!, proposal!.ProposalDigest)).Ok);

        await runtime.RunAsync(32);
        return (runtime, submit.WorkItemId!);
    }

    private static async Task<string> RejectReasonAsync(FactoryRuntime runtime, string workItemId)
    {
        var events = await runtime.EventsAsync(workItemId);
        var refusal = events
            .LastOrDefault(static e => e.Type is OperatorEvents.ReworkRequested or OperatorEvents.StageFailed);

        Assert.NotNull(refusal);
        Assert.True(refusal!.Data.TryGetValue("rejectReason", out var reason), "refusal carries no rejectReason");
        return reason!;
    }

    private static FactoryReviewCheckSet FullChecks(StubServer server, IRegressionTestRunner? regressions = null)
        => FactoryReviewCheckSet.Default(
            new StubBuild(),
            regressions ?? new NoRegressionTests(),
            new StubCandidateHost(server.BaseAddress),
            new AcceptanceContractRunner());

    [Fact]
    public async Task Review_GivenHttpContractPassButSecurityCheckMissing_RefusesReview()
    {
        using var server = new StubServer();
        var checks = new FactoryReviewCheckSet(
            new ValidationReviewCheck(new StubBuild(), new NoRegressionTests()),
            new RequirementCoverageReviewCheck(new StubCandidateHost(server.BaseAddress), new AcceptanceContractRunner()),
            new ArchitectureConformanceReviewCheck());

        var (runtime, workItem) = await RunToReviewAsync(server, checks);

        Assert.NotEqual("ReadyForDelivery", Assert.Single(await runtime.StatusAsync()).State);
        Assert.Contains("review-evidence-missing:security", await RejectReasonAsync(runtime, workItem), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Review_GivenCandidateListenerButArchitectureCheckMissing_RefusesReview()
    {
        using var server = new StubServer();
        var checks = new FactoryReviewCheckSet(
            new ValidationReviewCheck(new StubBuild(), new NoRegressionTests()),
            new RequirementCoverageReviewCheck(new StubCandidateHost(server.BaseAddress), new AcceptanceContractRunner()),
            new SecurityPolicyReviewCheck());

        var (runtime, workItem) = await RunToReviewAsync(server, checks);

        Assert.NotEqual("ReadyForDelivery", Assert.Single(await runtime.StatusAsync()).State);
        Assert.Contains("review-evidence-missing:architectural-conformance", await RejectReasonAsync(runtime, workItem), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Review_GivenCheckSaysPassedWithoutExecutionArtifact_RefusesReview()
    {
        using var server = new StubServer();
        var checks = FactoryReviewCheckSet.Default(
            new StubBuild(),
            new NoRegressionTests(),
            new StubCandidateHost(server.BaseAddress),
            new AcceptanceContractRunner());

        // Rebuild the set with a security check whose "passed" claim has no executable identity or artifact digest.
        var incompleteSecurity = new MutatingCheck(
            new SecurityPolicyReviewCheck(),
            static execution => execution with
            {
                Outcome = ReviewCheckExecution.PassedOutcome,
                ExecutedBy = string.Empty,
                ExecutedVersion = string.Empty,
                ArtifactDigest = string.Empty
            });

        var rebuilt = new FactoryReviewCheckSet(
            new ValidationReviewCheck(new StubBuild(), new NoRegressionTests()),
            new RequirementCoverageReviewCheck(new StubCandidateHost(server.BaseAddress), new AcceptanceContractRunner()),
            new ArchitectureConformanceReviewCheck(),
            incompleteSecurity);

        var (runtime, workItem) = await RunToReviewAsync(server, rebuilt);

        Assert.NotEqual("ReadyForDelivery", Assert.Single(await runtime.StatusAsync()).State);
        Assert.Contains("review-evidence-incomplete:security", await RejectReasonAsync(runtime, workItem), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Review_GivenRegressionFailureButHttpContractPass_RefusesReview()
    {
        using var server = new StubServer();
        var checks = FullChecks(server, new FailingRegressionTests());

        var (runtime, workItem) = await RunToReviewAsync(server, checks, new FailingRegressionTests());

        Assert.NotEqual("ReadyForDelivery", Assert.Single(await runtime.StatusAsync()).State);
        Assert.Contains("review-check-failed:validation", await RejectReasonAsync(runtime, workItem), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Review_GivenEvidenceFromDifferentCommit_RefusesReview()
    {
        using var server = new StubServer();
        var staleSecurity = new MutatingCheck(
            new SecurityPolicyReviewCheck(),
            static execution => execution with { ReviewedSha = "a-different-commit" });

        var checks = new FactoryReviewCheckSet(
            new ValidationReviewCheck(new StubBuild(), new NoRegressionTests()),
            new RequirementCoverageReviewCheck(new StubCandidateHost(server.BaseAddress), new AcceptanceContractRunner()),
            new ArchitectureConformanceReviewCheck(),
            staleSecurity);

        var (runtime, workItem) = await RunToReviewAsync(server, checks);

        Assert.NotEqual("ReadyForDelivery", Assert.Single(await runtime.StatusAsync()).State);
        Assert.Contains("review-evidence-stale:security", await RejectReasonAsync(runtime, workItem), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Review_GivenAllFourExecutedChecks_RecordsDurableEvidence()
    {
        using var server = new StubServer();
        var (runtime, workItem) = await RunToReviewAsync(server, FullChecks(server));

        Assert.Equal("ReadyForDelivery", Assert.Single(await runtime.StatusAsync()).State);

        var review = (await runtime.EventsAsync(workItem)).Last(static e => e.Type == OperatorEvents.ReviewAccepted);
        Assert.True(review.Data.ContainsKey("evidenceDigest"));
        Assert.StartsWith("sha256:", review.Data["evidenceDigest"], StringComparison.Ordinal);

        var evidence = new ReviewEvidenceStore(
            Path.Combine(runtime.Root.Root, "verify", "review-evidence.log")).ReadFor(workItem);
        Assert.Equal(4, evidence.Count);
        Assert.All(evidence, static execution => Assert.True(execution.IsComplete));
        Assert.All(evidence, static execution => Assert.Equal(ReviewCheckExecution.PassedOutcome, execution.Outcome));
    }
}
