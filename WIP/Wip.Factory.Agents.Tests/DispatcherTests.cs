using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Agents;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Builder;
using Wip.Factory.Adaptation;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Plugins;
using Wip.Factory.Contracts.Profiles;
using Wip.Factory.Wip.Fanout;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Factory.Agents.Tests;

public sealed class DispatcherTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "wip-factory-dispatcher-tests", Guid.NewGuid().ToString("N"));

    private static readonly SessionId Session = new("session-1");

    private static readonly CapabilityCatalog Catalog = new(
    [
        new CapabilityCatalogEntry(new CapabilityId("tool.repo-read"), new CapabilityVersion("1.0.0"))
    ]);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private sealed class Marker
    {
    }

    private sealed class RecordingPort : IFactoryAgentExecutionPort
    {
        private readonly Func<AgentExecutionResult> _result;

        public RecordingPort(Func<AgentExecutionResult> result) => _result = result;

        public AgentExecutionRequest? Request { get; private set; }

        public object? ResolvedMarker { get; private set; }

        public int Invocations { get; private set; }

        public ValueTask<AgentExecutionResult> ExecuteAsync(
            AgentExecutionRequest request,
            WipBuilder builder,
            IServiceProvider scopedServices,
            CancellationToken cancellationToken = default)
        {
            Invocations++;
            Request = request;
            ResolvedMarker = scopedServices.GetService(typeof(Marker));
            return ValueTask.FromResult(_result());
        }
    }

    private sealed class TestSessionStore : ISessionStore
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

    private sealed class NoOpEventPublisher : ISessionEventPublisher
    {
        public ValueTask PublishAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }

    private sealed class CapturingExecutor : IAgentExecutor
    {
        public CapabilityContext? CapturedContext { get; private set; }

        public ValueTask<AgentExecutionResult> ExecuteAsync(
            AgentExecutionRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            CapturedContext = context;
            return ValueTask.FromResult(new AgentExecutionResult(
                AgentExecutionStatus.Completed,
                ChatMessage.Assistant("done"),
                [],
                new ModelProviderUsage(3, 2),
                null));
        }
    }

    private sealed class UnusedChatModel : IChatModel
    {
        public ValueTask<ChatResponse> CompleteAsync(
            ChatRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("The chat model must not be used when an executor is registered.");

        public IAsyncEnumerable<StreamingChunk> StreamAsync(
            ChatRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("The chat model must not be used when an executor is registered.");
    }

    private (FactoryAgentDispatcher Dispatcher, ServiceProvider Provider, LocalAgentProfileRegistry Registry) Build(
        IFactoryAgentExecutionPort port,
        ICapabilityCatalog? catalog = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<Marker>();
        services.AddSingleton<IChatModel, UnusedChatModel>();
        var provider = services.BuildServiceProvider();

        var builder = new WipBuilder(services);
        var registry = new LocalAgentProfileRegistry(
            _directory,
            catalog ?? Catalog,
            FactoryRolePack.Definitions);

        var dispatcher = new FactoryAgentDispatcher(
            registry,
            FactoryRolePack.Definitions,
            catalog ?? Catalog,
            new AgentProfilePermissionResolver(),
            port,
            provider.GetRequiredService<IServiceScopeFactory>(),
            builder);

        return (dispatcher, provider, registry);
    }

    private static FactoryWorkDispatch Dispatch(string definitionId = "factory.requirements-analyst", int version = 1) =>
        new(
            Session,
            new WorkItemId("W-1"),
            new StageName("requirements"),
            new AgentDefinitionId(definitionId),
            new AgentProfileVersion(version),
            "Align the requirement");

    private static AgentExecutionResult Completion(string content = "done", int input = 0, int output = 0) =>
        new(AgentExecutionStatus.Completed, ChatMessage.Assistant(content), [], new ModelProviderUsage(input, output), null);

    [Fact]
    public async Task Dispatcher_GivenSingletonMarker_ResolvesSameInstanceInHostAndDispatchScope()
    {
        var port = new RecordingPort(() => Completion());
        var (dispatcher, provider, registry) = Build(port);
        await registry.PublishAsync(FactoryRolePack.Profiles[0]);

        var hostMarker = provider.GetRequiredService<Marker>();
        var result = await dispatcher.ExecuteAsync(Dispatch());

        Assert.True(result.Completed, result.FailureCategory);
        Assert.NotNull(port.ResolvedMarker);
        Assert.Same(hostMarker, port.ResolvedMarker);
    }

    [Fact]
    public async Task Dispatcher_GivenPinnedProfile_BuildsExecutionRequestFromProfileFields()
    {
        var port = new RecordingPort(() => Completion());
        var (dispatcher, _, registry) = Build(port);
        var profile = FactoryRolePack.Profiles[0];
        await registry.PublishAsync(profile);

        await dispatcher.ExecuteAsync(Dispatch());

        var request = Assert.IsType<AgentExecutionRequest>(port.Request);
        Assert.Equal(Session.Value, request.SessionId.Value);
        Assert.Equal("Align the requirement", request.Task);
        Assert.Equal(profile.Model.PreferredModel, request.ModelId);
        Assert.Equal(profile.ExecutionPolicy.MaxSteps, request.Options.MaxSteps);
        Assert.Equal(
            TimeSpan.FromSeconds(profile.ExecutionPolicy.TimeoutSeconds),
            request.Options.Timeout);
        Assert.Equal(profile.ExecutionPolicy.MaxTotalTokens, request.Options.MaxTotalTokens);
        Assert.Equal(
            profile.ExecutionPolicy.MaxConsecutiveToolFailures,
            request.Options.MaxConsecutiveToolFailures);
    }

    [Fact]
    public async Task Dispatcher_GivenAnalyticalRole_CompletesWithoutWorktree()
    {
        var store = new TestSessionStore();
        var created = DateTimeOffset.UtcNow;
        await store.SaveAsync(
            new SessionSnapshot(
                Session,
                new WorkflowId("workflow.linear"),
                SessionState.Editing,
                RepositoryPath: "repo",
                WorktreePath: string.Empty,
                UpdatedAtUtc: created),
            CancellationToken.None);

        var executor = new CapturingExecutor();
        var orchestrator = new WipRuntimeOrchestrator(store, new NoOpEventPublisher());
        var services = new ServiceCollection();
        services.AddSingleton<IAgentExecutor>(executor);
        services.AddSingleton<IChatModel, UnusedChatModel>();
        services.AddSingleton<Marker>();
        await using var provider = services.BuildServiceProvider();

        var builder = new WipBuilder(services);
        var registry = new LocalAgentProfileRegistry(_directory, Catalog, FactoryRolePack.Definitions);
        await registry.PublishAsync(FactoryRolePack.Profiles[0]);

        var dispatcher = new FactoryAgentDispatcher(
            registry,
            FactoryRolePack.Definitions,
            Catalog,
            new AgentProfilePermissionResolver(),
            new WipRuntimeAgentExecutorPort(orchestrator),
            provider.GetRequiredService<IServiceScopeFactory>(),
            builder);

        var result = await dispatcher.ExecuteAsync(Dispatch());

        Assert.True(result.Completed, result.FailureCategory);
        Assert.NotNull(executor.CapturedContext);
        Assert.Equal(string.Empty, executor.CapturedContext!.WorktreePath);
    }

    [Fact]
    public async Task Dispatcher_GivenMultiStepExecution_RecordsSeparatedInputAndOutputTokens()
    {
        var port = new RecordingPort(() => Completion("answer", input: 11, output: 7));
        var (dispatcher, _, registry) = Build(port);
        await registry.PublishAsync(FactoryRolePack.Profiles[0]);

        var result = await dispatcher.ExecuteAsync(Dispatch());

        Assert.True(result.Completed, result.FailureCategory);
        Assert.Equal(11, result.InputTokens);
        Assert.Equal(7, result.OutputTokens);

        // A completed step with no content is a failure, not an empty artifact recorded as success.
        var empty = new RecordingPort(() => new AgentExecutionResult(
            AgentExecutionStatus.Completed, null, [], new ModelProviderUsage(9, 4), null));
        var (emptyDispatcher, _, emptyRegistry) = Build(empty);
        await emptyRegistry.PublishAsync(FactoryRolePack.Profiles[0]);

        var emptyResult = await emptyDispatcher.ExecuteAsync(Dispatch());
        Assert.False(emptyResult.Completed);
        Assert.StartsWith(FactoryAgentDispatcher.EmptyModelContentCategory, emptyResult.FailureCategory, StringComparison.Ordinal);
        Assert.Equal(9, emptyResult.InputTokens);
        Assert.Equal(4, emptyResult.OutputTokens);
    }

    [Fact]
    public async Task Dispatcher_GivenBudgetExhaustion_ReturnsBoundedFailureCategory()
    {
        var port = new RecordingPort(() => new AgentExecutionResult(
            AgentExecutionStatus.MaxStepsReached,
            ChatMessage.Assistant("partial"),
            [],
            new ModelProviderUsage(5, 1),
            null));
        var (dispatcher, _, registry) = Build(port);
        await registry.PublishAsync(FactoryRolePack.Profiles[0]);

        var result = await dispatcher.ExecuteAsync(Dispatch());

        Assert.False(result.Completed);
        Assert.Equal(nameof(AgentExecutionStatus.MaxStepsReached), result.FailureCategory);
        Assert.Equal(5, result.InputTokens);
    }

    [Fact]
    public async Task Dispatcher_GivenUnavailableCapability_RefusesAndRecordsExplanation()
    {
        var emptyCatalog = new CapabilityCatalog([]);
        var port = new RecordingPort(() => Completion());

        var services = new ServiceCollection();
        services.AddSingleton<Marker>();
        services.AddSingleton<IChatModel, UnusedChatModel>();
        using var provider = services.BuildServiceProvider();
        var builder = new WipBuilder(services);

        // Publish against the full catalog, then dispatch against a host that has admitted nothing.
        var publishing = new LocalAgentProfileRegistry(_directory, Catalog, FactoryRolePack.Definitions);
        await publishing.PublishAsync(FactoryRolePack.Profiles[0]);

        var dispatcher = new FactoryAgentDispatcher(
            publishing,
            FactoryRolePack.Definitions,
            emptyCatalog,
            new AgentProfilePermissionResolver(),
            port,
            provider.GetRequiredService<IServiceScopeFactory>(),
            builder);

        var result = await dispatcher.ExecuteAsync(Dispatch());

        Assert.False(result.Completed);
        Assert.Contains(FactoryAgentDispatcher.CapabilityUnavailableCategory, result.FailureCategory, StringComparison.Ordinal);
        Assert.Contains("tool.repo-read@1.0.0", result.FailureCategory, StringComparison.Ordinal);
        Assert.Equal(0, port.Invocations);
    }
}
