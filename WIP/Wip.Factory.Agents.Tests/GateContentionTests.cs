using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Agents;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Builder;
using Wip.Factory.Adaptation;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Profiles;
using Wip.Factory.Wip.Fanout;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Factory.Agents.Tests;

public sealed class GateContentionTests
{
    private static readonly SessionId Session = new("session-contention");

    private sealed class SessionStore : ISessionStore
    {
        private readonly Dictionary<string, SessionSnapshot> _snapshots = new(StringComparer.Ordinal);

        public ValueTask SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
        {
            _snapshots[snapshot.SessionId.Value] = snapshot;
            return ValueTask.CompletedTask;
        }

        public ValueTask<SessionSnapshot?> LoadAsync(SessionId sessionId, CancellationToken cancellationToken)
            => ValueTask.FromResult(_snapshots.TryGetValue(sessionId.Value, out var snapshot) ? snapshot : null);
    }

    private sealed class NoOpPublisher : ISessionEventPublisher
    {
        public ValueTask PublishAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }

    private sealed class CapturingExecutor : IAgentExecutor
    {
        public ValueTask<AgentExecutionResult> ExecuteAsync(
            AgentExecutionRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new AgentExecutionResult(
                AgentExecutionStatus.Completed,
                ChatMessage.Assistant("dispatched while the gate was held"),
                [],
                new ModelProviderUsage(5, 2),
                null));
    }

    private sealed class UnusedChatModel : IChatModel
    {
        public ValueTask<ChatResponse> CompleteAsync(ChatRequest request, CapabilityContext context, CancellationToken cancellationToken)
            => throw new InvalidOperationException("unused");

        public IAsyncEnumerable<StreamingChunk> StreamAsync(ChatRequest request, CapabilityContext context, CancellationToken cancellationToken)
            => throw new InvalidOperationException("unused");
    }

    [Fact]
    public async Task Dispatcher_GivenDispatchDuringGatedOperation_CompletesWithoutAuthorityGateContention()
    {
        var store = new SessionStore();
        await store.SaveAsync(
            new SessionSnapshot(
                Session,
                new WorkflowId("workflow.linear"),
                SessionState.Editing,
                RepositoryPath: "repo",
                WorktreePath: string.Empty,
                UpdatedAtUtc: DateTimeOffset.UtcNow),
            CancellationToken.None);

        var orchestrator = new WipRuntimeOrchestrator(store, new NoOpPublisher());

        var gateHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // The observer runs while the real semaphore is held, so the gate genuinely stays closed until
        // the dispatch has completed — not a synthetic lock standing in for it.
        WipRuntimeOrchestrator.GateHeldObserver = async _ =>
        {
            gateHeld.TrySetResult();
            await releaseGate.Task.ConfigureAwait(false);
        };

        try
        {
            var gatedOperation = Task.Run(async () =>
                await orchestrator.DetachSessionAsync(CancellationToken.None).AsTask());

            await gateHeld.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var services = new ServiceCollection();
            services.AddSingleton<IAgentExecutor, CapturingExecutor>();
            services.AddSingleton<IChatModel, UnusedChatModel>();
            await using var provider = services.BuildServiceProvider();

            var builder = new WipBuilder(services);
            var catalog = new CapabilityCatalog(
            [
                new CapabilityCatalogEntry(new CapabilityId("tool.repo-read"), new CapabilityVersion("1.0.0"))
            ]);

            var registryRoot = Path.Combine(Path.GetTempPath(), "wip-gate-" + Guid.NewGuid().ToString("N"));
            var registry = new LocalAgentProfileRegistry(registryRoot, catalog, FactoryRolePack.Definitions);
            var profile = FactoryRolePack.Profiles[0];
            await registry.PublishAsync(profile);

            var dispatcher = new FactoryAgentDispatcher(
                registry,
                FactoryRolePack.Definitions,
                catalog,
                new AgentProfilePermissionResolver(),
                new WipRuntimeAgentExecutorPort(orchestrator),
                provider.GetRequiredService<IServiceScopeFactory>(),
                builder);

            // The gated operation is still in flight here, so this dispatch must not be blocked by it.
            Assert.False(gatedOperation.IsCompleted);

            var dispatch = await dispatcher
                .ExecuteAsync(
                    new FactoryWorkDispatch(
                        Session,
                        new WorkItemId("W-25"),
                        new StageName("requirements"),
                        profile.DefinitionId,
                        new AgentProfileVersion(1),
                        "dispatch while the authority gate is held"))
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(15));

            Assert.True(dispatch.Completed, dispatch.FailureCategory);
            Assert.Equal(5, dispatch.InputTokens);
            Assert.Equal(2, dispatch.OutputTokens);

            releaseGate.TrySetResult();
            await gatedOperation.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            WipRuntimeOrchestrator.GateHeldObserver = null;
            releaseGate.TrySetResult();
        }
    }
}
