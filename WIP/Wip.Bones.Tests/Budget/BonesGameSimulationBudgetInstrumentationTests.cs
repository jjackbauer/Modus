using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Artifacts.Local;
using Wip.Bones.Agents.Budget;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Observe;
using Wip.Bones.Agents.Play;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Bones.Tests;
using Xunit;

namespace Wip.Bones.Tests.Budget;

public sealed class BonesGameSimulationBudgetInstrumentationTests
{
    private const string ChecklistItem = BonesRequirementsChecklistItems.GameSimulationBudgetInstrumentation;

    private const string FirstLegalMoveScriptSource =
        """
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

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesObserveGamesTool_GivenBudget_ExpectedRecordsObservedGameCount()
    {
        var budget = new BonesGameSimulationBudget();
        var fixture = await ObserveFixture.CreateAsync(budget);

        await fixture.Tool.ExecuteAsync(
            fixture.CreateRequest(gameCount: 3),
            fixture.Context,
            CancellationToken.None);

        Assert.Equal(3, budget.GamesSimulated);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesPlayMatchTool_GivenBudget_ExpectedRecordsOnePlayMatch()
    {
        var budget = new BonesGameSimulationBudget();
        var fixture = await PlayFixture.CreateAsync(budget);

        await fixture.Tool.ExecuteAsync(fixture.Request, fixture.Context, CancellationToken.None);

        Assert.Equal(1, budget.GamesSimulated);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesStrategyPromotionEvaluator_GivenBudget_ExpectedRecordsEvaluationMatchCount()
    {
        var budget = new BonesGameSimulationBudget();
        const int evaluationMatchCount = 4;
        var evaluator = new BonesStrategyPromotionEvaluator(new BonesMatchSimulator(), gameBudget: budget);
        var learningPlayerId = new BonesPlayerId(1);
        var incumbent = CreateScriptArtifact(learningPlayerId, "initial-seat-1-v1", FirstLegalMoveScriptSource);
        var candidate = CreateScriptArtifact(learningPlayerId, "initial-seat-1-v2", FirstLegalMoveScriptSource);

        await evaluator.EvaluateAsync(
            new BonesStrategyPromotionEvaluationRequest(
                learningPlayerId,
                incumbent,
                candidate,
                CreateOpponentStrategies(),
                new BonesStrategyPromotionOptions { EvaluationMatchCount = evaluationMatchCount },
                BaseSeed: 99),
            CancellationToken.None);

        Assert.Equal(evaluationMatchCount * 2, budget.GamesSimulated);
    }

    private static BonesStrategyArtifact CreateScriptArtifact(
        BonesPlayerId playerId,
        string strategyId,
        string source)
        => new(
            new BonesStrategyId(strategyId),
            playerId,
            BonesStrategyKind.Script,
            source,
            BonesPromotionStatus.Active);

    private static IReadOnlyDictionary<BonesPlayerId, BonesStrategyArtifact> CreateOpponentStrategies()
    {
        var strategies = new Dictionary<BonesPlayerId, BonesStrategyArtifact>();
        foreach (var seat in new[] { 2, 3, 4 })
        {
            var playerId = new BonesPlayerId(seat);
            strategies[playerId] = CreateScriptArtifact(
                playerId,
                $"initial-seat-{seat}-v1",
                FirstLegalMoveScriptSource);
        }

        return strategies;
    }

    private sealed class ObserveFixture : IAsyncDisposable
    {
        public BonesObserveGamesTool Tool { get; }
        public CapabilityContext Context { get; }
        private readonly string _repositoryPath;

        private ObserveFixture(BonesObserveGamesTool tool, CapabilityContext context, string repositoryPath)
        {
            Tool = tool;
            Context = context;
            _repositoryPath = repositoryPath;
        }

        public static Task<ObserveFixture> CreateAsync(BonesGameSimulationBudget budget)
        {
            var repositoryPath = Path.Combine(Path.GetTempPath(), $"bones-budget-observe-{Guid.NewGuid():N}");
            Directory.CreateDirectory(repositoryPath);
            var sessionId = new SessionId(Guid.NewGuid().ToString("N"));
            var tool = new BonesObserveGamesTool(
                new WipArtifactStoreLocal(repositoryPath),
                new BonesMatchSimulator(),
                gameBudget: budget);

            return Task.FromResult(new ObserveFixture(
                tool,
                new CapabilityContext(sessionId, repositoryPath),
                repositoryPath));
        }

        public BonesObserveGamesRequest CreateRequest(int gameCount)
            => new(
                gameCount,
                Seed: 4242,
                TargetScore: 8,
                RepositoryPath: _repositoryPath,
                ObserverPlayerIds: [new BonesPlayerId(1)]);

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_repositoryPath))
                Directory.Delete(_repositoryPath, recursive: true);

            return ValueTask.CompletedTask;
        }
    }

    private sealed class PlayFixture : IAsyncDisposable
    {
        public BonesPlayMatchTool Tool { get; }
        public BonesPlayMatchRequest Request { get; }
        public CapabilityContext Context { get; }
        private readonly string _repositoryPath;

        private PlayFixture(
            BonesPlayMatchTool tool,
            BonesPlayMatchRequest request,
            CapabilityContext context,
            string repositoryPath)
        {
            Tool = tool;
            Request = request;
            Context = context;
            _repositoryPath = repositoryPath;
        }

        public static async Task<PlayFixture> CreateAsync(BonesGameSimulationBudget budget)
        {
            var repositoryPath = Path.Combine(Path.GetTempPath(), $"bones-budget-play-{Guid.NewGuid():N}");
            Directory.CreateDirectory(repositoryPath);
            var sessionId = new SessionId(Guid.NewGuid().ToString("N"));
            var artifactStore = new WipArtifactStoreLocal(repositoryPath);
            var knowledgeStore = new BonesPlayerKnowledgeStore(artifactStore, repositoryPath, sessionId);

            foreach (var seat in Enumerable.Range(BonesPlayerId.MinSeat, BonesTableConfig.FixedPlayerCount))
            {
                var playerId = new BonesPlayerId(seat);
                await knowledgeStore.SaveStrategy(
                    playerId,
                    new BonesStrategyDocument(
                        new BonesStrategyId($"initial-seat-{seat}-v1"),
                        playerId,
                        markdown: "# Strategy\n- Play first legal move."),
                    CancellationToken.None);
            }

            var tool = new BonesPlayMatchTool(
                artifactStore,
                new BonesGameEngine(),
                new StubPlayTurnModelProvider(),
                gameBudget: budget);

            return new PlayFixture(
                tool,
                new BonesPlayMatchRequest(
                    Seed: 4242,
                    TargetScore: 8,
                    RepositoryPath: repositoryPath),
                new CapabilityContext(sessionId, repositoryPath),
                repositoryPath);
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_repositoryPath))
                Directory.Delete(_repositoryPath, recursive: true);

            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubPlayTurnModelProvider
        : IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>
    {
        public ValueTask<ModelProviderResponse<BonesPlayTurnResult>> ExecuteAsync(
            ModelProviderRequest<BonesPlayTurnRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(
                new ModelProviderResponse<BonesPlayTurnResult>(
                    payload: new BonesPlayTurnResult(request.Payload.AllowedMoves[0].MoveId.Value),
                    providerId: "stub",
                    modelId: request.ModelId,
                    usage: new ModelProviderUsage(1, 1),
                    correlationId: request.CorrelationId));
    }
}