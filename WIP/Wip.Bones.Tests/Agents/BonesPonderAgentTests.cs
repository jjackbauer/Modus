using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Abstractions.Workflows;
using Wip.Artifacts.Local;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Builder;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Bones.Tests.Agents;

public sealed class BonesPonderAgentTests
{
    private const string ChecklistItem = BonesRequirementsChecklistItems.PonderAgent;
    private const string PonderScriptChecklistItem = BonesRequirementsChecklistItems.PonderScriptPersistence;
    private const string PonderBootstrapSkipItem = BonesRequirementsChecklistItems.PonderBootstrapSkip;

    private const string ValidScriptSource = """
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

    private const string InvalidScriptSource = """
        using Wip.Bones.Domain;
        using Wip.Bones.Engine;

        public sealed class SeatStrategy : IBonesPlayerSlot
        {
            public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
            {
                return legalMoves[0];
            }
        """;

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesPonderAgent_GivenObservationArtifacts_ExpectedPromptIncludesBoardStateAndLegalMoveVocabulary()
    {
        await using var fixture = await PonderAgentFixture.CreateAsync();

        var playerId = new BonesPlayerId(1);
        await fixture.SaveObservationAsync(
            playerId,
            new BonesGameId("observe-board-state"),
            "# Observation\nSeat 1 watched open ends shift from 6-4 to 5-4 after a Left play.");

        var stubProvider = new DeterministicBonesStrategyModelProvider();
        var agent = new BonesPonderAgent(fixture.ArtifactStore, stubProvider);

        await agent.ExecuteAsync(
            fixture.CreateRequest(playerId),
            fixture.CreateCapabilityContext(),
            CancellationToken.None);

        var capturedRequest = Assert.Single(stubProvider.CapturedRequests);
        var promptText = string.Join(
            '\n',
            capturedRequest.Payload.Messages.Select(message => message.Content));

        Assert.Contains("open ends", promptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(BonesStrategyScriptApiReference.InterfaceName, promptText, StringComparison.Ordinal);
        Assert.Contains(BonesStrategyScriptApiReference.ContractHeader, promptText, StringComparison.Ordinal);
        Assert.Contains(BonesPonderPromptBuilder.ScriptOutputFormatHeader, promptText, StringComparison.Ordinal);
        Assert.Contains("open ends shift from 6-4 to 5-4", promptText, StringComparison.Ordinal);
        Assert.Contains("observe-board-state", promptText, StringComparison.Ordinal);
        Assert.All(
            capturedRequest.Payload.ObservationArtifactPaths,
            path => Assert.StartsWith($"bones-player-{playerId.Seat}-observation-", path, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    [Trait("ChecklistItem", PonderScriptChecklistItem)]
    public async Task BonesPonderAgent_GivenMockedScriptAuthoringResponse_ExpectedPersistsScriptArtifactWithActiveStatus()
    {
        await using var fixture = await PonderAgentFixture.CreateAsync();

        var playerId = new BonesPlayerId(2);
        await fixture.SaveObservationAsync(
            playerId,
            new BonesGameId("observe-rules"),
            "# Observation\nSeat 2 observed blocked round with four consecutive passes.");

        var stubProvider = new DeterministicBonesStrategyModelProvider();
        var agent = new BonesPonderAgent(fixture.ArtifactStore, stubProvider);

        var result = await agent.ExecuteAsync(
            fixture.CreateRequest(playerId),
            fixture.CreateCapabilityContext(),
            CancellationToken.None);

        Assert.Equal(new BonesStrategyId("initial-seat-2-v1"), result.StrategyId);
        Assert.Equal(BonesStrategyKind.Script, result.AuthoredStrategy.Kind);
        Assert.Equal(BonesPromotionStatus.Active, result.AuthoredStrategy.PromotionStatus);
        Assert.Contains("IBonesPlayerSlot", result.AuthoredStrategy.Source, StringComparison.Ordinal);
        Assert.Contains("bones-strategy-initial-seat-2-v1", result.StrategyArtifact.ArtifactId.Value, StringComparison.Ordinal);
        Assert.Contains($"bones-player-{playerId.Seat}-strategy-", result.StrategyArtifact.RelativePath, StringComparison.Ordinal);

        var loadedArtifact = await fixture.KnowledgeStore.LoadStrategyArtifact(
            playerId,
            result.StrategyId,
            CancellationToken.None);

        Assert.Equal(BonesStrategyKind.Script, loadedArtifact.Kind);
        Assert.Equal(BonesPromotionStatus.Active, loadedArtifact.PromotionStatus);
        Assert.Equal(result.AuthoredStrategy.Source, loadedArtifact.Source);
        Assert.Equal(result.AuthoredStrategy.Source, result.StrategyDocument.Markdown);
        Assert.Equal(1, result.ObservationsLoaded);
    }

    [Fact]
    [Trait("ChecklistItem", BonesLearningLoopReliabilityRequirementsChecklistItems.PonderCompileRetry)]
    [Trait("ChecklistItem", BonesLearningLoopReliabilityRequirementsChecklistItems.PonderExecutionArtifactFields)]
    public async Task BonesPonderAgent_GivenFirstResponseFailsCompileSecondSucceeds_ExpectedPersistsScriptArtifact()
    {
        await using var fixture = await PonderAgentFixture.CreateAsync();

        var playerId = new BonesPlayerId(3);
        var stubProvider = new SequentialBonesStrategyModelProvider(
            $"```csharp\n{InvalidScriptSource}\n```",
            $"```csharp\n{ValidScriptSource}\n```");
        var agent = new BonesPonderAgent(fixture.ArtifactStore, stubProvider);

        var result = await agent.ExecuteAsync(
            fixture.CreateRequest(playerId),
            fixture.CreateCapabilityContext(),
            CancellationToken.None);

        Assert.Equal(2, stubProvider.CapturedRequests.Count);
        Assert.Equal(BonesStrategyKind.Script, result.AuthoredStrategy.Kind);

        var executionContent = await fixture.ReadArtifactContentAsync(result.ExecutionArtifact);
        using var executionJson = JsonDocument.Parse(executionContent);
        Assert.Equal(2, executionJson.RootElement.GetProperty("CompileAttempts").GetInt32());
        Assert.True(executionJson.RootElement.GetProperty("FinalCompileSucceeded").GetBoolean());

        var retryPrompt = stubProvider.CapturedRequests[1].Payload.Messages.Last().Content;
        Assert.Contains("Compiler diagnostics", retryPrompt, StringComparison.Ordinal);
        Assert.Contains(BonesStrategyScriptApiReference.ContractHeader, retryPrompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", BonesLearningLoopReliabilityRequirementsChecklistItems.PonderCompileRetry)]
    public async Task BonesPonderAgent_GivenAllCompileAttemptsFail_ExpectedThrowsInvalidOperationException()
    {
        await using var fixture = await PonderAgentFixture.CreateAsync();

        var playerId = new BonesPlayerId(4);
        var stubProvider = new SequentialBonesStrategyModelProvider(
            $"```csharp\n{InvalidScriptSource}\n```",
            $"```csharp\n{InvalidScriptSource}\n```");
        var agent = new BonesPonderAgent(
            fixture.ArtifactStore,
            stubProvider,
            compileRetryOptions: new BonesPonderCompileRetryOptions { MaxCompileRetries = 2 });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agent.ExecuteAsync(
                fixture.CreateRequest(playerId),
                fixture.CreateCapabilityContext(),
                CancellationToken.None)
            .AsTask());

        Assert.Equal(2, stubProvider.CapturedRequests.Count);

        var artifacts = await fixture.ArtifactStore.ListAsync(fixture.SessionId, CancellationToken.None);
        Assert.DoesNotContain(
            artifacts,
            artifact => artifact.ArtifactId.Value.StartsWith("bones-strategy-initial-seat-4-v1", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("ChecklistItem", PonderScriptChecklistItem)]
    public async Task BonesPonderAgent_GivenNonCompilableScriptResponse_ExpectedFailsWithoutStrategyArtifactWrite()
    {
        await using var fixture = await PonderAgentFixture.CreateAsync();

        var playerId = new BonesPlayerId(3);
        var stubProvider = new DeterministicBonesStrategyModelProvider(
            responseContent: $"```csharp\n{InvalidScriptSource}\n```");
        var agent = new BonesPonderAgent(fixture.ArtifactStore, stubProvider);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agent.ExecuteAsync(
                fixture.CreateRequest(playerId),
                fixture.CreateCapabilityContext(),
                CancellationToken.None)
            .AsTask());

        var artifacts = await fixture.ArtifactStore.ListAsync(fixture.SessionId, CancellationToken.None);
        Assert.DoesNotContain(
            artifacts,
            artifact => artifact.ArtifactId.Value.StartsWith("bones-strategy-initial-seat-3-v1", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("ChecklistItem", PonderBootstrapSkipItem)]
    public async Task BonesStrategyBootstrapper_GivenLibraryEntryForLearningPlayer_ExpectedSkipsPonderForThatSeat()
    {
        await using var fixture = await PonderAgentFixture.CreateAsync();

        var playerId = new BonesPlayerId(1);
        var strategyId = new BonesStrategyId("library-seat-1-v4");
        var libraryStrategy = new BonesStrategyArtifact(
            strategyId,
            playerId,
            BonesStrategyKind.Script,
            ValidScriptSource,
            BonesPromotionStatus.Active);

        await fixture.KnowledgeStore.SaveStrategyArtifact(playerId, libraryStrategy, CancellationToken.None);
        await BonesStrategyBootstrapState.SaveAsync(
            fixture.ArtifactStore,
            fixture.SessionId,
            new BonesStrategyBootstrapResult([playerId], []),
            CancellationToken.None);

        var stubProvider = new DeterministicBonesStrategyModelProvider();
        var agent = new BonesPonderAgent(fixture.ArtifactStore, stubProvider);

        var result = await agent.ExecuteAsync(
            fixture.CreateRequest(playerId),
            fixture.CreateCapabilityContext(),
            CancellationToken.None);

        Assert.Empty(stubProvider.CapturedRequests);
        Assert.Equal(strategyId, result.StrategyId);
        Assert.Equal(0, result.ObservationsLoaded);

        var executionContent = await fixture.ReadArtifactContentAsync(result.ExecutionArtifact);
        using var executionJson = JsonDocument.Parse(executionContent);
        Assert.Equal(BonesBootstrapSource.Library, executionJson.RootElement.GetProperty("BootstrapSource").GetString());
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesPonderAgent_GivenOnlyOtherPlayerObservations_ExpectedCannotLoadForeignArtifacts()
    {
        await using var fixture = await PonderAgentFixture.CreateAsync();

        var ponderingPlayer = new BonesPlayerId(1);
        var otherPlayer = new BonesPlayerId(3);

        await fixture.SaveObservationAsync(
            otherPlayer,
            new BonesGameId("foreign-observation"),
            "# Observation\nSeat 3 private board read: opponent hand sizes hidden except public plays.");

        var stubProvider = new DeterministicBonesStrategyModelProvider();
        var agent = new BonesPonderAgent(fixture.ArtifactStore, stubProvider);

        var result = await agent.ExecuteAsync(
            fixture.CreateRequest(ponderingPlayer),
            fixture.CreateCapabilityContext(),
            CancellationToken.None);

        var capturedRequest = Assert.Single(stubProvider.CapturedRequests);
        var promptText = string.Join(
            '\n',
            capturedRequest.Payload.Messages.Select(message => message.Content));

        Assert.Equal(0, result.ObservationsLoaded);
        Assert.Empty(capturedRequest.Payload.ObservationArtifactPaths);
        Assert.Contains("No observation transcripts were loaded for this player.", promptText, StringComparison.Ordinal);
        Assert.DoesNotContain("foreign-observation", promptText, StringComparison.Ordinal);
        Assert.DoesNotContain("Seat 3 private board read", promptText, StringComparison.Ordinal);
        Assert.DoesNotContain($"bones-player-{otherPlayer.Seat}-observation-", promptText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesPonderAgent_GivenOrchestratorDispatch_ExpectedAgentCapabilityInvokedWithTypedContracts()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-bones-ponder-dispatch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var sessionId = new SessionId($"bones-ponder-dispatch-{Guid.NewGuid():N}");
            var artifactStore = new WipArtifactStoreLocal(repositoryPath);
            var services = new ServiceCollection();
            var builder = services.AddWipCapabilities();

            services.AddSingleton<IArtifactStore>(artifactStore);
            services.AddSingleton<IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>, DeterministicBonesStrategyModelProvider>();
            services.AddSingleton<BonesPonderAgent>();

            builder.AddAgent<BonesPonderAgent, BonesPonderRequest, BonesPonderResult>(
                BonesPonderCapability.Id,
                "Bones ponder agent");

            using var provider = services.BuildServiceProvider();
            var dispatcher = new WorkflowStageCapabilityDispatcher(provider, builder.CapabilityDescriptors);
            var descriptor = WorkflowStageDescriptor.Create<BonesPonderRequest, BonesPonderResult>(
                WorkflowStageKind.Plan);
            var request = new BonesPonderRequest(
                PlayerId: new BonesPlayerId(4),
                RepositoryPath: repositoryPath);

            var stageResult = await dispatcher.ExecuteStageAsync(
                descriptor,
                request,
                new WorkflowStageDispatchContext(
                    CreateSessionSnapshot(sessionId, repositoryPath),
                    builder,
                    "corr-bones-ponder",
                    ArtifactStore: artifactStore),
                new WorkflowStageMappedInputEvidence(
                    descriptor.RequestContractName,
                    request,
                    descriptor.RequestContractName,
                    request),
                CancellationToken.None);

            Assert.Equal(BonesPonderCapability.Id.Value, stageResult.ProducerCapabilityId.Value);
            Assert.Equal(typeof(BonesPonderRequest).FullName, stageResult.RequestContractName);
            Assert.Equal(typeof(BonesPonderResult).FullName, stageResult.ResultContractName);

            var typedResult = Assert.IsType<BonesPonderResult>(stageResult.Result);
            Assert.Equal(new BonesStrategyId("initial-seat-4-v1"), typedResult.StrategyId);
            Assert.Equal(BonesStrategyKind.Script, typedResult.AuthoredStrategy.Kind);
            Assert.Equal("Wip.Bones.Agents.BonesPonderAgent", typedResult.ExecutionArtifact.ProducerType);

            var artifacts = await artifactStore.ListAsync(sessionId, CancellationToken.None);
            Assert.Contains(
                artifacts,
                artifact => artifact.ArtifactId == typedResult.StrategyArtifact.ArtifactId);
            Assert.Contains(
                artifacts,
                artifact => artifact.ArtifactId == typedResult.ExecutionArtifact.ArtifactId);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    private static SessionSnapshot CreateSessionSnapshot(SessionId sessionId, string repositoryPath)
        => new(
            SessionId: sessionId,
            WorkflowId: new WorkflowId("workflow.bones.learning"),
            State: SessionState.Created,
            RepositoryPath: repositoryPath,
            WorktreePath: Path.Combine(repositoryPath, ".wip", "worktrees", "bones-ponder"),
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            TaskDescription: "ponder agent dispatch probe");

    private sealed class SequentialBonesStrategyModelProvider
        : IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>
    {
        private readonly Queue<string> _responses;

        public SequentialBonesStrategyModelProvider(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        public List<ModelProviderRequest<BonesStrategyAuthoringRequest>> CapturedRequests { get; } = [];

        public ValueTask<ModelProviderResponse<BonesStrategyAuthoringResult>> ExecuteAsync(
            ModelProviderRequest<BonesStrategyAuthoringRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            CapturedRequests.Add(request);

            var responseContent = _responses.Count > 0
                ? _responses.Dequeue()
                : throw new InvalidOperationException("No more stub responses were configured.");

            return ValueTask.FromResult(
                new ModelProviderResponse<BonesStrategyAuthoringResult>(
                    payload: new BonesStrategyAuthoringResult(responseContent),
                    providerId: "bones-strategy-stub",
                    modelId: request.ModelId,
                    usage: new ModelProviderUsage(120, 48),
                    correlationId: request.CorrelationId));
        }
    }

    private sealed class DeterministicBonesStrategyModelProvider
        : IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>
    {
        private readonly string _responseContent;

        public DeterministicBonesStrategyModelProvider(string? responseContent = null)
        {
            _responseContent = responseContent ?? $"""
                ```csharp
                {ValidScriptSource}
                ```
                """;
        }

        public List<ModelProviderRequest<BonesStrategyAuthoringRequest>> CapturedRequests { get; } = [];

        public ValueTask<ModelProviderResponse<BonesStrategyAuthoringResult>> ExecuteAsync(
            ModelProviderRequest<BonesStrategyAuthoringRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            CapturedRequests.Add(request);

            return ValueTask.FromResult(
                new ModelProviderResponse<BonesStrategyAuthoringResult>(
                    payload: new BonesStrategyAuthoringResult(_responseContent),
                    providerId: "bones-strategy-stub",
                    modelId: request.ModelId,
                    usage: new ModelProviderUsage(120, 48),
                    correlationId: request.CorrelationId));
        }
    }

    private sealed class PonderAgentFixture : IAsyncDisposable
    {
        private readonly string _repositoryPath;

        private PonderAgentFixture(
            string repositoryPath,
            SessionId sessionId,
            WipArtifactStoreLocal artifactStore,
            BonesPlayerKnowledgeStore knowledgeStore)
        {
            _repositoryPath = repositoryPath;
            SessionId = sessionId;
            ArtifactStore = artifactStore;
            KnowledgeStore = knowledgeStore;
        }

        public SessionId SessionId { get; }

        public WipArtifactStoreLocal ArtifactStore { get; }

        public BonesPlayerKnowledgeStore KnowledgeStore { get; }

        public static async Task<PonderAgentFixture> CreateAsync()
        {
            var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-bones-ponder-{Guid.NewGuid():N}");
            Directory.CreateDirectory(repositoryPath);

            var sessionId = new SessionId($"bones-ponder-{Guid.NewGuid():N}");
            var artifactStore = new WipArtifactStoreLocal(repositoryPath);
            var knowledgeStore = new BonesPlayerKnowledgeStore(artifactStore, repositoryPath, sessionId);

            await Task.CompletedTask;
            return new PonderAgentFixture(repositoryPath, sessionId, artifactStore, knowledgeStore);
        }

        public async Task SaveObservationAsync(BonesPlayerId playerId, BonesGameId gameId, string markdown)
        {
            await KnowledgeStore.SaveObservation(
                playerId,
                new BonesObservationTranscript(gameId, playerId, markdown),
                CancellationToken.None);
        }

        public BonesPonderRequest CreateRequest(BonesPlayerId playerId)
            => new(playerId, _repositoryPath);

        public CapabilityContext CreateCapabilityContext()
            => new(SessionId, Path.Combine(_repositoryPath, ".wip", "worktrees", "bones-ponder"));

        public async Task<string> ReadArtifactContentAsync(ArtifactDescriptor descriptor)
        {
            var artifactPath = Path.Combine(
                _repositoryPath,
                descriptor.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            return await File.ReadAllTextAsync(artifactPath);
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_repositoryPath))
                Directory.Delete(_repositoryPath, recursive: true);

            return ValueTask.CompletedTask;
        }
    }
}
