using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Wip.Factory.Host.Factory;
using Xunit;

namespace Wip.Factory.Host.Tests;

/// <summary>
/// PA-04's core: the frozen contract is an oracle that can reject an implementation. These tests drive real
/// HTTP over a loopback socket against scripted servers, so "passes" and "fails" are decided by bytes on the
/// wire rather than by an in-process shortcut.
/// </summary>
public sealed class AcceptanceContractTests
{
    private sealed class StubServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;
        private readonly Func<string, string, string> _respond;

        public StubServer(Func<string, string, string> respond)
        {
            _respond = respond;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            BaseAddress = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
            _loop = Task.Run(AcceptLoopAsync);
        }

        public Uri BaseAddress { get; }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (SocketException)
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

                            var firstLine = request.Split("\r\n")[0].Split(' ');
                            var method = firstLine[0];
                            var path = firstLine.Length > 1 ? firstLine[1] : "/";

                            var response = _respond(method, path);
                            await stream.WriteAsync(Encoding.UTF8.GetBytes(response), _cts.Token);
                            await stream.FlushAsync(_cts.Token);
                        }
                        catch (Exception)
                        {
                            // a stub that never answers is a failed scenario, which the runner reports
                        }
                    }
                });
            }
        }

        public static string Respond(int status, string reason, string json)
            => $"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(json)}\r\nConnection: close\r\n\r\n{json}";

        public static string Respond(int status, string reason, string contentType, string body)
            => $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            try
            {
                _loop.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
            }

            _cts.Dispose();
        }
    }

    private const string ContractJson = """
        {
          "schemaVersion": 1,
          "supportedClass": "aspnetcore-http-net10",
          "scenarios": [
            {
              "id": "AC-1",
              "method": "POST",
              "path": "/books",
              "request": { "json": { "title": "Dune", "author": "Herbert" } },
              "expect": { "status": 201, "jsonSubset": { "title": "Dune", "author": "Herbert" } },
              "capture": { "bookId": "$.id" }
            },
            {
              "id": "AC-2",
              "method": "GET",
              "path": "/books/{bookId}",
              "expect": { "status": 200, "jsonSubset": { "title": "Dune" } }
            },
            {
              "id": "AC-3",
              "method": "DELETE",
              "path": "/books/{bookId}",
              "expect": { "status": 204 }
            }
          ]
        }
        """;

    private static AcceptanceContract Contract()
    {
        var (contract, errors) = AcceptanceContract.Parse(ContractJson);
        Assert.NotNull(contract);
        Assert.Empty(errors);
        return contract!;
    }

    [Fact]
    public async Task Contract_GivenCorrectImplementation_PassesEveryScenario()
    {
        using var server = new StubServer((method, path) => (method, path) switch
        {
            ("POST", "/books") => StubServer.Respond(201, "Created", """{"id":"b1","title":"Dune","author":"Herbert"}"""),
            ("GET", "/books/b1") => StubServer.Respond(200, "OK", """{"id":"b1","title":"Dune","author":"Herbert"}"""),
            ("DELETE", "/books/b1") => StubServer.Respond(204, "No Content", ""),
            _ => StubServer.Respond(404, "Not Found", "{}")
        });

        var result = await new AcceptanceContractRunner().RunAsync(Contract(), server.BaseAddress);

        Assert.True(result.Passed, result.FailureSummary);
        Assert.Equal(3, result.Scenarios.Count);
        Assert.All(result.Scenarios, static s => Assert.True(s.Passed));
    }

    [Fact]
    public async Task Contract_GivenWrongImplementation_FailsOriginalScenarios()
    {
        // A plausible but wrong implementation: creation answers 200 instead of 201.
        using var server = new StubServer((method, path) => (method, path) switch
        {
            ("POST", "/books") => StubServer.Respond(200, "OK", """{"id":"b1","title":"Dune","author":"Herbert"}"""),
            ("GET", "/books/b1") => StubServer.Respond(200, "OK", """{"id":"b1","title":"Dune","author":"Herbert"}"""),
            ("DELETE", "/books/b1") => StubServer.Respond(204, "No Content", ""),
            _ => StubServer.Respond(404, "Not Found", "{}")
        });

        var result = await new AcceptanceContractRunner().RunAsync(Contract(), server.BaseAddress);

        Assert.False(result.Passed);

        // The first scenario fails on status. Its capture never runs, so the later scenarios cannot
        // substitute their placeholder and fail too — a cascade the runner reports rather than hides.
        var first = result.Scenarios.Single(static s => s.Id == "AC-1");
        Assert.False(first.Passed);
        Assert.Contains(AcceptanceContractRunner.StatusMismatch, first.Reason, StringComparison.Ordinal);
        Assert.Equal(200, first.ObservedStatus);
        Assert.All(result.Scenarios.Where(static s => s.Id != "AC-1"), static s => Assert.False(s.Passed));
    }

    [Fact]
    public async Task Contract_GivenWrongFieldValue_FailsSubsetMatch()
    {
        using var server = new StubServer((method, path) => (method, path) switch
        {
            ("POST", "/books") => StubServer.Respond(201, "Created", """{"id":"b1","title":"Dune","author":"Herbert"}"""),
            // The stored title changed: a passing status is not a passing contract.
            ("GET", "/books/b1") => StubServer.Respond(200, "OK", """{"id":"b1","title":"Dune Messiah"}"""),
            ("DELETE", "/books/b1") => StubServer.Respond(204, "No Content", ""),
            _ => StubServer.Respond(404, "Not Found", "{}")
        });

        var result = await new AcceptanceContractRunner().RunAsync(Contract(), server.BaseAddress);

        Assert.False(result.Passed);
        var failed = Assert.Single(result.Scenarios.Where(static s => !s.Passed));
        Assert.Equal("AC-2", failed.Id);
        Assert.Contains(AcceptanceContractRunner.SubsetMismatch, failed.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Contract_GivenScenarioCaptureAndSubstitution_ExecutesInOrder()
    {
        var seen = new List<string>();
        using var server = new StubServer((method, path) =>
        {
            lock (seen)
            {
                seen.Add(method + " " + path);
            }

            return (method, path) switch
            {
                ("POST", "/books") => StubServer.Respond(201, "Created", """{"id":"generated-42","title":"Dune","author":"Herbert"}"""),
                ("GET", "/books/generated-42") => StubServer.Respond(200, "OK", """{"title":"Dune"}"""),
                ("DELETE", "/books/generated-42") => StubServer.Respond(204, "No Content", ""),
                _ => StubServer.Respond(404, "Not Found", "{}")
            };
        });

        var result = await new AcceptanceContractRunner().RunAsync(Contract(), server.BaseAddress);

        Assert.True(result.Passed, result.FailureSummary);
        lock (seen)
        {
            Assert.Equal(["POST /books", "GET /books/generated-42", "DELETE /books/generated-42"], seen);
        }
    }

    [Fact]
    public void Contract_GivenAbsoluteUrlOrMissingStatus_IsRefused()
    {
        var (absolute, absoluteErrors) = AcceptanceContract.Parse("""
            {"schemaVersion":1,"supportedClass":"aspnetcore-http-net10","scenarios":[
              {"id":"AC-1","method":"GET","path":"http://evil.test/books","expect":{"status":200}}]}
            """);
        Assert.Null(absolute);
        Assert.Contains(absoluteErrors, static e => e.StartsWith(AcceptanceContract.UnsupportedFormCategory, StringComparison.Ordinal));

        var (noStatus, noStatusErrors) = AcceptanceContract.Parse("""
            {"schemaVersion":1,"supportedClass":"aspnetcore-http-net10","scenarios":[
              {"id":"AC-1","method":"GET","path":"/books"}]}
            """);
        Assert.Null(noStatus);
        Assert.Contains(noStatusErrors, static e => e.StartsWith(AcceptanceContract.InvalidCategory, StringComparison.Ordinal));

        var (wrongClass, _) = AcceptanceContract.Parse("""
            {"schemaVersion":1,"supportedClass":"python-desktop","scenarios":[
              {"id":"AC-1","method":"GET","path":"/books","expect":{"status":200}}]}
            """);
        Assert.Null(wrongClass);
    }

    [Fact]
    public void Contract_GivenSubsetAndArrayAssertions_UsesSpecifiedSemantics()
    {
        using var expected = JsonDocument.Parse("""{"a":1,"b":["x","y"],"c":{"d":true}}""");
        using var superset = JsonDocument.Parse("""{"a":1,"b":["x","y"],"c":{"d":true,"e":2},"extra":"allowed"}""");
        using var wrongArray = JsonDocument.Parse("""{"a":1,"b":["y","x"],"c":{"d":true}}""");
        using var wrongScalar = JsonDocument.Parse("""{"a":1.0,"b":["x","y"],"c":{"d":true}}""");

        Assert.True(JsonSubset.Matches(superset.RootElement, expected.RootElement, out _));
        Assert.False(JsonSubset.Matches(wrongArray.RootElement, expected.RootElement, out var arrayPath));
        // Arrays are ordered, so the first differing element is reported, not the array as a whole.
        Assert.Contains("$.b[0]", arrayPath, StringComparison.Ordinal);
        // 1 and 1.0 compare equal numerically; the mismatched field is caught by structure, not formatting.
        Assert.True(JsonSubset.Matches(wrongScalar.RootElement, expected.RootElement, out _));
    }

    [Fact]
    public async Task Intake_GivenRequirementWithContract_ProposesAnExecutableOracle()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "factory-contract-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(basePath, "target");
        Directory.CreateDirectory(repository);

        var (root, error) = FactoryControlRoot.Resolve(repository, Path.Combine(basePath, "control"));
        Assert.NotNull(root);
        Assert.Null(error);

        var requirement = """
            ---
            title: Book catalog HTTP API
            supportedClass: aspnetcore-http-net10
            acceptanceContract: contract.json
            ---

            Create an HTTP JSON API to add, retrieve and delete books.

            ## Acceptance expectations
            - Creating a book returns 201 with an identifier.
            """;

        var runtime = new FactoryRuntime(root!);
        var submit = await runtime.SubmitAsync(requirement, null, ContractJson);
        Assert.True(submit.Accepted, string.Join(";", submit.Errors));

        await runtime.RunAsync(16);

        var (proposal, showError) = await runtime.ContractShowAsync(submit.WorkItemId!);
        Assert.Null(showError);
        Assert.NotNull(proposal);
        Assert.True(proposal!.Executable);
        Assert.Equal(Contract().Digest, proposal.ExecutableContractDigest);
    }
}