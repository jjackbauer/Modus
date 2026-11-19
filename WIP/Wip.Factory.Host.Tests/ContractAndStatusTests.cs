using Wip.Factory.Contracts.Events;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Work;
using Wip.Factory.Core;
using Wip.Factory.Core.Fanout;
using Wip.Factory.Host.Factory;
using Xunit;

namespace Wip.Factory.Host.Tests;

/// <summary>
/// Frozen-contract integrity and operator-visible failure state: bytes that do not hash to the approved digest never
/// reach review, and status surfaces the last durable failure rather than a narrative.
/// </summary>
public sealed class ContractAndStatusTests
{
    private const string ContractJson = """
        {
          "schemaVersion": 1,
          "supportedClass": "aspnetcore-http-net10",
          "scenarios": [
            { "id": "AC-1", "method": "GET", "path": "/ping", "expect": { "status": 200 } }
          ]
        }
        """;

    private const string Requirement = """
        ---
        title: Ping HTTP API
        supportedClass: aspnetcore-http-net10
        ---

        Create an HTTP JSON API with a health endpoint.

        ## Acceptance expectations
        - GET /ping returns 200.
        """;

    [Fact]
    public async Task Intake_GivenTamperedFrozenContract_BlocksWorkItem()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "factory-tamper-" + Guid.NewGuid().ToString("N"));
        var worktree = Path.Combine(basePath, "worktree");
        FactoryTestWorkspace.Write(worktree);

        var handler = new FactoryStageDispatchHandler(verificationRoot: Path.Combine(basePath, "verify"));
        var snapshot = new WorkItemSnapshot(
            new WorkItemId("WORK-tamper"), FactoryWorkState.InReview, 4, basePath, [], DateTimeOffset.UtcNow);

        var trigger = new FactoryEventEnvelope(
            new FactoryEventId("E-tamper"),
            OperatorEvents.ImplementationCompleted,
            "test",
            "WORK-tamper",
            DateTimeOffset.UtcNow,
            null,
            null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["workItemId"] = "WORK-tamper",
                ["worktreePath"] = worktree,
                ["headSha"] = "head-1",
                ["baseSha"] = "base-1",
                // The bytes no longer hash to the digest that was frozen at intake.
                ["acceptanceContractDigest"] = "sha256:approved-before-tampering",
                ["acceptanceContract"] = ContractJson
            });

        var outcome = await handler.HandleAsync(snapshot, trigger);

        Assert.Equal(StageDispatchKind.Failed, outcome.Kind);
        Assert.Equal("contract-tampered", outcome.FailureCategory);
    }

    [Fact]
    public async Task Host_GivenStatus_ReportsStateVersionDigestAndLastFailure()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "factory-status-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(basePath, "target");
        Directory.CreateDirectory(repository);

        var (root, error) = FactoryControlRoot.Resolve(repository, Path.Combine(basePath, "control"));
        Assert.NotNull(root);
        Assert.Null(error);

        // No implementation runner: the stage fails with a durable named category.
        var runtime = new FactoryRuntime(root!);
        var submit = await runtime.SubmitAsync(Requirement);
        Assert.True(submit.Accepted, string.Join(";", submit.Errors));

        await runtime.RunAsync(16);
        var (proposal, _) = await runtime.ContractShowAsync(submit.WorkItemId!);
        Assert.NotNull(proposal);
        Assert.True((await runtime.ContractApproveAsync(submit.WorkItemId!, proposal!.ProposalDigest)).Ok);

        await runtime.RunAsync(32);

        var status = Assert.Single(await runtime.StatusAsync());
        Assert.Equal("InImplementation", status.State);
        Assert.True(status.Version > 1);
        Assert.StartsWith("sha256:", status.RequirementDigest, StringComparison.Ordinal);
        Assert.Equal(proposal.ProposalDigest, status.FrozenContractDigest);
        Assert.NotNull(status.LastFailure);
        Assert.Contains("implementation-runner-unavailable", status.LastFailure, StringComparison.Ordinal);
    }
}
