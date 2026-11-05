using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Permissions;
using Wip.Abstractions.Sessions;
using Wip.Abstractions.Workflows;
using Wip.Builder;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Runtime.Tests.Runtime;

public sealed class WorkflowStageCapabilityDispatcherTests
{
    private const string ChecklistItem =
        "Introduce `WorkflowStageCapabilityDispatcher` that resolves and invokes registered agent, tool, and validator capabilities for each compiled workflow stage using typed descriptors from `WorkflowBuilderStageCompiler` [depends on command semantics contract]";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task WorkflowStageCapabilityDispatcher_GivenRegisteredToolStage_ExpectedInvokeAsyncDispatchesWithTypedRequestResult()
    {
        var invocation = new InvocationTracker();
        var services = new ServiceCollection();
        var builder = services.AddWipCapabilities();
        builder.AddTool<DispatchProbeTool, DispatchProbeRequest, DispatchProbeResult>(
            new CapabilityId("tool.dispatch.probe"),
            "Dispatch probe tool");

        services.AddSingleton(invocation);

        var artifactStore = new InMemoryArtifactStore();
        using var provider = services.BuildServiceProvider();
        var dispatcher = new WorkflowStageCapabilityDispatcher(provider, builder.CapabilityDescriptors);
        var descriptor = WorkflowStageDescriptor.Create<DispatchProbeRequest, DispatchProbeResult>(WorkflowStageKind.Run);
        var session = CreateSessionSnapshot();
        var request = new DispatchProbeRequest("typed-dispatch");

        var result = await dispatcher.ExecuteStageAsync(
            descriptor,
            request,
            new WorkflowStageDispatchContext(session, builder, "corr-dispatch", ArtifactStore: artifactStore),
            new WorkflowStageMappedInputEvidence(
                descriptor.RequestContractName,
                request,
                descriptor.RequestContractName,
                request),
            CancellationToken.None);

        Assert.Equal("tool.dispatch.probe", result.ProducerCapabilityId.Value);
        Assert.Equal(typeof(DispatchProbeRequest).FullName, result.RequestContractName);
        Assert.Equal(typeof(DispatchProbeResult).FullName, result.ResultContractName);
        Assert.Equal("typed-dispatch", invocation.LastRequest?.Command);
        Assert.Equal("typed-dispatch:echo", Assert.IsType<DispatchProbeResult>(result.Result).Output);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task WorkflowStageCapabilityDispatcher_GivenMissingCapability_ExpectedDeterministicFailureBeforeStateMutation()
    {
        var store = new InMemorySessionStore();
        var publisher = new CollectingSessionEventPublisher();
        var orchestrator = new WipRuntimeOrchestrator(store, publisher);

        var services = new ServiceCollection();
        var builder = services.AddWipCapabilities();
        builder.AddWorkflow<MissingCapabilityWorkflow, MissingCapabilityWorkflowRequest, MissingCapabilityWorkflowResult>(
            workflowId: new WorkflowId("workflow.missing-capability"),
            displayName: "Missing capability workflow");
        builder.AddAgent<MissingCapabilityPlanAgent, PlanStageRequest, PlanStageResult>(
            new CapabilityId("agent.workflow.plan"),
            displayName: "Workflow plan stage agent");

        var snapshot = await orchestrator.StartSessionAsync(
            workflowId: new WorkflowId("workflow.missing-capability"),
            repositoryPath: TestRepositoryPaths.RepositoryRoot,
            worktreePath: TestRepositoryPaths.Worktree("missing-capability"),
            cancellationToken: CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await orchestrator.RunWorkflowAsync(
                snapshot.SessionId,
                builder,
                selectedWorkflowId: new WorkflowId("workflow.missing-capability"),
                services: builder.Services.BuildServiceProvider(),
                CancellationToken.None));

        Assert.Contains("requires a registered", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Tool", exception.Message, StringComparison.Ordinal);

        var persisted = await store.LoadAsync(snapshot.SessionId, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(SessionState.Created, persisted!.State);
        Assert.DoesNotContain(publisher.Events, e => e.Kind == SessionEventKind.WorkflowExecuted);
    }

    private static SessionSnapshot CreateSessionSnapshot()
        => new(
            SessionId: new SessionId("session-dispatch"),
            WorkflowId: new WorkflowId("workflow.dispatch"),
            State: SessionState.Created,
            RepositoryPath: TestRepositoryPaths.RepositoryRoot,
            WorktreePath: TestRepositoryPaths.Worktree("dispatch"),
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            TaskDescription: "dispatch probe");

    private sealed record DispatchProbeRequest(string Command);
    private sealed record DispatchProbeResult(string Output);

    private sealed class DispatchProbeTool : ITool<DispatchProbeRequest, DispatchProbeResult>
    {
        private readonly InvocationTracker _tracker;
        public DispatchProbeTool(InvocationTracker tracker) => _tracker = tracker;
        public ValueTask<DispatchProbeResult> ExecuteAsync(DispatchProbeRequest request, CapabilityContext context, CancellationToken cancellationToken)
        {
            _tracker.LastRequest = request;
            return ValueTask.FromResult(new DispatchProbeResult($"{request.Command}:echo"));
        }
    }

    private sealed class InvocationTracker { public DispatchProbeRequest? LastRequest { get; set; } }
    private sealed record MissingCapabilityWorkflowRequest(string Task);
    private sealed record MissingCapabilityWorkflowResult(string Outcome);

    private sealed class MissingCapabilityWorkflow : IWorkflow<MissingCapabilityWorkflowRequest, MissingCapabilityWorkflowResult>
    {
        public WorkflowId WorkflowId => new("workflow.missing-capability");
        public ValueTask<MissingCapabilityWorkflowResult> ExecuteAsync(MissingCapabilityWorkflowRequest request, WorkflowContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new MissingCapabilityWorkflowResult(request.Task));
    }

    private sealed class MissingCapabilityPlanAgent : IAgent<PlanStageRequest, PlanStageResult>
    {
        public ValueTask<PlanStageResult> ExecuteAsync(PlanStageRequest request, CapabilityContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new PlanStageResult(true));
    }

    private sealed class InMemorySessionStore : ISessionStore
    {
        private readonly ConcurrentDictionary<SessionId, SessionSnapshot> _sessions = new();
        public ValueTask SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken) { _sessions[snapshot.SessionId] = snapshot; return ValueTask.CompletedTask; }
        public ValueTask<SessionSnapshot?> LoadAsync(SessionId sessionId, CancellationToken cancellationToken)
            => ValueTask.FromResult(_sessions.TryGetValue(sessionId, out var snapshot) ? snapshot : null);
    }

    private sealed class CollectingSessionEventPublisher : ISessionEventPublisher
    {
        public List<SessionEvent> Events { get; } = [];
        public ValueTask PublishAsync(SessionEvent sessionEvent, CancellationToken cancellationToken) { Events.Add(sessionEvent); return ValueTask.CompletedTask; }
    }

    private sealed class InMemoryArtifactStore : IArtifactStore
    {
        public ValueTask<ArtifactDescriptor> SaveAsync(SessionId sessionId, ArtifactContent artifact, CancellationToken cancellationToken)
        {
            var descriptor = new ArtifactDescriptor(artifact.ArtifactId, sessionId, artifact.Kind,
                $"artifacts/{sessionId.Value}/{artifact.FileName}-{artifact.ArtifactId.Value}",
                artifact.ProducerType, artifact.ProducerVersion, artifact.ProducedAtUtc);
            return ValueTask.FromResult(descriptor);
        }
        public ValueTask<IReadOnlyList<ArtifactDescriptor>> ListAsync(SessionId sessionId, CancellationToken cancellationToken)
            => ValueTask.FromResult<IReadOnlyList<ArtifactDescriptor>>([]);
    }
    [Fact]
    public async Task WorkflowStageCapabilityDispatcher_GivenPluginCapabilityWithoutGrant_ExpectedPermissionDenied()
    {
        var services = new ServiceCollection();
        var builder = services.AddWipCapabilities();
        builder.AddTool<DispatchProbeTool, DispatchProbeRequest, DispatchProbeResult>(
            new CapabilityId("tool.dispatch.denied"),
            "Denied probe tool",
            requiredPermissions: [new CapabilityPermission(PermissionCatalog.WorkspaceWrite)],
            pluginOrigin: new PluginOrigin("plugin.denied", new CapabilityVersion("1.0.0"), "Plugin.Denied"));
        services.AddSingleton(new InvocationTracker());

        using var provider = services.BuildServiceProvider();
        var dispatcher = new WorkflowStageCapabilityDispatcher(provider, builder.CapabilityDescriptors);
        var descriptor = WorkflowStageDescriptor.Create<DispatchProbeRequest, DispatchProbeResult>(WorkflowStageKind.Run);
        var session = CreateSessionSnapshot();
        var request = new DispatchProbeRequest("denied");

        var exception = await Assert.ThrowsAsync<PermissionDeniedException>(() => dispatcher.ExecuteStageAsync(
            descriptor,
            request,
            new WorkflowStageDispatchContext(session, builder, "corr-denied"),
            new WorkflowStageMappedInputEvidence(descriptor.RequestContractName, request, descriptor.RequestContractName, request),
            CancellationToken.None).AsTask());

        Assert.Equal(PermissionCatalog.WorkspaceWrite, exception.Permission);
    }

    [Fact]
    public async Task WorkflowStageCapabilityDispatcher_GivenAuditMode_ExpectedCapabilityRunsAndDecisionIsAudited()
    {
        var auditSink = new List<PermissionDecision>();
        var services = new ServiceCollection();
        var builder = services.AddWipCapabilities();
        builder.AddTool<DispatchProbeTool, DispatchProbeRequest, DispatchProbeResult>(
            new CapabilityId("tool.dispatch.audited"),
            "Audited probe tool",
            requiredPermissions: [new CapabilityPermission(PermissionCatalog.ModelInvoke)],
            pluginOrigin: new PluginOrigin("plugin.audited", new CapabilityVersion("1.0.0"), "Plugin.Audited"));
        services.AddSingleton(new InvocationTracker());
        services.AddSingleton<IPermissionEvaluator>(
            new PermissionEvaluator(PermissionMode.Audit, auditSink: decision => auditSink.Add(decision)));

        var artifactStore = new InMemoryArtifactStore();
        using var provider = services.BuildServiceProvider();
        var dispatcher = new WorkflowStageCapabilityDispatcher(provider, builder.CapabilityDescriptors);
        var descriptor = WorkflowStageDescriptor.Create<DispatchProbeRequest, DispatchProbeResult>(WorkflowStageKind.Run);
        var session = CreateSessionSnapshot();
        var request = new DispatchProbeRequest("audited");

        var result = await dispatcher.ExecuteStageAsync(
            descriptor,
            request,
            new WorkflowStageDispatchContext(session, builder, "corr-audited", ArtifactStore: artifactStore),
            new WorkflowStageMappedInputEvidence(descriptor.RequestContractName, request, descriptor.RequestContractName, request),
            CancellationToken.None);

        Assert.Equal("tool.dispatch.audited", result.ProducerCapabilityId.Value);
        var decision = Assert.Single(auditSink);
        Assert.True(decision.Allowed);
        Assert.True(decision.Audited);
    }

    [Fact]
    public async Task WorkflowStageCapabilityDispatcher_GivenGrantedPluginCapability_ExpectedCapabilityRuns()
    {
        var services = new ServiceCollection();
        var builder = services.AddWipCapabilities();
        builder.AddTool<DispatchProbeTool, DispatchProbeRequest, DispatchProbeResult>(
            new CapabilityId("tool.dispatch.granted"),
            "Granted probe tool",
            requiredPermissions: [new CapabilityPermission(PermissionCatalog.WorkspaceWrite)],
            pluginOrigin: new PluginOrigin("plugin.granted", new CapabilityVersion("1.0.0"), "Plugin.Granted"));
        services.AddSingleton(new InvocationTracker());
        services.AddSingleton<IPermissionEvaluator>(
            new PermissionEvaluator(PermissionMode.Enforce, ["plugin.granted:workspace.write"]));

        var artifactStore = new InMemoryArtifactStore();
        using var provider = services.BuildServiceProvider();
        var dispatcher = new WorkflowStageCapabilityDispatcher(provider, builder.CapabilityDescriptors);
        var descriptor = WorkflowStageDescriptor.Create<DispatchProbeRequest, DispatchProbeResult>(WorkflowStageKind.Run);
        var session = CreateSessionSnapshot();
        var request = new DispatchProbeRequest("granted");

        var result = await dispatcher.ExecuteStageAsync(
            descriptor,
            request,
            new WorkflowStageDispatchContext(session, builder, "corr-granted", ArtifactStore: artifactStore),
            new WorkflowStageMappedInputEvidence(descriptor.RequestContractName, request, descriptor.RequestContractName, request),
            CancellationToken.None);

        Assert.Equal("tool.dispatch.granted", result.ProducerCapabilityId.Value);
    }
}