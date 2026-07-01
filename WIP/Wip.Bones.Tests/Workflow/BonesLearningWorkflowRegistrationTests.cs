using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Abstractions.Workflows;
using Wip.Artifacts.Local;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Observe;
using Wip.Bones.Agents.Play;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.Agents.Workflow;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Builder;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Bones.Tests.Workflow;

public sealed class BonesLearningWorkflowRegistrationTests
{
    private const string BuilderExtensionChecklistItem = BonesRequirementsChecklistItems.BuilderExtension;

    private const string WorkflowRegistrationChecklistItem = BonesRequirementsChecklistItems.WorkflowRegistration;

    [Fact]
    [Trait("ChecklistItem", BuilderExtensionChecklistItem)]
    public void AddBonesLearningWorkflow_GivenBuilder_ExpectedRegistersObservePonderPlayEnhanceCapabilities()
    {
        ResetMapRuntimeForTests();

        var services = new ServiceCollection();
        var builder = services.AddWipCapabilities().AddBonesLearningWorkflow();

        var workflow = Assert.Single(
            builder.WorkflowRegistrations,
            registration => registration.WorkflowId.Value == "workflow.bones.learning");

        Assert.Equal(typeof(BonesLearningWorkflow), workflow.WorkflowType);
        Assert.Equal(typeof(BonesLearningWorkflowRequest), workflow.RequestType);
        Assert.Equal(typeof(BonesLearningWorkflowResult), workflow.ResultType);

        var capabilityIds = builder.CapabilityDescriptors
            .Select(descriptor => descriptor.CapabilityId.Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("tool.bones.observe-games", capabilityIds);
        Assert.Contains("agent.bones.ponder", capabilityIds);
        Assert.Contains("tool.bones.play-match", capabilityIds);
        Assert.Contains("agent.bones.enhance-strategy", capabilityIds);
        Assert.Equal(4, builder.CapabilityDescriptors.Count);
    }

    [Fact]
    [Trait("ChecklistItem", WorkflowRegistrationChecklistItem)]
    public void CompileLinear_GivenBonesLearningWorkflow_ExpectedMapAdaptersPreserveTypedStageContracts()
    {
        ResetMapRuntimeForTests();

        var builder = new ServiceCollection().AddWipCapabilities().AddBonesLearningWorkflow();
        var workflowRegistration = Assert.Single(
            builder.WorkflowRegistrations,
            registration => registration.WorkflowId.Value == "workflow.bones.learning");

        var compilation = WorkflowBuilderStageCompiler.CompileLinear(workflowRegistration);

        Assert.Equal(4, compilation.StageDescriptors.Count);
        Assert.Collection(
            compilation.StageDescriptors,
            stage =>
            {
                Assert.Equal(WorkflowStageKind.Run, stage.Stage);
                Assert.Equal(typeof(BonesObserveGamesRequest), stage.RequestType);
                Assert.Equal(typeof(BonesObserveGamesResult), stage.ResultType);
            },
            stage =>
            {
                Assert.Equal(WorkflowStageKind.Plan, stage.Stage);
                Assert.Equal(typeof(BonesPonderRequest), stage.RequestType);
                Assert.Equal(typeof(BonesPonderResult), stage.ResultType);
            },
            stage =>
            {
                Assert.Equal(WorkflowStageKind.Run, stage.Stage);
                Assert.Equal(typeof(BonesPlayMatchRequest), stage.RequestType);
                Assert.Equal(typeof(BonesPlayMatchResult), stage.ResultType);
            },
            stage =>
            {
                Assert.Equal(WorkflowStageKind.Plan, stage.Stage);
                Assert.Equal(typeof(BonesEnhanceStrategyRequest), stage.RequestType);
                Assert.Equal(typeof(BonesEnhanceStrategyResult), stage.ResultType);
            });

        Assert.Collection(
            compilation.MapAdapters,
            map =>
            {
                Assert.Equal(typeof(BonesObserveGamesResult), map.SourceType);
                Assert.Equal(typeof(BonesPonderRequest), map.TargetType);
            },
            map =>
            {
                Assert.Equal(typeof(BonesPonderResult), map.SourceType);
                Assert.Equal(typeof(BonesPlayMatchRequest), map.TargetType);
            },
            map =>
            {
                Assert.Equal(typeof(BonesPlayMatchResult), map.SourceType);
                Assert.Equal(typeof(BonesEnhanceStrategyRequest), map.TargetType);
            });

        Assert.All(compilation.MapAdapters, map =>
        {
            Assert.NotEqual(typeof(object), map.SourceType);
            Assert.NotEqual(typeof(object), map.TargetType);
        });
    }

    [Fact]
    [Trait("ChecklistItem", WorkflowRegistrationChecklistItem)]
    public async Task RunWorkflowAsync_GivenBonesLearningWorkflow_ExpectedDispatchesAllStagesInOrderWithArtifacts()
    {
        ResetMapRuntimeForTests();

        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-bones-learning-workflow-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            const int gameCount = 1;
            const int seed = 4242;
            const int targetScore = 10;
            var learningPlayer = new BonesPlayerId(1);
            var taskDescription = BonesLearningWorkflowParameters.FormatTaskDescription(
                gameCount,
                seed,
                targetScore,
                learningPlayer);

            var sessionStore = new InMemorySessionStore();
            var publisher = new CollectingSessionEventPublisher();
            var orchestrator = new WipRuntimeOrchestrator(sessionStore, publisher);
            var artifactStore = new WipArtifactStoreLocal(repositoryPath);

            var services = new ServiceCollection();
            var builder = CreateFullBonesLearningBuilder(
                services,
                artifactStore,
                new StubBonesStrategyModelProvider(),
                new StubBonesPlayTurnModelProvider(),
                new StubBonesEnhancementModelProvider());

            var snapshot = await orchestrator.StartSessionAsync(
                new SessionStartRequest(
                    WorkflowId: BonesLearningWorkflowIds.WorkflowId,
                    TaskDescription: taskDescription,
                    RepositoryPath: repositoryPath,
                    WorktreePath: Path.Combine(repositoryPath, ".wip", "worktrees", "bones-learning")),
                CancellationToken.None);

            var knowledgeStore = new BonesPlayerKnowledgeStore(artifactStore, repositoryPath, snapshot.SessionId);
            foreach (var seat in new[] { 2, 3, 4 })
            {
                var playerId = new BonesPlayerId(seat);
                await knowledgeStore.SaveStrategy(
                    playerId,
                    new BonesStrategyDocument(
                        new BonesStrategyId($"initial-seat-{seat}-v1"),
                        playerId,
                        markdown: $"""
                            # Seat {seat} Strategy

                            ## Rules
                            - Prefer first legal move from GetLegalMoves.
                            - Pass only when no playable tile matches open ends.
                            """),
                    CancellationToken.None);
            }

            var result = await orchestrator.RunWorkflowAsync(
                snapshot.SessionId,
                builder,
                selectedWorkflowId: BonesLearningWorkflowIds.WorkflowId,
                cancellationToken: CancellationToken.None,
                artifactStore: artifactStore);

            Assert.Equal("workflow.bones.learning", result.WorkflowId.Value);
            Assert.Collection(
                result.Stages,
                stage =>
                {
                    Assert.Equal(WorkflowStageKind.Run, stage.Descriptor.Stage);
                    Assert.Equal(typeof(BonesObserveGamesRequest), stage.Descriptor.RequestType);
                    Assert.Equal(BonesObserveGamesCapability.Id, stage.ProducerCapabilityId);
                    Assert.NotNull(stage.ExecutionArtifact);
                },
                stage =>
                {
                    Assert.Equal(WorkflowStageKind.Plan, stage.Descriptor.Stage);
                    Assert.Equal(typeof(BonesPonderRequest), stage.Descriptor.RequestType);
                    Assert.Equal(BonesPonderCapability.Id, stage.ProducerCapabilityId);
                    Assert.NotNull(stage.ExecutionArtifact);
                },
                stage =>
                {
                    Assert.Equal(WorkflowStageKind.Run, stage.Descriptor.Stage);
                    Assert.Equal(typeof(BonesPlayMatchRequest), stage.Descriptor.RequestType);
                    Assert.Equal(BonesPlayMatchCapability.Id, stage.ProducerCapabilityId);
                    Assert.NotNull(stage.ExecutionArtifact);
                },
                stage =>
                {
                    Assert.Equal(WorkflowStageKind.Plan, stage.Descriptor.Stage);
                    Assert.Equal(typeof(BonesEnhanceStrategyRequest), stage.Descriptor.RequestType);
                    Assert.Equal(BonesEnhanceStrategyCapability.Id, stage.ProducerCapabilityId);
                    Assert.NotNull(stage.ExecutionArtifact);
                });

            Assert.Contains(
                publisher.Events,
                sessionEvent => sessionEvent.Kind == SessionEventKind.WorkflowExecuted
                    && sessionEvent.SessionId == snapshot.SessionId);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", WorkflowRegistrationChecklistItem)]
    public async Task RunWorkflowAsync_GivenMissingBonesCapability_ExpectedDeterministicFailureBeforePartialStageArtifacts()
    {
        ResetMapRuntimeForTests();

        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-bones-learning-missing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var sessionStore = new InMemorySessionStore();
            var publisher = new CollectingSessionEventPublisher();
            var orchestrator = new WipRuntimeOrchestrator(sessionStore, publisher);
            var artifactStore = new WipArtifactStoreLocal(repositoryPath);

            var services = new ServiceCollection();
            var builder = CreateBonesLearningBuilderWithoutObserveTool(
                services,
                artifactStore,
                new StubBonesStrategyModelProvider(),
                new StubBonesPlayTurnModelProvider(),
                new StubBonesEnhancementModelProvider());

            var learningPlayer = new BonesPlayerId(1);
            var taskDescription = BonesLearningWorkflowParameters.FormatTaskDescription(
                gameCount: 1,
                seed: 9001,
                targetScore: 8,
                learningPlayer);

            var snapshot = await orchestrator.StartSessionAsync(
                new SessionStartRequest(
                    WorkflowId: BonesLearningWorkflowIds.WorkflowId,
                    TaskDescription: taskDescription,
                    RepositoryPath: repositoryPath,
                    WorktreePath: Path.Combine(repositoryPath, ".wip", "worktrees", "bones-learning-missing")),
                CancellationToken.None);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await orchestrator.RunWorkflowAsync(
                    snapshot.SessionId,
                    builder,
                    selectedWorkflowId: BonesLearningWorkflowIds.WorkflowId,
                    cancellationToken: CancellationToken.None,
                    artifactStore: artifactStore));

            Assert.Contains("requires a registered", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Tool", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(
                publisher.Events,
                sessionEvent => sessionEvent.Kind == SessionEventKind.WorkflowExecuted);

            var artifacts = await artifactStore.ListAsync(snapshot.SessionId, CancellationToken.None);
            Assert.Empty(artifacts);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    private static void ResetMapRuntimeForTests()
    {
        WorkflowStageMapAdapterRuntime.ResetRegisteredBindingsForTests();
        BonesLearningWorkflowMapRuntime.ResetRegistrationForTests();
    }

    private static WipBuilder CreateFullBonesLearningBuilder(
        IServiceCollection services,
        IArtifactStore artifactStore,
        IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult> strategyModelProvider,
        IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult> playTurnModelProvider,
        IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult> enhancementModelProvider)
    {
        RegisterBonesLearningServices(
            services,
            artifactStore,
            strategyModelProvider,
            playTurnModelProvider,
            enhancementModelProvider);

        return services.AddWipCapabilities().AddBonesLearningWorkflow();
    }

    private static WipBuilder CreateBonesLearningBuilderWithoutObserveTool(
        IServiceCollection services,
        IArtifactStore artifactStore,
        IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult> strategyModelProvider,
        IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult> playTurnModelProvider,
        IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult> enhancementModelProvider)
    {
        RegisterBonesLearningServices(
            services,
            artifactStore,
            strategyModelProvider,
            playTurnModelProvider,
            enhancementModelProvider);

        BonesLearningWorkflowMapRuntime.Register();

        var builder = services.AddWipCapabilities();
        builder.AddWorkflow<BonesLearningWorkflow, BonesLearningWorkflowRequest, BonesLearningWorkflowResult>(
            BonesLearningWorkflowIds.WorkflowId,
            "Bones learning workflow");
        builder.AddAgent<BonesPonderAgent, BonesPonderRequest, BonesPonderResult>(
            BonesPonderCapability.Id,
            "Bones ponder agent");
        builder.AddTool<BonesPlayMatchTool, BonesPlayMatchRequest, BonesPlayMatchResult>(
            BonesPlayMatchCapability.Id,
            "Bones play match tool");
        builder.AddAgent<BonesEnhanceStrategyAgent, BonesEnhanceStrategyRequest, BonesEnhanceStrategyResult>(
            BonesEnhanceStrategyCapability.Id,
            "Bones enhance strategy agent");

        return builder;
    }

    private static void RegisterBonesLearningServices(
        IServiceCollection services,
        IArtifactStore artifactStore,
        IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult> strategyModelProvider,
        IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult> playTurnModelProvider,
        IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult> enhancementModelProvider)
    {
        services.AddSingleton(artifactStore);
        services.AddSingleton<IArtifactStore>(artifactStore);
        services.AddSingleton(new BonesMatchSimulator());
        services.AddSingleton(new BonesGameEngine());
        services.AddSingleton(strategyModelProvider);
        services.AddSingleton(playTurnModelProvider);
        services.AddSingleton(enhancementModelProvider);
        services.AddSingleton<BonesObserveGamesTool>();
        services.AddSingleton<BonesPonderAgent>();
        services.AddSingleton<BonesPlayMatchTool>();
        services.AddSingleton<BonesEnhanceStrategyAgent>();
    }

    private sealed class InMemorySessionStore : ISessionStore
    {
        private readonly ConcurrentDictionary<SessionId, SessionSnapshot> _sessions = new();

        public ValueTask SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
        {
            _sessions[snapshot.SessionId] = snapshot;
            return ValueTask.CompletedTask;
        }

        public ValueTask<SessionSnapshot?> LoadAsync(SessionId sessionId, CancellationToken cancellationToken)
        {
            if (_sessions.TryGetValue(sessionId, out var snapshot))
                return ValueTask.FromResult<SessionSnapshot?>(snapshot);

            return ValueTask.FromResult<SessionSnapshot?>(null);
        }
    }

    private sealed class CollectingSessionEventPublisher : ISessionEventPublisher
    {
        public List<SessionEvent> Events { get; } = [];

        public ValueTask PublishAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
        {
            Events.Add(sessionEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubBonesStrategyModelProvider
        : IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>
    {
        public ValueTask<ModelProviderResponse<BonesStrategyAuthoringResult>> ExecuteAsync(
            ModelProviderRequest<BonesStrategyAuthoringRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            const string markdown = """
```csharp
using Wip.Bones.Domain;
using Wip.Bones.Engine;

public sealed class SeatStrategy : IBonesPlayerSlot
{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        return legalMoves[0];
    }
}
```
""";

            return ValueTask.FromResult(
                new ModelProviderResponse<BonesStrategyAuthoringResult>(
                    payload: new BonesStrategyAuthoringResult(markdown),
                    providerId: "bones-strategy-stub",
                    modelId: request.ModelId,
                    usage: new ModelProviderUsage(120, 48),
                    correlationId: request.CorrelationId));
        }
    }

    private sealed class StubBonesPlayTurnModelProvider
        : IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>
    {
        public ValueTask<ModelProviderResponse<BonesPlayTurnResult>> ExecuteAsync(
            ModelProviderRequest<BonesPlayTurnRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(
                new ModelProviderResponse<BonesPlayTurnResult>(
                    payload: new BonesPlayTurnResult(request.Payload.AllowedMoves[0].MoveId.Value),
                    providerId: "bones-play-turn-stub",
                    modelId: request.ModelId,
                    usage: new ModelProviderUsage(64, 16),
                    correlationId: request.CorrelationId));
    }

    private sealed class StubBonesEnhancementModelProvider
        : IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>
    {
        public ValueTask<ModelProviderResponse<BonesStrategyEnhancementResult>> ExecuteAsync(
            ModelProviderRequest<BonesStrategyEnhancementRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            const string script = """
using Wip.Bones.Domain;
using Wip.Bones.Engine;

public sealed class SeatStrategy : IBonesPlayerSlot
{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        return legalMoves[0];
    }
}
""";

            return ValueTask.FromResult(
                new ModelProviderResponse<BonesStrategyEnhancementResult>(
                    payload: new BonesStrategyEnhancementResult($"```csharp\n{script}\n```"),
                    providerId: "bones-enhance-stub",
                    modelId: request.ModelId,
                    usage: new ModelProviderUsage(140, 60),
                    correlationId: request.CorrelationId));
        }
    }
}
