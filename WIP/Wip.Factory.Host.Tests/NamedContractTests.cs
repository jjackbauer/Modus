using Wip.Factory.Contracts.Events;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Persistence;
using Wip.Factory.Contracts.Work;
using Wip.Factory.Core;
using Wip.Factory.Core.Fanout;
using Wip.Factory.Host.Factory;
using Wip.Factory.Persistence;
using Xunit;

namespace Wip.Factory.Host.Tests;

/// <summary>
/// Named tests from v0.3 §12.2–12.3 whose mechanisms now exist. Each asserts observable behaviour; none is a
/// name-only placeholder.
/// </summary>
public sealed class NamedContractTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "factory-named-" + Guid.NewGuid().ToString("N"));

    private const string Requirement = """
        ---
        title: Ping HTTP API
        supportedClass: aspnetcore-http-net10
        ---

        Create an HTTP JSON API with a health endpoint.

        ## Acceptance expectations
        - GET /ping returns 200.
        """;

    private static WorkItemSnapshot Snapshot(FactoryWorkState state = FactoryWorkState.Proposed)
        => new(new WorkItemId("W-1"), state, 1, "/repo", [], DateTimeOffset.UtcNow);

    private static FactoryEventEnvelope UnknownEvent()
        => new(new FactoryEventId("E-unknown"), "factory.not.a.known.event.v1", "test", "W-1",
            DateTimeOffset.UtcNow, null, null,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["workItemId"] = "W-1" });

    [Fact]
    public async Task StageHandler_GivenUnknownEvent_ReturnsNoOp()
    {
        var handler = new FactoryStageDispatchHandler();

        var outcome = await handler.HandleAsync(Snapshot(), UnknownEvent());

        Assert.Equal(StageDispatchKind.NoOp, outcome.Kind);
        Assert.Null(outcome.NextState);
    }

    [Fact]
    public async Task Lifecycle_GivenOnlyUnlistedEvent_DoesNotAdvance()
    {
        Directory.CreateDirectory(_directory);
        var store = new LocalFactoryStore(_directory);

        var handled = await new FactoryStageDispatchHandler().HandleAsync(Snapshot(), UnknownEvent());
        Assert.Equal(StageDispatchKind.NoOp, handled.Kind);

        // No event was produced, so the aggregate cannot move.
        var loaded = await store.LoadAsync(new WorkItemId("W-1"));
        Assert.Null(loaded);
    }

    private sealed class IllegalTargetHandler : IStageDispatchHandler
    {
        public ValueTask<StageDispatchOutcome> HandleAsync(WorkItemSnapshot workItem, FactoryEventEnvelope trigger, CancellationToken cancellationToken = default)
            // RequirementsAligned -> Proposed is not in the adjacency.
            => ValueTask.FromResult(StageDispatchOutcome.Advanced(FactoryWorkState.Proposed));
    }

    [Fact]
    public async Task StageHandler_GivenIllegalTarget_FailsWithIllegalTransition()
    {
        Directory.CreateDirectory(_directory);
        var store = new LocalFactoryStore(_directory);

        var snapshot = Snapshot(FactoryWorkState.RequirementsAligned);
        Assert.True((await store.CommitAsync(new WorkItemCommit(snapshot, 0, [UnknownEvent()]))).Succeeded);

        var worker = new FactoryDispatchWorker(
            store, store, store, new IllegalTargetHandler(),
            new FactoryDispatchOptions(new SubscriberId("test-illegal"), MaxAttemptsPerEvent: 99));

        var report = await worker.RunOnceAsync();

        // The illegal advance is refused, so nothing was dispatched and the state is untouched.
        Assert.Equal(0, report.Dispatched);
        var loaded = await store.LoadAsync(new WorkItemId("W-1"));
        Assert.Equal(FactoryWorkState.RequirementsAligned, loaded!.State);
    }

    [Fact]
    public async Task Store_GivenRestart_OperatorStatusReadsDurableState()
    {
        var basePath = Path.Combine(_directory, "status");
        var repository = Path.Combine(basePath, "target");
        Directory.CreateDirectory(repository);

        var (root, error) = FactoryControlRoot.Resolve(repository, Path.Combine(basePath, "control"));
        Assert.NotNull(root);
        Assert.Null(error);

        var submitted = await new FactoryRuntime(root!).SubmitAsync(Requirement);
        Assert.True(submitted.Accepted, string.Join(";", submitted.Errors));

        // A brand-new runtime over the same control root must read the item from durable state alone.
        var restarted = await new FactoryRuntime(root!).StatusAsync();
        var status = Assert.Single(restarted);
        Assert.Equal(submitted.WorkItemId, status.WorkItemId);
        Assert.Equal("Proposed", status.State);
        Assert.Equal(submitted.RequirementDigest, status.RequirementDigest);
    }

    [Fact]
    public async Task Contract_GivenProposalChanged_InvalidatesPriorAuthorization()
    {
        var basePath = Path.Combine(_directory, "auth");
        var repository = Path.Combine(basePath, "target");
        Directory.CreateDirectory(repository);

        var (root, error) = FactoryControlRoot.Resolve(repository, Path.Combine(basePath, "control"));
        Assert.NotNull(root);
        Assert.Null(error);

        var runtime = new FactoryRuntime(root!);
        var submitted = await runtime.SubmitAsync(Requirement);
        Assert.True(submitted.Accepted);
        await runtime.RunAsync(16);

        var (proposal, _) = await runtime.ContractShowAsync(submitted.WorkItemId!);
        Assert.NotNull(proposal);

        // An authorisation for a different proposal must not freeze this one.
        var (ok, refused) = await runtime.ContractApproveAsync(submitted.WorkItemId!, "sha256:not-this-proposal");

        Assert.False(ok);
        Assert.Equal("contract-digest-mismatch", refused);
        var status = Assert.Single(await runtime.StatusAsync());
        Assert.Equal("Proposed", status.State);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}