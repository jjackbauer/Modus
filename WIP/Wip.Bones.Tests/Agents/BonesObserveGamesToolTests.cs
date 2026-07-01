using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Abstractions.Workflows;
using Wip.Artifacts.Local;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Observe;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Builder;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Bones.Tests.Agents;

public sealed class BonesObserveGamesToolTests
{
    private const string ChecklistItem = BonesRequirementsChecklistItems.ObserveGamesTool;

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesObserveGamesTool_GivenObserveRequest_ExpectedRunsConfiguredGameCountThroughEngine()
    {
        await using var fixture = await ObserveGamesToolFixture.CreateAsync();

        const int gameCount = 2;
        var result = await fixture.Tool.ExecuteAsync(
            fixture.CreateRequest(gameCount: gameCount, seed: 4242),
            fixture.CreateCapabilityContext(),
            CancellationToken.None);

        Assert.Equal(gameCount, result.GamesObserved);
        Assert.Equal(gameCount * BonesTableConfig.FixedPlayerCount, result.TranscriptsPersisted);

        foreach (var seat in Enumerable.Range(BonesPlayerId.MinSeat, BonesTableConfig.FixedPlayerCount))
        {
            var playerId = new BonesPlayerId(seat);
            Assert.Equal(gameCount, result.ObservationsPerPlayer[playerId]);
        }
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesObserveGamesTool_GivenFourObserverAgents_ExpectedEachReceivesOwnTranscriptArtifact()
    {
        await using var fixture = await ObserveGamesToolFixture.CreateAsync();

        var result = await fixture.Tool.ExecuteAsync(
            fixture.CreateRequest(gameCount: 1, seed: 9001),
            fixture.CreateCapabilityContext(),
            CancellationToken.None);

        var observerArtifacts = new Dictionary<BonesPlayerId, ArtifactDescriptor>();
        var observerContents = new Dictionary<BonesPlayerId, string>();

        foreach (var seat in Enumerable.Range(BonesPlayerId.MinSeat, BonesTableConfig.FixedPlayerCount))
        {
            var playerId = new BonesPlayerId(seat);
            var history = await fixture.KnowledgeStore.ListHistory(playerId, CancellationToken.None);
            var observation = Assert.Single(history, descriptor =>
                descriptor.ArtifactId.Value.StartsWith($"bones-observation-{seat}-", StringComparison.Ordinal));

            observerArtifacts[playerId] = observation;
            observerContents[playerId] = await File.ReadAllTextAsync(
                fixture.GetArtifactPath(observation),
                CancellationToken.None);
        }

        Assert.Equal(BonesTableConfig.FixedPlayerCount, observerArtifacts.Values.Select(descriptor => descriptor.RelativePath).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(BonesTableConfig.FixedPlayerCount, observerContents.Values.Distinct(StringComparer.Ordinal).Count());

        foreach (var seat in Enumerable.Range(BonesPlayerId.MinSeat, BonesTableConfig.FixedPlayerCount))
        {
            var playerId = new BonesPlayerId(seat);
            Assert.Contains($"Observer seat: {seat}", observerContents[playerId], StringComparison.Ordinal);
            Assert.Contains("redacted", observerContents[playerId], StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(BonesTableConfig.FixedPlayerCount, result.TranscriptsPersisted);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesObserveGamesTool_GivenOrchestratorDispatch_ExpectedToolCapabilityResolvedAndExecutionArtifactPersisted()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-bones-observe-dispatch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var sessionId = new SessionId($"bones-observe-dispatch-{Guid.NewGuid():N}");
            var artifactStore = new WipArtifactStoreLocal(repositoryPath);
            var services = new ServiceCollection();
            var builder = services.AddWipCapabilities();

            services.AddSingleton<IArtifactStore>(artifactStore);
            services.AddSingleton<BonesMatchSimulator>();
            services.AddSingleton<BonesObserveGamesTool>();

            builder.AddTool<BonesObserveGamesTool, BonesObserveGamesRequest, BonesObserveGamesResult>(
                BonesObserveGamesCapability.Id,
                "Bones observe games tool");

            using var provider = services.BuildServiceProvider();
            var dispatcher = new WorkflowStageCapabilityDispatcher(provider, builder.CapabilityDescriptors);
            var descriptor = WorkflowStageDescriptor.Create<BonesObserveGamesRequest, BonesObserveGamesResult>(
                WorkflowStageKind.Run);
            var request = new BonesObserveGamesRequest(
                GameCount: 1,
                Seed: 1337,
                TargetScore: 15,
                ObserverPlayerIds: [new BonesPlayerId(1), new BonesPlayerId(2)],
                RepositoryPath: repositoryPath);

            var stageResult = await dispatcher.ExecuteStageAsync(
                descriptor,
                request,
                new WorkflowStageDispatchContext(
                    CreateSessionSnapshot(sessionId, repositoryPath),
                    builder,
                    "corr-bones-observe",
                    ArtifactStore: artifactStore),
                new WorkflowStageMappedInputEvidence(
                    descriptor.RequestContractName,
                    request,
                    descriptor.RequestContractName,
                    request),
                CancellationToken.None);

            Assert.Equal(BonesObserveGamesCapability.Id.Value, stageResult.ProducerCapabilityId.Value);
            Assert.Equal(typeof(BonesObserveGamesRequest).FullName, stageResult.RequestContractName);
            Assert.Equal(typeof(BonesObserveGamesResult).FullName, stageResult.ResultContractName);

            var typedResult = Assert.IsType<BonesObserveGamesResult>(stageResult.Result);
            Assert.Equal(1, typedResult.GamesObserved);
            Assert.Equal(2, typedResult.TranscriptsPersisted);
            Assert.Equal("Wip.Bones.Agents.BonesObserveGamesTool", typedResult.ExecutionArtifact.ProducerType);

            var artifacts = await artifactStore.ListAsync(sessionId, CancellationToken.None);
            Assert.Contains(
                artifacts,
                artifact => artifact.ArtifactId == typedResult.ExecutionArtifact.ArtifactId);
            Assert.Contains(
                artifacts,
                artifact => artifact.ProducerType == "Wip.Bones.Agents.BonesPlayerKnowledgeStore");
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
            WorktreePath: Path.Combine(repositoryPath, ".wip", "worktrees", "bones-observe"),
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            TaskDescription: "observe games dispatch probe");

    private sealed class ObserveGamesToolFixture : IAsyncDisposable
    {
        private readonly string _repositoryPath;

        private ObserveGamesToolFixture(
            string repositoryPath,
            SessionId sessionId,
            BonesObserveGamesTool tool,
            BonesPlayerKnowledgeStore knowledgeStore)
        {
            _repositoryPath = repositoryPath;
            SessionId = sessionId;
            Tool = tool;
            KnowledgeStore = knowledgeStore;
        }

        public SessionId SessionId { get; }

        public BonesObserveGamesTool Tool { get; }

        public BonesPlayerKnowledgeStore KnowledgeStore { get; }

        public static async Task<ObserveGamesToolFixture> CreateAsync()
        {
            var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-bones-observe-{Guid.NewGuid():N}");
            Directory.CreateDirectory(repositoryPath);

            var sessionId = new SessionId($"bones-observe-{Guid.NewGuid():N}");
            var artifactStore = new WipArtifactStoreLocal(repositoryPath);
            var knowledgeStore = new BonesPlayerKnowledgeStore(artifactStore, repositoryPath, sessionId);
            var tool = new BonesObserveGamesTool(artifactStore, new BonesMatchSimulator());

            await Task.CompletedTask;
            return new ObserveGamesToolFixture(repositoryPath, sessionId, tool, knowledgeStore);
        }

        public BonesObserveGamesRequest CreateRequest(int gameCount, int seed)
        {
            var observers = Enumerable
                .Range(BonesPlayerId.MinSeat, BonesTableConfig.FixedPlayerCount)
                .Select(seat => new BonesPlayerId(seat))
                .ToArray();

            return new BonesObserveGamesRequest(
                gameCount,
                seed,
                TargetScore: 15,
                observers,
                _repositoryPath);
        }

        public CapabilityContext CreateCapabilityContext()
            => new(SessionId, Path.Combine(_repositoryPath, ".wip", "worktrees", "bones-observe"));

        public string GetArtifactPath(ArtifactDescriptor descriptor)
            => Path.Combine(_repositoryPath, descriptor.RelativePath.Replace('/', Path.DirectorySeparatorChar));

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_repositoryPath))
                Directory.Delete(_repositoryPath, recursive: true);

            return ValueTask.CompletedTask;
        }
    }
}
