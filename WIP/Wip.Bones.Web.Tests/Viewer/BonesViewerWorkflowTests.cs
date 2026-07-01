using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Artifacts.Local;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Observe;
using Wip.Bones.Agents.Play;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.Agents.Viewer;
using Wip.Bones.Agents.Workflow;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Bones.Web;
using Wip.Bones.Web.Viewer;
using Wip.Builder;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Bones.Web.Tests;

public sealed class BonesViewerWorkflowTests
{
    private const string ChecklistItem = BonesRequirementsChecklistItems.ViewerWorkflowWiring;

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesViewerWorkflow_GivenLearningRun_ExpectedShellStdoutContainsViewerUrl()
    {
        BonesLearningWorkflowMapRuntime.ResetRegistrationForTests();

        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-bones-viewer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var catalog = new InMemoryBonesMatchCatalog();
            var stdout = new CollectingBonesViewerStdout();
            var artifactStore = new WipArtifactStoreLocal(repositoryPath);
            var sessionStore = new InMemorySessionStore();
            var publisher = new CollectingSessionEventPublisher();

            await using var factory = new BonesViewerWorkflowWebApplicationFactory(catalog, stdout);
            var viewerBaseUrl = factory.Server.BaseAddress
                ?? throw new InvalidOperationException("Test server base address was not configured.");

            var services = new ServiceCollection();
            RegisterWorkflowServices(
                services,
                artifactStore,
                catalog,
                stdout,
                viewerBaseUrl);

            var builder = services.AddWipCapabilities().AddBonesLearningWorkflow();
            var orchestrator = new WipRuntimeOrchestrator(sessionStore, publisher);

            const int gameCount = 1;
            const int seed = 4242;
            const int targetScore = 10;
            var learningPlayer = new BonesPlayerId(1);
            var taskDescription = BonesLearningWorkflowParameters.FormatTaskDescription(
                gameCount,
                seed,
                targetScore,
                learningPlayer);

            var snapshot = await orchestrator.StartSessionAsync(
                new SessionStartRequest(
                    WorkflowId: BonesLearningWorkflowIds.WorkflowId,
                    TaskDescription: taskDescription,
                    RepositoryPath: repositoryPath,
                    WorktreePath: Path.Combine(repositoryPath, ".wip", "worktrees", "bones-viewer")),
                CancellationToken.None);

            foreach (var seat in new[] { 2, 3, 4 })
            {
                var playerId = new BonesPlayerId(seat);
                await new BonesPlayerKnowledgeStore(artifactStore, repositoryPath, snapshot.SessionId)
                    .SaveStrategy(
                        playerId,
                        new BonesStrategyDocument(
                            new BonesStrategyId($"initial-seat-{seat}-v1"),
                            playerId,
                            markdown: """
                                # Bones Strategy

                                ## Rules
                                - Prefer first legal move.
                                """),
                        CancellationToken.None);
            }

            await orchestrator.RunWorkflowAsync(
                snapshot.SessionId,
                builder,
                BonesLearningWorkflowIds.WorkflowId,
                CancellationToken.None,
                artifactStore);

            var matchId = BonesViewerUrlBuilder.CreateLearningMatchId(snapshot.SessionId);
            var expectedViewerUrl = BonesViewerUrlBuilder.BuildMatchViewUrl(
                viewerBaseUrl,
                snapshot.SessionId,
                matchId);

            Assert.Contains(
                stdout.Lines,
                line => line.Contains(BonesViewerMarkers.ViewerUrlStdoutPrefix, StringComparison.Ordinal)
                    && line.Contains(expectedViewerUrl.ToString(), StringComparison.Ordinal));

            var viewerUrlArtifact = await artifactStore.ListAsync(snapshot.SessionId, CancellationToken.None);
            Assert.Contains(
                viewerUrlArtifact,
                descriptor => descriptor.RelativePath.Contains(BonesViewerMarkers.ViewerUrlArtifactFileName, StringComparison.Ordinal));

            using var client = factory.CreateClient();
            var snapshotResponse = await client.GetAsync(
                $"/bones/sessions/{snapshot.SessionId.Value}/matches/{matchId.Value}");

            Assert.Equal(HttpStatusCode.OK, snapshotResponse.StatusCode);

            var matchSnapshot = await snapshotResponse.Content.ReadFromJsonAsync<BonesMatchViewModel>(
                BonesWebHost.JsonOptions);
            Assert.NotNull(matchSnapshot);
            Assert.True(matchSnapshot.FrameCount > 0, "Live play/observe stages should publish replay frames.");
            Assert.Equal(snapshot.SessionId.Value, matchSnapshot.SessionId);
            Assert.Equal(matchId.Value, matchSnapshot.MatchId);

            var snapshotBody = await snapshotResponse.Content.ReadAsStringAsync();
            Assert.DoesNotContain("bones-strategy", snapshotBody, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Prefer first legal move", snapshotBody, StringComparison.Ordinal);
            Assert.DoesNotContain("private hand contents", snapshotBody, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);

            BonesLearningWorkflowMapRuntime.ResetRegistrationForTests();
        }
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesObserveGamesTool_GivenViewerPublish_ExpectedMatchSnapshotReplaySucceeds()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-bones-observe-viewer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            const int seed = 4242;
            var catalog = new InMemoryBonesMatchCatalog();
            var feed = new BonesCatalogPublicMatchFeed(catalog);
            var coordinator = new BonesLearningViewerCoordinator(
                feed,
                new NoOpBonesViewerRunPublisher(),
                new Uri("http://127.0.0.1:5050"));
            var artifactStore = new WipArtifactStoreLocal(repositoryPath);
            var sessionId = new SessionId($"bones-observe-viewer-{Guid.NewGuid():N}");
            var tool = new BonesObserveGamesTool(
                artifactStore,
                new BonesMatchSimulator(),
                coordinator);

            await tool.ExecuteAsync(
                new BonesObserveGamesRequest(
                    GameCount: 1,
                    Seed: seed,
                    TargetScore: 15,
                    ObserverPlayerIds: [new BonesPlayerId(1), new BonesPlayerId(2), new BonesPlayerId(3), new BonesPlayerId(4)],
                    RepositoryPath: repositoryPath),
                new CapabilityContext(sessionId, Path.Combine(repositoryPath, ".wip", "worktrees", "observe-viewer")),
                CancellationToken.None);

            var matchId = BonesViewerUrlBuilder.CreateLearningMatchId(sessionId);
            Assert.True(catalog.TryGet(sessionId, matchId, out var registered));

            var viewer = new BonesMatchViewerService();
            var snapshot = viewer.BuildMatchSnapshot(sessionId, registered);
            var frames = viewer.BuildMatchTimeline(registered);

            Assert.True(snapshot.FrameCount > 0);
            Assert.Equal(registered.MatchResult.Transcript.Length, frames.Count);
            Assert.Equal(HashCode.Combine(seed, 1), registered.MatchSeed);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    private sealed class NoOpBonesViewerRunPublisher : IBonesViewerRunPublisher
    {
        public ValueTask PublishViewerUrlOnceAsync(
            SessionId sessionId,
            BonesGameId matchId,
            Uri viewerUrl,
            CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }

    private static void RegisterWorkflowServices(
        IServiceCollection services,
        IArtifactStore artifactStore,
        InMemoryBonesMatchCatalog catalog,
        CollectingBonesViewerStdout stdout,
        Uri viewerBaseUrl)
    {
        services.AddSingleton(artifactStore);
        services.AddSingleton<IArtifactStore>(artifactStore);
        services.AddSingleton<IBonesMatchCatalog>(catalog);
        services.AddSingleton<IBonesPublicMatchFeed, BonesCatalogPublicMatchFeed>();
        services.AddSingleton<IBonesViewerStdout>(stdout);
        services.AddSingleton<BonesViewerRunPublisher>();
        services.AddSingleton<IBonesViewerRunPublisher>(sp => sp.GetRequiredService<BonesViewerRunPublisher>());
        services.AddSingleton(viewerBaseUrl);
        services.AddSingleton<BonesLearningViewerCoordinator>();
        services.AddSingleton(new BonesMatchSimulator());
        services.AddSingleton(new BonesGameEngine());
        services.AddSingleton<IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>, StubBonesStrategyModelProvider>();
        services.AddSingleton<IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>, StubBonesPlayTurnModelProvider>();
        services.AddSingleton<IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>, StubBonesEnhancementModelProvider>();
        services.AddSingleton<BonesObserveGamesTool>();
        services.AddSingleton<BonesPonderAgent>();
        services.AddSingleton<BonesPlayMatchTool>();
        services.AddSingleton<BonesEnhanceStrategyAgent>();
    }

    private sealed class BonesViewerWorkflowWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly InMemoryBonesMatchCatalog _catalog;

        public BonesViewerWorkflowWebApplicationFactory(
            InMemoryBonesMatchCatalog catalog,
            CollectingBonesViewerStdout stdout)
        {
            _catalog = catalog;
            _ = stdout;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                var catalogDescriptor = services.SingleOrDefault(
                    descriptor => descriptor.ServiceType == typeof(IBonesMatchCatalog));
                if (catalogDescriptor is not null)
                    services.Remove(catalogDescriptor);

                services.AddSingleton<IBonesMatchCatalog>(_catalog);
                services.AddSingleton<IBonesPublicMatchFeed, BonesCatalogPublicMatchFeed>();
            });
        }
    }

    private sealed class InMemorySessionStore : ISessionStore
    {
        private readonly Dictionary<SessionId, SessionSnapshot> _sessions = new();

        public ValueTask SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
        {
            _sessions[snapshot.SessionId] = snapshot;
            return ValueTask.CompletedTask;
        }

        public ValueTask<SessionSnapshot?> LoadAsync(SessionId sessionId, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(_sessions.TryGetValue(sessionId, out var snapshot) ? snapshot : null);
        }
    }

    private sealed class CollectingSessionEventPublisher : ISessionEventPublisher
    {
        public ValueTask PublishAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }

    private sealed class StubBonesStrategyModelProvider
        : IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>
    {
        public ValueTask<ModelProviderResponse<BonesStrategyAuthoringResult>> ExecuteAsync(
            ModelProviderRequest<BonesStrategyAuthoringRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(
                new ModelProviderResponse<BonesStrategyAuthoringResult>(
                    payload: new BonesStrategyAuthoringResult("""
                        # Bones Strategy

                        ## Rules
                        - Prefer matching open ends before passing.
                        """),
                    providerId: "bones-strategy-stub",
                    modelId: request.ModelId,
                    usage: new ModelProviderUsage(64, 32),
                    correlationId: request.CorrelationId));
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
                    usage: new ModelProviderUsage(32, 8),
                    correlationId: request.CorrelationId));
    }

    private sealed class StubBonesEnhancementModelProvider
        : IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>
    {
        public ValueTask<ModelProviderResponse<BonesStrategyEnhancementResult>> ExecuteAsync(
            ModelProviderRequest<BonesStrategyEnhancementRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(
                new ModelProviderResponse<BonesStrategyEnhancementResult>(
                    payload: new BonesStrategyEnhancementResult("""
                        # Bones Strategy

                        ## Rules
                        - Prefer matching open ends before passing.

                        ## Enhancements
                        - Block earlier when opponents are close to dominoes.
                        """),
                    providerId: "bones-enhance-stub",
                    modelId: request.ModelId,
                    usage: new ModelProviderUsage(96, 48),
                    correlationId: request.CorrelationId));
    }
}
