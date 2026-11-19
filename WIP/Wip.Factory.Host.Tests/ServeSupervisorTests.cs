using System.Net;
using System.Net.Sockets;
using System.Text;
using Wip.Factory.Host.Factory;
using Xunit;

namespace Wip.Factory.Host.Tests;

/// <summary>
/// FAC-P1-02 / K-10: serve is a bounded supervisor over the pinned merged revision, with one owner per work item,
/// accurate states, and no always-on claim. A child death restarts within the budget or fails with a named category.
/// </summary>
public sealed class ServeSupervisorTests
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

    private sealed class LivenessCandidateHost : ICandidateHost
    {
        private readonly Uri _address;
        private int _starts;

        public LivenessCandidateHost(Uri address) => _address = address;

        public Func<int, bool> Alive { get; set; } = static _ => true;

        public int Starts => Volatile.Read(ref _starts);

        public ValueTask<CandidateSession> StartAsync(string worktreePath, CancellationToken cancellationToken = default)
        {
            var index = Interlocked.Increment(ref _starts);
            return ValueTask.FromResult(new CandidateSession(
                _address,
                5000 + index,
                true,
                static () => ValueTask.CompletedTask,
                "test-listener@1:" + (5000 + index),
                () => Alive(index)));
        }
    }

    private sealed class StubBuild : ICandidateBuildVerifier
    {
        public ValueTask<(bool Ok, string Detail)> VerifyAsync(string worktreePath, CancellationToken cancellationToken = default)
            => ValueTask.FromResult((true, "build passed"));
    }

    private sealed class StubMerge : IMergeEngine
    {
        public string? MaterialisedSha { get; private set; }

        public ValueTask<MergeResult> MergeAsync(string repositoryPath, string headSha, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(MergeResult.Ok_("merged-sha-abcdef1234567890"));

        public ValueTask<string> MaterialiseAsync(string repositoryPath, string mergedSha, string destination, CancellationToken cancellationToken = default)
        {
            MaterialisedSha = mergedSha;
            FactoryTestWorkspace.Write(destination);
            return ValueTask.FromResult(destination);
        }
    }

    private sealed class ScriptedRunner : IImplementationStageRunner
    {
        private readonly string _worktree;

        public ScriptedRunner(string worktree) => _worktree = worktree;

        public ValueTask<ImplementationOutcome> RunAsync(ImplementationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(ImplementationOutcome.Ok("base-1", "head-1", 2, "sha256:diff", _worktree));
    }

    private sealed record Released(
        FactoryRuntime Runtime,
        string WorkItemId,
        string MergedSha,
        LivenessCandidateHost Host,
        StubMerge Merge);

    private static async Task<Released> ReleasedAsync(StubServer server)
    {
        var basePath = Path.Combine(Path.GetTempPath(), "factory-supervisor-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(basePath, "target");
        var worktree = Path.Combine(basePath, "worktree");
        Directory.CreateDirectory(repository);
        FactoryTestWorkspace.Write(worktree);

        var (root, error) = FactoryControlRoot.Resolve(repository, Path.Combine(basePath, "control"));
        Assert.NotNull(root);
        Assert.Null(error);

        var host = new LivenessCandidateHost(server.BaseAddress);
        var merge = new StubMerge();
        var runner = new ScriptedRunner(worktree);
        var build = new StubBuild();
        var handler = new FactoryStageDispatchHandler(
            runner,
            host,
            build,
            merges: merge,
            verificationRoot: Path.Combine(root!.Root, "verify"),
            targetHead: _ => Task.FromResult<string?>("base-1"));

        var runtime = new FactoryRuntime(root, runner, host, build, merge, handler);

        var submit = await runtime.SubmitAsync(Requirement, null, ContractJson);
        Assert.True(submit.Accepted, string.Join(";", submit.Errors));

        await runtime.RunAsync(16);
        var (proposal, _) = await runtime.ContractShowAsync(submit.WorkItemId!);
        Assert.True((await runtime.ContractApproveAsync(submit.WorkItemId!, proposal!.ProposalDigest)).Ok);

        await runtime.RunAsync(16);
        var review = (await runtime.EventsAsync(submit.WorkItemId!)).Last(static e => e.Type == OperatorEvents.ReviewAccepted);
        Assert.True((await runtime.ApproveMergeAsync(submit.WorkItemId!, review.Data["reviewDigest"])).Ok);

        await runtime.RunAsync(16);

        var status = Assert.Single(await runtime.StatusAsync());
        Assert.Equal("Released", status.State);

        var release = (await runtime.EventsAsync(submit.WorkItemId!)).Last(static e => e.Type == OperatorEvents.ReleaseCompleted);
        return new Released(runtime, submit.WorkItemId!, release.Data["mergedSha"], host, merge);
    }

    private static ServeStatusJournal Journal(FactoryRuntime runtime, string workItemId)
        => new(Path.Combine(runtime.Root.Root, "serve", workItemId + ".status.log"));

    private static async Task WaitForStatusAsync(ServeStatusJournal journal, string status)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (string.Equals(journal.Latest()?.Status, status, StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new InvalidOperationException($"status '{status}' was not observed; last was '{journal.Latest()?.Status}'");
    }

    [Fact]
    public async Task Supervisor_GivenChildDeath_RestartsWithinBound()
    {
        using var server = new StubServer();
        var released = await ReleasedAsync(server);

        // Review start = 1, post-merge start = 2, supervised start = 3. Let the supervised process die once.
        released.Host.Alive = static index => index != 3;

        var result = await released.Runtime.ServeAsync(released.WorkItemId, TimeSpan.FromMilliseconds(800));

        Assert.True(result.Ok, result.Error);
        Assert.Equal("stopped", result.Status);
        Assert.Equal(1, result.RestartCount);
        Assert.True(released.Host.Starts >= 4);
        Assert.Contains(Journal(released.Runtime, released.WorkItemId).ReadAll(), static record => record.Status == "restarted");
    }

    [Fact]
    public async Task Supervisor_GivenRestartBudgetExhausted_ReportsFailure()
    {
        using var server = new StubServer();
        var released = await ReleasedAsync(server);

        released.Host.Alive = static _ => false;

        var result = await released.Runtime.ServeAsync(released.WorkItemId, TimeSpan.FromSeconds(5));

        Assert.False(result.Ok);
        Assert.Equal("supervisor-restart-budget-exhausted", result.Error);
        Assert.Equal(FactoryRuntime.MaxServeRestarts, result.RestartCount);
        Assert.Contains(Journal(released.Runtime, released.WorkItemId).ReadAll(), static record => record.Status == "failed");
    }

    [Fact]
    public async Task Supervisor_GivenSecondServe_RefusesDuplicateOwnership()
    {
        using var server = new StubServer();
        var released = await ReleasedAsync(server);
        var journal = Journal(released.Runtime, released.WorkItemId);

        var first = released.Runtime.ServeAsync(released.WorkItemId, TimeSpan.FromSeconds(2)).AsTask();
        await WaitForStatusAsync(journal, "serving");

        var second = await released.Runtime.ServeAsync(released.WorkItemId, TimeSpan.FromMilliseconds(100));

        Assert.False(second.Ok);
        Assert.StartsWith("serve-duplicate-ownership", second.Error, StringComparison.Ordinal);

        var completed = await first;
        Assert.True(completed.Ok, completed.Error);
    }

    [Fact]
    public async Task Supervisor_GivenHostRestart_ServesPinnedRelease()
    {
        using var server = new StubServer();
        var released = await ReleasedAsync(server);

        // A new process view over the same control root; the target branch moved after the release.
        var handler = new FactoryStageDispatchHandler(
            new ScriptedRunner("/unused"),
            released.Host,
            new StubBuild(),
            merges: released.Merge,
            verificationRoot: Path.Combine(released.Runtime.Root.Root, "verify"),
            targetHead: _ => Task.FromResult<string?>("a-newer-target-head"));

        var restarted = new FactoryRuntime(
            released.Runtime.Root,
            implementations: null,
            candidates: released.Host,
            builds: new StubBuild(),
            merges: released.Merge,
            handler: handler,
            targetHeadProbe: _ => Task.FromResult<string?>("a-newer-target-head"));

        var result = await restarted.ServeAsync(released.WorkItemId, TimeSpan.FromMilliseconds(300));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(released.MergedSha, result.MergedSha);
        Assert.Equal(released.MergedSha, released.Merge.MaterialisedSha);
    }

    [Fact]
    public async Task Supervisor_GivenTimedServeExit_DoesNotClaimAlwaysOn()
    {
        using var server = new StubServer();
        var released = await ReleasedAsync(server);

        var result = await released.Runtime.ServeAsync(released.WorkItemId, TimeSpan.FromMilliseconds(300));

        Assert.True(result.Ok, result.Error);
        Assert.Equal("stopped", result.Status);
        Assert.Equal(0, result.RestartCount);
        Assert.Equal("stopped", Journal(released.Runtime, released.WorkItemId).Latest()!.Status);
    }
}
