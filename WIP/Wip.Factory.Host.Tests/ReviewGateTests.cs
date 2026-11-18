using System.Net;
using System.Net.Sockets;
using System.Text;
using Wip.Factory.Host.Factory;
using Xunit;

namespace Wip.Factory.Host.Tests;

/// <summary>
/// The review gate: a candidate only advances when the factory has verified it. These tests use a stub
/// candidate host (a scripted HTTP server) and a stub build verifier, so the gate's logic is exercised
/// without a toolchain — the real toolchain is exercised by the operator run.
/// </summary>
public sealed class ReviewGateTests
{
    private static readonly string ContractJson = """
        {
          "schemaVersion": 1,
          "supportedClass": "aspnetcore-http-net10",
          "scenarios": [
            { "id": "AC-1", "method": "POST", "path": "/books",
              "request": { "json": { "title": "Dune" } },
              "expect": { "status": 201, "jsonSubset": { "title": "Dune" } },
              "capture": { "bookId": "$.id" } },
            { "id": "AC-2", "method": "GET", "path": "/books/{bookId}", "expect": { "status": 200 } }
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
        private readonly Func<string, string, string> _respond;

        public StubServer(Func<string, string, string> respond)
        {
            _respond = respond;
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
                            var read = await stream.ReadAsync(buffer, _cts.Token);
                            var request = Encoding.ASCII.GetString(buffer, 0, read);
                            var parts = request.Split("\r\n")[0].Split(' ');
                            var response = _respond(parts[0], parts.Length > 1 ? parts[1] : "/");
                            await stream.WriteAsync(Encoding.UTF8.GetBytes(response), _cts.Token);
                        }
                        catch (Exception)
                        {
                        }
                    }
                });
            }
        }

        public static string Json(int status, string reason, string json)
            => $"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(json)}\r\nConnection: close\r\n\r\n{json}";

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
        private readonly bool _verified;

        public StubCandidateHost(Uri address, bool verified = true)
        {
            _address = address;
            _verified = verified;
        }

        public ValueTask<CandidateSession> StartAsync(string worktreePath, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new CandidateSession(_address, 4242, _verified, static () => ValueTask.CompletedTask));
    }

    private sealed class StubBuild : ICandidateBuildVerifier
    {
        private readonly bool _ok;

        public StubBuild(bool ok) => _ok = ok;

        public ValueTask<(bool Ok, string Detail)> VerifyAsync(string worktreePath, CancellationToken cancellationToken = default)
            => ValueTask.FromResult((_ok, _ok ? "build passed" : "build failed: CS1002"));
    }

    private sealed class ScriptedRunner : IImplementationStageRunner
    {
        private readonly string _worktree;

        public ScriptedRunner(string worktree) => _worktree = worktree;

        public ValueTask<ImplementationOutcome> RunAsync(ImplementationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(ImplementationOutcome.Ok("base-1", "head-1", 2, "sha256:diff", _worktree));
    }

    private static async Task<(FactoryRuntime Runtime, string WorkItem)> PreparedAsync(
        ICandidateHost host,
        ICandidateBuildVerifier build)
    {
        var basePath = Path.Combine(Path.GetTempPath(), "factory-review-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(basePath, "target");
        var worktree = Path.Combine(basePath, "worktree");
        Directory.CreateDirectory(repository);
        Directory.CreateDirectory(worktree);
        await File.WriteAllTextAsync(Path.Combine(worktree, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var (root, error) = FactoryControlRoot.Resolve(repository, Path.Combine(basePath, "control"));
        Assert.NotNull(root);
        Assert.Null(error);

        var runtime = new FactoryRuntime(root!, new ScriptedRunner(worktree), host, build);
        var submit = await runtime.SubmitAsync(Requirement, null, ContractJson);
        Assert.True(submit.Accepted, string.Join(";", submit.Errors));

        await runtime.RunAsync(16);
        var (proposal, _) = await runtime.ContractShowAsync(submit.WorkItemId!);
        Assert.NotNull(proposal);
        Assert.True((await runtime.ContractApproveAsync(submit.WorkItemId!, proposal!.ProposalDigest)).Ok);

        return (runtime, submit.WorkItemId!);
    }

    [Fact]
    public async Task Review_GivenContractPasses_AdvancesToReadyForDelivery()
    {
        using var server = new StubServer((method, path) => (method, path) switch
        {
            ("POST", "/books") => StubServer.Json(201, "Created", """{"id":"b1","title":"Dune"}"""),
            ("GET", "/books/b1") => StubServer.Json(200, "OK", """{"id":"b1","title":"Dune"}"""),
            _ => StubServer.Json(404, "Not Found", "{}")
        });

        var (runtime, _) = await PreparedAsync(new StubCandidateHost(server.BaseAddress), new StubBuild(true));
        await runtime.RunAsync(16);

        var status = Assert.Single(await runtime.StatusAsync());
        Assert.Equal("ReadyForDelivery", status.State);
    }

    [Fact]
    public async Task Review_GivenContractFails_DoesNotAdvance()
    {
        // Statuses look fine but the created book is wrong: the oracle must catch it.
        using var server = new StubServer((method, path) => (method, path) switch
        {
            ("POST", "/books") => StubServer.Json(201, "Created", """{"id":"b1","title":"Dune Messiah"}"""),
            ("GET", "/books/b1") => StubServer.Json(200, "OK", "{}"),
            _ => StubServer.Json(404, "Not Found", "{}")
        });

        var (runtime, _) = await PreparedAsync(new StubCandidateHost(server.BaseAddress), new StubBuild(true));
        await runtime.RunAsync(16);

        var status = Assert.Single(await runtime.StatusAsync());
        Assert.Equal("InReview", status.State);
    }

    [Fact]
    public async Task Review_GivenBuildFails_DoesNotStartTheCandidate()
    {
        using var server = new StubServer((_, _) => StubServer.Json(200, "OK", "{}"));
        var (runtime, _) = await PreparedAsync(new StubCandidateHost(server.BaseAddress), new StubBuild(false));
        await runtime.RunAsync(16);

        var status = Assert.Single(await runtime.StatusAsync());
        Assert.Equal("InReview", status.State);
    }

    [Fact]
    public async Task Review_GivenCandidateIdentityUnverified_DoesNotAdvance()
    {
        using var server = new StubServer((_, _) => StubServer.Json(201, "Created", """{"id":"b1","title":"Dune"}"""));
        var (runtime, _) = await PreparedAsync(new StubCandidateHost(server.BaseAddress, verified: false), new StubBuild(true));
        await runtime.RunAsync(16);

        var status = Assert.Single(await runtime.StatusAsync());
        Assert.Equal("InReview", status.State);
    }
}
