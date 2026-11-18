using System.Net;
using System.Net.Sockets;
using System.Text;
using Wip.Factory.Host.Factory;
using Xunit;

namespace Wip.Factory.Host.Tests;

/// <summary>
/// The merge lifecycle: a separate operator authorisation, a pre-merge drift guard, the real merge, and
/// verification of the merged revision. A merged candidate that fails verification is never Released.
/// </summary>
public sealed class MergeLifecycleTests
{
    private static readonly string ContractJson = """
        {
          "schemaVersion": 1,
          "supportedClass": "aspnetcore-http-net10",
          "scenarios": [
            { "id": "AC-1", "method": "POST", "path": "/books",
              "request": { "json": { "title": "Dune" } },
              "expect": { "status": 201, "jsonSubset": { "title": "Dune" } },
              "capture": { "bookId": "$.id" } }
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
        private readonly Func<string> _body;

        public StubServer(string createdBody) : this(() => createdBody)
        {
        }

        public StubServer(Func<string> body)
        {
            _body = body;
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
                            var body = _body();
                            var response = $"HTTP/1.1 201 Created\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
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
            => ValueTask.FromResult(new CandidateSession(_address, 4242, true, static () => ValueTask.CompletedTask));
    }

    private sealed class StubBuild : ICandidateBuildVerifier
    {
        public ValueTask<(bool Ok, string Detail)> VerifyAsync(string worktreePath, CancellationToken cancellationToken = default)
            => ValueTask.FromResult((true, "build passed"));
    }

    private sealed class StubMerge : IMergeEngine
    {
        private readonly bool _ok;

        public StubMerge(bool ok) => _ok = ok;

        public int MergeCalls { get; private set; }

        public ValueTask<MergeResult> MergeAsync(string repositoryPath, string headSha, CancellationToken cancellationToken = default)
        {
            MergeCalls++;
            return ValueTask.FromResult(_ok
                ? MergeResult.Ok_("merged-sha-1234567890")
                : MergeResult.Refused("merge-conflict:CONFLICT"));
        }

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

    private static async Task<(FactoryRuntime Runtime, string WorkItem)> AtReadyForDeliveryAsync(
        ICandidateHost host,
        IMergeEngine merge,
        Func<CancellationToken, Task<string?>>? targetHead = null,
        Func<MergeAuthority?>? evidence = null)
    {
        var basePath = Path.Combine(Path.GetTempPath(), "factory-merge-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(basePath, "target");
        var worktree = Path.Combine(basePath, "worktree");
        Directory.CreateDirectory(repository);
        Directory.CreateDirectory(worktree);
        await File.WriteAllTextAsync(Path.Combine(worktree, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var (root, error) = FactoryControlRoot.Resolve(repository, Path.Combine(basePath, "control"));
        Assert.NotNull(root);
        Assert.Null(error);

        var handler = new FactoryStageDispatchHandler(
            new ScriptedRunner(worktree),
            host,
            new StubBuild(),
            merges: merge,
            verificationRoot: Path.Combine(basePath, "verify"),
            targetHead: targetHead,
            currentEvidence: evidence is null ? null : (_, _) => Task.FromResult(evidence()));

        var runtime = new FactoryRuntime(root!, handler: handler);
        var submit = await runtime.SubmitAsync(Requirement, null, ContractJson);
        Assert.True(submit.Accepted, string.Join(";", submit.Errors));

        await runtime.RunAsync(16);
        var (proposal, _) = await runtime.ContractShowAsync(submit.WorkItemId!);
        Assert.True((await runtime.ContractApproveAsync(submit.WorkItemId!, proposal!.ProposalDigest)).Ok);

        await runtime.RunAsync(16);
        return (runtime, submit.WorkItemId!);
    }

    private static async Task<string> ReviewDigestAsync(FactoryRuntime runtime, string workItem)
    {
        var status = Assert.Single(await runtime.StatusAsync());
        Assert.Equal("ReadyForDelivery", status.State);
        foreach (var value in await runtime.EventsAsync(workItem))
        {
            if (value.Type == OperatorEvents.ReviewAccepted && value.Data.TryGetValue("reviewDigest", out var digest))
            {
                return digest;
            }
        }

        throw new InvalidOperationException("no review digest recorded");
    }

    [Fact]
    public async Task Merge_GivenApprovedReview_MergesAndVerifiesTheMergedRevision()
    {
        using var server = new StubServer("""{"id":"b1","title":"Dune"}""");
        var merge = new StubMerge(ok: true);
        var (runtime, workItem) = await AtReadyForDeliveryAsync(new StubCandidateHost(server.BaseAddress), merge, _ => Task.FromResult<string?>("base-1"));

        var digest = await ReviewDigestAsync(runtime, workItem);
        var approved = await runtime.ApproveMergeAsync(workItem, digest);
        Assert.True(approved.Ok, approved.Error);

        await runtime.RunAsync(16);

        var status = Assert.Single(await runtime.StatusAsync());
        Assert.Equal("Released", status.State);
        Assert.Equal(1, merge.MergeCalls);
    }

    [Fact]
    public async Task Merge_GivenWrongReviewDigest_RefusesApproval()
    {
        using var server = new StubServer("""{"id":"b1","title":"Dune"}""");
        var (runtime, workItem) = await AtReadyForDeliveryAsync(new StubCandidateHost(server.BaseAddress), new StubMerge(ok: true));

        var approved = await runtime.ApproveMergeAsync(workItem, "sha256:not-the-review");

        Assert.False(approved.Ok);
        Assert.Equal("review-digest-mismatch", approved.Error);
        var status = Assert.Single(await runtime.StatusAsync());
        Assert.Equal("ReadyForDelivery", status.State);
    }

    [Fact]
    public async Task Merge_GivenTargetDrift_DoesNotMerge()
    {
        using var server = new StubServer("""{"id":"b1","title":"Dune"}""");
        var merge = new StubMerge(ok: true);
        // The target branch moved after the approval was issued.
        var (runtime, workItem) = await AtReadyForDeliveryAsync(
            new StubCandidateHost(server.BaseAddress), merge, _ => Task.FromResult<string?>("a-different-base"));

        var digest = await ReviewDigestAsync(runtime, workItem);
        Assert.True((await runtime.ApproveMergeAsync(workItem, digest)).Ok);

        await runtime.RunAsync(16);

        var status = Assert.Single(await runtime.StatusAsync());
        Assert.Equal("DeliveryValidation", status.State);
        Assert.Equal(0, merge.MergeCalls);
    }

    [Fact]
    public async Task Merge_GivenPostMergeContractFails_IsNotReleased()
    {
        // The pre-merge candidate passes, then the merged revision behaves differently.
        var broken = false;
        using var server = new StubServer(() => broken ? """{"id":"b1","title":""}""" : """{"id":"b1","title":"Dune"}""");
        var merge = new StubMerge(ok: true);
        var (runtime, workItem) = await AtReadyForDeliveryAsync(new StubCandidateHost(server.BaseAddress), merge, _ => Task.FromResult<string?>("base-1"));

        var digest = await ReviewDigestAsync(runtime, workItem);
        Assert.True((await runtime.ApproveMergeAsync(workItem, digest)).Ok);

        // Only the merged revision is broken.
        broken = true;

        await runtime.RunAsync(16);

        var status = Assert.Single(await runtime.StatusAsync());
        Assert.NotEqual("Released", status.State);
    }

    [Fact]
    public async Task Merge_GivenMergeConflict_DoesNotRelease()
    {
        using var server = new StubServer("""{"id":"b1","title":"Dune"}""");
        var (runtime, workItem) = await AtReadyForDeliveryAsync(
            new StubCandidateHost(server.BaseAddress), new StubMerge(ok: false), _ => Task.FromResult<string?>("base-1"));

        var digest = await ReviewDigestAsync(runtime, workItem);
        Assert.True((await runtime.ApproveMergeAsync(workItem, digest)).Ok);

        await runtime.RunAsync(16);

        var status = Assert.Single(await runtime.StatusAsync());
        Assert.NotEqual("Released", status.State);
    }


    [Fact]
    public async Task Approval_GivenMatchingEvidence_MergesAndReleases()
    {
        using var server = new StubServer("""{"id":"b1","title":"Dune"}""");
        var merge = new StubMerge(ok: true);

        // The evidence lookup is lazy: the test fills it with the durable values once review has happened.
        MergeAuthority? captured = null;
        var (runtime, workItem) = await AtReadyForDeliveryAsync(
            new StubCandidateHost(server.BaseAddress), merge, _ => Task.FromResult<string?>("base-1"), () => captured);

        var review = (await runtime.EventsAsync(workItem)).Last(static e => e.Type == OperatorEvents.ReviewAccepted);
        captured = new MergeAuthority(
            review.Data["reviewDigest"], review.Data["headSha"], review.Data["baseSha"], review.Data["contractDigest"],
            review.Data["policyVersion"], review.Data["guidelinesVersion"], review.Data["profileVersion"], review.Data["runnerVersion"]);

        Assert.True((await runtime.ApproveMergeAsync(workItem, review.Data["reviewDigest"])).Ok);
        await runtime.RunAsync(16);

        var status = Assert.Single(await runtime.StatusAsync());
        Assert.Equal("Released", status.State);
        Assert.Equal(1, merge.MergeCalls);
    }

    [Theory]
    [InlineData("reviewDigest")]
    [InlineData("headSha")]
    [InlineData("baseSha")]
    [InlineData("contractDigest")]
    [InlineData("policyVersion")]
    [InlineData("guidelinesVersion")]
    [InlineData("profileVersion")]
    [InlineData("runnerVersion")]
    public async Task Approval_GivenAnyBoundFieldChanged_RefusesMerge(string changedField)
    {
        using var server = new StubServer("""{"id":"b1","title":"Dune"}""");
        var merge = new StubMerge(ok: true);

        // Every bound fact matches except one, which now differs from durable evidence.
        var evidence = new MergeAuthority(
            ReviewDigest: changedField == "reviewDigest" ? "sha256:changed" : "unused-matches",
            HeadSha: changedField == "headSha" ? "a-different-head" : "unused-matches",
            BaseSha: changedField == "baseSha" ? "a-different-base" : "unused-matches",
            ContractDigest: changedField == "contractDigest" ? "sha256:changed" : "unused-matches",
            PolicyVersion: changedField == "policyVersion" ? "policy://changed@2" : FactoryPolicy.PolicyVersion,
            GuidelinesVersion: changedField == "guidelinesVersion" ? "guidelines/changed@2" : FactoryPolicy.GuidelinesVersion,
            ProfileVersion: changedField == "profileVersion" ? "profile/changed@2" : FactoryPolicy.ProfileVersion,
            RunnerVersion: changedField == "runnerVersion" ? "runner@2" : FactoryPolicy.RunnerVersion);

        var (runtime, workItem) = await AtReadyForDeliveryAsync(
            new StubCandidateHost(server.BaseAddress), merge, _ => Task.FromResult<string?>("base-1"), () => evidence);

        var digest = await ReviewDigestAsync(runtime, workItem);
        Assert.True((await runtime.ApproveMergeAsync(workItem, digest)).Ok);

        await runtime.RunAsync(16);

        var status = Assert.Single(await runtime.StatusAsync());
        Assert.Equal("DeliveryValidation", status.State);
        Assert.Equal(0, merge.MergeCalls);
    }
}