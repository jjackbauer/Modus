using System.Net;
using System.Net.Sockets;
using System.Text;
using Wip.Factory.Host.Factory;
using Wip.Factory.Wip.Governance;
using Xunit;

namespace Wip.Factory.Host.Tests;

/// <summary>
/// FAC-P1-05 / K-13: merge authorization is bound to the current review evidence, the requirement revision, the
/// frozen contract and the target branch. Missing, changed or already-applied evidence refuses the authorization.
/// </summary>
public sealed class ApprovalBindingTests
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

    private sealed class ScriptedRunner : IImplementationStageRunner
    {
        private readonly string _worktree;

        public ScriptedRunner(string worktree) => _worktree = worktree;

        public ValueTask<ImplementationOutcome> RunAsync(ImplementationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(ImplementationOutcome.Ok("base-1", "head-1", 2, "sha256:diff", _worktree));
    }

    private sealed class StubMerge : IMergeEngine
    {
        public ValueTask<MergeResult> MergeAsync(string repositoryPath, string headSha, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(MergeResult.Ok_("merged-sha-1"));

        public ValueTask<string> MaterialiseAsync(string repositoryPath, string mergedSha, string destination, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(destination);
    }

    private sealed record Ready(
        FactoryControlRoot Root,
        FactoryRuntime Runtime,
        string WorkItemId,
        string ReviewDigest);

    private static async Task<Ready> ReadyForDeliveryAsync(StubServer server, Func<CancellationToken, Task<string?>>? targetProbe = null)
    {
        var basePath = Path.Combine(Path.GetTempPath(), "factory-approval-" + Guid.NewGuid().ToString("N"));
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
            merges: new StubMerge(),
            verificationRoot: Path.Combine(root!.Root, "verify"),
            targetHead: _ => Task.FromResult<string?>("base-1"),
            regressions: new NoRegressionTests());

        var runtime = new FactoryRuntime(root, runner, host, build, new StubMerge(), handler, targetProbe);
        var submit = await runtime.SubmitAsync(Requirement, null, ContractJson);
        Assert.True(submit.Accepted, string.Join(";", submit.Errors));

        await runtime.RunAsync(16);
        var (proposal, _) = await runtime.ContractShowAsync(submit.WorkItemId!);
        Assert.True((await runtime.ContractApproveAsync(submit.WorkItemId!, proposal!.ProposalDigest)).Ok);
        await runtime.RunAsync(32);

        Assert.Equal("ReadyForDelivery", Assert.Single(await runtime.StatusAsync()).State);
        var review = (await runtime.EventsAsync(submit.WorkItemId!)).Last(static e => e.Type == OperatorEvents.ReviewAccepted);
        return new Ready(root, runtime, submit.WorkItemId!, review.Data["reviewDigest"]);
    }

    private static string EvidencePath(FactoryControlRoot root)
        => Path.Combine(root.Root, "verify", "review-evidence.log");

    [Fact]
    public async Task Approval_GivenMissingCheckEvidence_RefusesMerge()
    {
        using var server = new StubServer();
        var ready = await ReadyForDeliveryAsync(server);

        File.Delete(EvidencePath(ready.Root));

        var approved = await ready.Runtime.ApproveMergeAsync(ready.WorkItemId, ready.ReviewDigest);

        Assert.False(approved.Ok);
        Assert.Equal("approval-missing-check-evidence", approved.Error);
    }

    [Fact]
    public async Task Approval_GivenTargetBranchMoves_RefusesMerge()
    {
        using var server = new StubServer();
        var moved = "a-new-target-head";
        var ready = await ReadyForDeliveryAsync(server, _ => Task.FromResult<string?>(moved));

        var approved = await ready.Runtime.ApproveMergeAsync(ready.WorkItemId, ready.ReviewDigest);

        Assert.False(approved.Ok);
        Assert.Equal("approval-target-moved", approved.Error);
    }

    [Fact]
    public async Task Approval_GivenEvidenceChangesButSummaryDoesNot_RefusesMerge()
    {
        using var server = new StubServer();
        var ready = await ReadyForDeliveryAsync(server);

        var store = new ReviewEvidenceStore(EvidencePath(ready.Root));
        var evidence = store.ReadFor(ready.WorkItemId);
        var security = evidence.Single(static execution => execution.Kind == ReviewCheckKind.Security);
        store.Append(ready.WorkItemId, [security with { ArtifactDigest = "sha256:changed-but-summary-identical" }]);

        var approved = await ready.Runtime.ApproveMergeAsync(ready.WorkItemId, ready.ReviewDigest);

        Assert.False(approved.Ok);
        Assert.Equal("approval-evidence-mismatch", approved.Error);
    }

    [Fact]
    public async Task Approval_GivenRestartAfterApplied_RefusesDuplicate()
    {
        using var server = new StubServer();
        var ready = await ReadyForDeliveryAsync(server);

        var first = await ready.Runtime.ApproveMergeAsync(ready.WorkItemId, ready.ReviewDigest);
        Assert.True(first.Ok, first.Error);

        var restarted = new FactoryRuntime(ready.Root);
        var second = await restarted.ApproveMergeAsync(ready.WorkItemId, ready.ReviewDigest);

        Assert.False(second.Ok);
        Assert.Equal("approval-already-applied", second.Error);
    }
}
