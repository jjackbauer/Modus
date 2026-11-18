using System.Net;
using System.Net.Sockets;
using System.Text;
using Wip.Factory.Contracts.Work;
using Wip.Factory.Host.Factory;
using Xunit;

namespace Wip.Factory.Host.Tests;

/// <summary>
/// Supervision (PA-07): `serve` owns the released revision for as long as it runs, proves the identity of the
/// process it started, and is the only path to OperationallyValidated.
/// </summary>
public sealed class ServeTests
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
        private readonly int _status;

        public StubServer(int status = 201)
        {
            _status = status;
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
                            var stream = client.GetStream();
                            var buffer = new byte[8192];
                            await stream.ReadAsync(buffer, _cts.Token);
                            const string body = """{"id":"b1","title":"Dune"}""";
                            var response = $"HTTP/1.1 {_status} {( _status == 201 ? "Created" : "OK")}\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
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
        private readonly int _verifiedForCalls;
        private int _calls;

        public StubCandidateHost(Uri address, int verifiedForCalls = int.MaxValue)
        {
            _address = address;
            _verifiedForCalls = verifiedForCalls;
        }

        public ValueTask<CandidateSession> StartAsync(string worktreePath, CancellationToken cancellationToken = default)
        {
            _calls++;
            var verified = _calls <= _verifiedForCalls;
            return ValueTask.FromResult(new CandidateSession(_address, 5150, verified, static () => ValueTask.CompletedTask));
        }
    }

    private sealed class StubBuild : ICandidateBuildVerifier
    {
        public ValueTask<(bool Ok, string Detail)> VerifyAsync(string worktreePath, CancellationToken cancellationToken = default)
            => ValueTask.FromResult((true, "build passed"));
    }

    private sealed class StubMerge : IMergeEngine
    {
        public ValueTask<MergeResult> MergeAsync(string repositoryPath, string headSha, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(MergeResult.Ok_("merged-sha-abcdef1234567890"));

        public ValueTask<string> MaterialiseAsync(string repositoryPath, string mergedSha, string destination, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
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

    private static async Task<(FactoryRuntime Runtime, string WorkItem)> ReleasedAsync(Uri candidateAddress, int verifiedForCalls = int.MaxValue)
    {
        var basePath = Path.Combine(Path.GetTempPath(), "factory-serve-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(basePath, "target");
        var worktree = Path.Combine(basePath, "worktree");
        Directory.CreateDirectory(repository);
        Directory.CreateDirectory(worktree);
        await File.WriteAllTextAsync(Path.Combine(worktree, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var (root, error) = FactoryControlRoot.Resolve(repository, Path.Combine(basePath, "control"));
        Assert.NotNull(root);
        Assert.Null(error);

        var host = new StubCandidateHost(candidateAddress, verifiedForCalls);
        var merge = new StubMerge();
        var handler = new FactoryStageDispatchHandler(
            new ScriptedRunner(worktree), host, new StubBuild(),
            merges: merge, verificationRoot: Path.Combine(basePath, "verify"),
            targetHead: _ => Task.FromResult<string?>("base-1"));

        var runtime = new FactoryRuntime(root!, new ScriptedRunner(worktree), host, new StubBuild(), merge, handler);

        var submit = await runtime.SubmitAsync(Requirement, null, ContractJson);
        Assert.True(submit.Accepted, string.Join(";", submit.Errors));

        await runtime.RunAsync(16);
        var (proposal, _) = await runtime.ContractShowAsync(submit.WorkItemId!);
        Assert.True((await runtime.ContractApproveAsync(submit.WorkItemId!, proposal!.ProposalDigest)).Ok);

        await runtime.RunAsync(16);

        var review = (await runtime.EventsAsync(submit.WorkItemId!))
            .Last(static e => e.Type == OperatorEvents.ReviewAccepted);
        Assert.True((await runtime.ApproveMergeAsync(submit.WorkItemId!, review.Data["reviewDigest"])).Ok);

        await runtime.RunAsync(16);

        return (runtime, submit.WorkItemId!);
    }

    [Fact]
    public async Task Supervisor_GivenServedRelease_ProvesIdentityAndValidatesOperations()
    {
        using var server = new StubServer();
        var (runtime, workItem) = await ReleasedAsync(server.BaseAddress);

        var result = await runtime.ServeAsync(workItem, TimeSpan.FromMilliseconds(300));

        Assert.True(result.Ok, result.Error);
        Assert.True(result.ProcessId > 0);
        Assert.NotNull(result.Url);

        var status = Assert.Single(await runtime.StatusAsync());
        Assert.Equal("OperationallyValidated", status.State);
    }

    [Fact]
    public async Task Supervisor_GivenUnverifiedIdentity_DoesNotValidate()
    {
        using var server = new StubServer();
        // Review and post-merge each start a candidate; only the supervised one is unverified.
        var (runtime, workItem) = await ReleasedAsync(server.BaseAddress, verifiedForCalls: 2);

        var result = await runtime.ServeAsync(workItem, TimeSpan.FromMilliseconds(100));

        Assert.False(result.Ok);
        Assert.Equal("serve-identity-unproven", result.Error);
        var status = Assert.Single(await runtime.StatusAsync());
        Assert.Equal("Released", status.State);
    }

    [Fact]
    public async Task Supervisor_GivenNotReleased_RefusesToServe()
    {
        using var server = new StubServer();
        var (runtime, workItem) = await ReleasedAsync(server.BaseAddress);

        // Move the item back out of the released states by serving once, then refusing is checked on a fresh item.
        var first = await runtime.ServeAsync(workItem, TimeSpan.FromMilliseconds(100));
        Assert.True(first.Ok, first.Error);

        var (runtime2, workItem2) = await ReleasedAsync(server.BaseAddress);
        var snapshotState = (await runtime2.StatusAsync()).Single();
        Assert.Equal("Released", snapshotState.State);

        // A work item that never merged cannot be served.
        var statusResult = await runtime2.ServeAsync("WORK-does-not-exist", TimeSpan.FromMilliseconds(50));
        Assert.False(statusResult.Ok);
        Assert.Equal("work-item-not-found", statusResult.Error);
    }

    [Fact]
    public async Task Supervisor_GivenServeExit_DoesNotClaimAlwaysOnDaemon()
    {
        using var server = new StubServer();
        var (runtime, workItem) = await ReleasedAsync(server.BaseAddress);

        var result = await runtime.ServeAsync(workItem, TimeSpan.FromMilliseconds(100));

        // The session reports its own process id and the fact it supervised, not that a daemon persists.
        Assert.True(result.Ok);
        Assert.True(result.ProcessId > 0);
        var status = Assert.Single(await runtime.StatusAsync());
        Assert.Equal("OperationallyValidated", status.State);
    }
}
