using System.Net;
using System.Net.Sockets;
using System.Text;
using Wip.Factory.Host.Factory;
using Xunit;

namespace Wip.Factory.Host.Tests;

/// <summary>
/// The bounded rework loop (v0.3 §3.5): a rejected attempt is fed back to the implementer with the failure as
/// context, at most <see cref="FactoryStageDispatchHandler.ReworkBudget"/> times, then the item fails.
/// </summary>
public sealed class ReworkLoopTests
{
    private const string ContractJson = """
        {
          "schemaVersion": 1,
          "supportedClass": "aspnetcore-http-net10",
          "scenarios": [
            { "id": "AC-1", "method": "POST", "path": "/jobs",
              "request": { "json": { "name": "build" } },
              "expect": { "status": 201, "jsonSubset": { "state": "pending" } } }
          ]
        }
        """;

    private const string Requirement = """
        ---
        title: Job queue HTTP API
        supportedClass: aspnetcore-http-net10
        ---

        Create an HTTP JSON API for a job queue.

        ## Acceptance expectations
        - Creating a job returns 201 with state pending.
        """;

    private sealed class FlakyServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly int _failFirst;
        private int _calls;

        public FlakyServer(int failFirst)
        {
            _failFirst = failFirst;
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

                            var call = Interlocked.Increment(ref _calls);
                            var response = call <= _failFirst
                                ? Respond(405, "Method Not Allowed", """{"error":"wrong verb"}""")
                                : Respond(201, "Created", """{"id":"j1","state":"pending"}""");

                            await stream.WriteAsync(Encoding.UTF8.GetBytes(response), _cts.Token);
                        }
                        catch (Exception)
                        {
                        }
                    }
                });
            }
        }

        private static string Respond(int status, string reason, string body)
            => $"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";

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
            => ValueTask.FromResult(new CandidateSession(_address, 5151, true, static () => ValueTask.CompletedTask));
    }

    private sealed class StubBuild : ICandidateBuildVerifier
    {
        public ValueTask<(bool Ok, string Detail)> VerifyAsync(string worktreePath, CancellationToken cancellationToken = default)
            => ValueTask.FromResult((true, "build passed"));
    }

    private sealed class CountingRunner : IImplementationStageRunner
    {
        private readonly string _worktree;

        public CountingRunner(string worktree) => _worktree = worktree;

        public int Calls { get; private set; }

        public string? LastReworkContext { get; private set; }

        public ValueTask<ImplementationOutcome> RunAsync(ImplementationRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastReworkContext = request.ReworkContext;
            return ValueTask.FromResult(ImplementationOutcome.Ok("base-1", "head-1", 2, "sha256:diff", _worktree));
        }
    }

    private static async Task<(FactoryRuntime Runtime, CountingRunner Runner, string WorkItem)> RunAsync(int failFirst)
    {
        var basePath = Path.Combine(Path.GetTempPath(), "factory-rework-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(basePath, "target");
        var worktree = Path.Combine(basePath, "worktree");
        Directory.CreateDirectory(repository);
        Directory.CreateDirectory(worktree);
        await File.WriteAllTextAsync(Path.Combine(worktree, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var (root, error) = FactoryControlRoot.Resolve(repository, Path.Combine(basePath, "control"));
        Assert.NotNull(root);
        Assert.Null(error);

        var server = new FlakyServer(failFirst);
        var runner = new CountingRunner(worktree);
        var handler = new FactoryStageDispatchHandler(
            runner, new StubCandidateHost(server.BaseAddress), new StubBuild(),
            merges: new StubMerge(), verificationRoot: Path.Combine(basePath, "verify"),
            targetHead: _ => Task.FromResult<string?>("base-1"));

        var runtime = new FactoryRuntime(root!, runner, new StubCandidateHost(server.BaseAddress), new StubBuild(), new StubMerge(), handler);

        var submit = await runtime.SubmitAsync(Requirement, null, ContractJson);
        Assert.True(submit.Accepted, string.Join(";", submit.Errors));

        await runtime.RunAsync(16);
        var (proposal, _) = await runtime.ContractShowAsync(submit.WorkItemId!);
        Assert.True((await runtime.ContractApproveAsync(submit.WorkItemId!, proposal!.ProposalDigest)).Ok);

        await runtime.RunAsync(32);
        return (runtime, runner, submit.WorkItemId!);
    }

    private sealed class StubMerge : IMergeEngine
    {
        public ValueTask<MergeResult> MergeAsync(string repositoryPath, string headSha, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(MergeResult.Ok_("merged-sha"));

        public ValueTask<string> MaterialiseAsync(string repositoryPath, string mergedSha, string destination, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            return ValueTask.FromResult(destination);
        }
    }

    [Fact]
    public async Task Rework_GivenFirstAttemptRejected_RepairsAndAdvances()
    {
        // The first candidate start answers 405; the repair answers 201.
        var (runtime, runner, _) = await RunAsync(failFirst: 1);

        var status = Assert.Single(await runtime.StatusAsync());
        Assert.Equal("ReadyForDelivery", status.State);
        Assert.Equal(2, runner.Calls);
        Assert.NotNull(runner.LastReworkContext);
        Assert.Contains("status-mismatch", runner.LastReworkContext!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rework_GivenBudgetExhausted_FailsTheStage()
    {
        // Always wrong: the factory must stop after its budget instead of looping forever.
        var (runtime, runner, _) = await RunAsync(failFirst: 100);

        var status = Assert.Single(await runtime.StatusAsync());
        Assert.Equal("Failed", status.State);
        Assert.Equal(FactoryStageDispatchHandler.ReworkBudget + 1, runner.Calls);
    }
}
