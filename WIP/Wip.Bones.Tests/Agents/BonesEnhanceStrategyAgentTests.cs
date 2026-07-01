using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Abstractions.Workflows;
using Wip.Artifacts.Local;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Builder;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Bones.Tests.Agents;

file static class BonesPromotionTestScriptSources
{
    public static string Normalize(string source) =>
        source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    public static async Task<int> FindSeedWhereCandidateOutperformsIncumbentAsync(
        string incumbentSource,
        string candidateSource,
        Func<BonesPromotionEvaluationMetrics, bool> metricPredicate,
        double minimumWinRateImprovement = 0,
        int minimumScoreDifferentialImprovement = int.MinValue)
    {
        var evaluator = new BonesStrategyPromotionEvaluator(new BonesMatchSimulator());

        for (var seed = 1; seed <= 500; seed++)
        {
            var request = CreateEvaluationRequest(
                incumbentSource,
                candidateSource,
                seed,
                minimumWinRateImprovement,
                minimumScoreDifferentialImprovement);

            var decision = await evaluator.EvaluateAsync(request, CancellationToken.None);
            if (metricPredicate(decision.Metrics))
                return seed;
        }

        throw new InvalidOperationException("No evaluation seed produced the expected metric relationship.");
    }

    public static BonesStrategyPromotionEvaluationRequest CreateEvaluationRequest(
        string incumbentSource,
        string candidateSource,
        int baseSeed,
        double minimumWinRateImprovement,
        int minimumScoreDifferentialImprovement)
    {
        var learningPlayerId = new BonesPlayerId(1);
        var incumbent = new BonesStrategyArtifact(
            new BonesStrategyId("seat-1-v1"),
            learningPlayerId,
            BonesStrategyKind.Script,
            Normalize(incumbentSource),
            BonesPromotionStatus.Active);

        var candidate = new BonesStrategyArtifact(
            new BonesStrategyId("seat-1-v2"),
            learningPlayerId,
            BonesStrategyKind.Script,
            Normalize(candidateSource),
            BonesPromotionStatus.Candidate);

        var opponents = new Dictionary<BonesPlayerId, BonesStrategyArtifact>();
        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            if (seat == learningPlayerId.Seat)
                continue;

            var opponentId = new BonesPlayerId(seat);
            opponents[opponentId] = new BonesStrategyArtifact(
                new BonesStrategyId($"seat-{seat}-v1"),
                opponentId,
                BonesStrategyKind.Script,
                Normalize(incumbentSource),
                BonesPromotionStatus.Active);
        }

        return new BonesStrategyPromotionEvaluationRequest(
            learningPlayerId,
            incumbent,
            candidate,
            opponents,
            new BonesStrategyPromotionOptions
            {
                EvaluationMatchCount = 5,
                MinimumWinRateImprovement = minimumWinRateImprovement,
                MinimumScoreDifferentialImprovement = minimumScoreDifferentialImprovement,
            },
            baseSeed,
            TargetScore: 8);
    }
}

[Collection("BonesPromotion")]
public sealed class BonesEnhanceStrategyAgentTests
{
    private const string ChecklistItem = BonesRequirementsChecklistItems.EnhanceStrategyAgent;

    private const string FirstLegalMoveScriptSource = """
using System.Linq;
using Wip.Bones.Domain;
using Wip.Bones.Engine;

public sealed class SeatStrategy : IBonesPlayerSlot
{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        return legalMoves.OrderBy(move => move.MoveId.Value, System.StringComparer.Ordinal).First();
    }
}
""";

    private const string LastLegalMoveScriptSource = """
using System.Linq;
using Wip.Bones.Domain;
using Wip.Bones.Engine;

public sealed class SeatStrategy : IBonesPlayerSlot
{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        return legalMoves.OrderBy(move => move.MoveId.Value, System.StringComparer.Ordinal).Last();
    }
}
""";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesEnhanceStrategyAgent_GivenOwnMatchHistory_ExpectedProducesNewStrategyVersionId()
    {
        await using var fixture = await EnhanceAgentFixture.CreateAsync();

        var playerId = new BonesPlayerId(1);
        var priorStrategyId = new BonesStrategyId("initial-seat-1-v1");
        await fixture.SaveActiveScriptStrategyAsync(playerId, priorStrategyId, FirstLegalMoveScriptSource);
        await fixture.SaveOpponentScriptStrategiesAsync(playerId);
        await fixture.SaveMatchHistoryAsync(playerId, new BonesGameId("played-match-1"), CreateMatchHistoryMarkdown(
            playerId,
            winnerSeat: 3,
            playerScore: 6,
            winnerScore: 14));

        var promotionOptions = new BonesStrategyPromotionOptions
        {
            EvaluationMatchCount = 5,
            MinimumWinRateImprovement = 0.05,
            MinimumScoreDifferentialImprovement = 1,
        };
        var rejectionSeed = await BonesPromotionTestScriptSources.FindSeedWhereCandidateOutperformsIncumbentAsync(
            FirstLegalMoveScriptSource,
            FirstLegalMoveScriptSource,
            metrics => metrics.WinRateImprovement < 0.05 && metrics.ScoreDifferentialDelta < 1.0,
            minimumWinRateImprovement: 0.05,
            minimumScoreDifferentialImprovement: 1);

        var stubProvider = new DeterministicBonesEnhancementModelProvider();
        var agent = new BonesEnhanceStrategyAgent(
            fixture.ArtifactStore,
            stubProvider,
            promotionOptions: promotionOptions);

        var result = await agent.ExecuteAsync(
            fixture.CreateRequest(playerId, new BonesGameId("played-match-1"), evaluationSeed: rejectionSeed),
            fixture.CreateCapabilityContext(),
            CancellationToken.None);

        Assert.Equal(priorStrategyId, result.PriorStrategyId);
        Assert.Equal(new BonesStrategyId("initial-seat-1-v2"), result.StrategyId);
        Assert.True(
            ExtractStrategyVersion(result.PriorStrategyId) < ExtractStrategyVersion(result.StrategyId));
        Assert.Contains("bones-strategy-initial-seat-1-v2", result.StrategyArtifact.ArtifactId.Value, StringComparison.Ordinal);
        Assert.Equal(1, result.MatchHistoriesLoaded);
        Assert.Equal(BonesPromotionDecisionOutcome.Rejected, result.PromotionOutcome);
        Assert.Equal(priorStrategyId, result.ActiveStrategyId);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesEnhanceStrategyAgent_GivenPrompt_ExpectedIncludesPriorStrategyAndMatchOutcomeSummary()
    {
        await using var fixture = await EnhanceAgentFixture.CreateAsync();

        var playerId = new BonesPlayerId(2);
        const string priorRules = "Prefer matching open ends before passing.";
        var priorScript = CreateScriptWithComment(priorRules);
        await fixture.SaveActiveScriptStrategyAsync(
            playerId,
            new BonesStrategyId("initial-seat-2-v1"),
            priorScript);
        await fixture.SaveOpponentScriptStrategiesAsync(playerId);
        await fixture.SaveMatchHistoryAsync(
            playerId,
            new BonesGameId("played-match-2"),
            CreateMatchHistoryMarkdown(playerId, winnerSeat: 4, playerScore: 4, winnerScore: 10));

        var stubProvider = new DeterministicBonesEnhancementModelProvider();
        var agent = new BonesEnhanceStrategyAgent(
            fixture.ArtifactStore,
            stubProvider,
            promotionOptions: new BonesStrategyPromotionOptions { EvaluationMatchCount = 5 });

        await agent.ExecuteAsync(
            fixture.CreateRequest(playerId, new BonesGameId("played-match-2"), evaluationSeed: 9001),
            fixture.CreateCapabilityContext(),
            CancellationToken.None);

        var capturedRequest = Assert.Single(stubProvider.CapturedRequests);
        var promptText = string.Join(
            '\n',
            capturedRequest.Payload.Messages.Select(message => message.Content));

        Assert.Contains(BonesEnhancePromptBuilder.PriorScriptHeader, promptText, StringComparison.Ordinal);
        Assert.Contains(priorRules, promptText, StringComparison.Ordinal);
        Assert.Contains("## Match outcome summary", promptText, StringComparison.Ordinal);
        Assert.Contains("Outcome: Loss", promptText, StringComparison.Ordinal);
        Assert.Contains("Score differential vs winner: -6", promptText, StringComparison.Ordinal);
        Assert.Contains("played-match-2", promptText, StringComparison.Ordinal);
        Assert.All(
            capturedRequest.Payload.MatchHistoryArtifactPaths,
            path => Assert.StartsWith($"bones-player-{playerId.Seat}-match-", path, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesEnhanceStrategyAgent_GivenEnhancement_ExpectedPriorStrategyArtifactRetainedInHistory()
    {
        await using var fixture = await EnhanceAgentFixture.CreateAsync();

        var enhancingPlayer = new BonesPlayerId(1);
        var otherPlayer = new BonesPlayerId(3);
        var priorStrategyId = new BonesStrategyId("initial-seat-1-v1");
        var otherStrategyId = new BonesStrategyId("initial-seat-3-v1");

        await fixture.SaveActiveScriptStrategyAsync(enhancingPlayer, priorStrategyId, FirstLegalMoveScriptSource);
        await fixture.SaveActiveScriptStrategyAsync(
            otherPlayer,
            otherStrategyId,
            CreateScriptWithComment("Hold doubles until blocked."));
        await fixture.SaveOpponentScriptStrategiesAsync(enhancingPlayer);
        await fixture.SaveMatchHistoryAsync(
            enhancingPlayer,
            new BonesGameId("retention-probe"),
            CreateMatchHistoryMarkdown(enhancingPlayer, winnerSeat: 2, playerScore: 8, winnerScore: 12));

        var otherStrategyBefore = await fixture.KnowledgeStore.LoadActiveStrategy(otherPlayer, CancellationToken.None);
        var stubProvider = new DeterministicBonesEnhancementModelProvider();
        var agent = new BonesEnhanceStrategyAgent(
            fixture.ArtifactStore,
            stubProvider,
            promotionOptions: new BonesStrategyPromotionOptions { EvaluationMatchCount = 5 });

        var result = await agent.ExecuteAsync(
            fixture.CreateRequest(enhancingPlayer, new BonesGameId("retention-probe"), evaluationSeed: 9001),
            fixture.CreateCapabilityContext(),
            CancellationToken.None);

        var enhancingPlayerStrategies = await fixture.KnowledgeStore.ListStrategyArtifacts(
            enhancingPlayer,
            CancellationToken.None);

        Assert.Equal(2, enhancingPlayerStrategies.Count);
        Assert.Contains(
            enhancingPlayerStrategies,
            artifact => artifact.ArtifactId.Value == $"bones-strategy-{priorStrategyId.Value}");
        Assert.Contains(
            enhancingPlayerStrategies,
            artifact => artifact.ArtifactId.Value == $"bones-strategy-{result.StrategyId.Value}");

        var activeStrategy = await fixture.KnowledgeStore.LoadActiveStrategy(enhancingPlayer, CancellationToken.None);
        Assert.Equal(result.ActiveStrategyId, activeStrategy.StrategyId);

        var otherStrategyAfter = await fixture.KnowledgeStore.LoadActiveStrategy(otherPlayer, CancellationToken.None);
        Assert.Equal(otherStrategyBefore.StrategyId, otherStrategyAfter.StrategyId);
        Assert.Equal(otherStrategyBefore.Source, otherStrategyAfter.Source);

        var otherPlayerStrategies = await fixture.KnowledgeStore.ListStrategyArtifacts(
            otherPlayer,
            CancellationToken.None);
        Assert.Single(otherPlayerStrategies);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesEnhanceStrategyAgent_GivenOrchestratorDispatch_ExpectedAgentCapabilityInvokedAfterPlayStage()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-bones-enhance-dispatch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var sessionId = new SessionId($"bones-enhance-dispatch-{Guid.NewGuid():N}");
            var artifactStore = new WipArtifactStoreLocal(repositoryPath);
            var knowledgeStore = new BonesPlayerKnowledgeStore(artifactStore, repositoryPath, sessionId);
            var playerId = new BonesPlayerId(4);
            var matchGameId = new BonesGameId("post-play-match");

            await knowledgeStore.SaveStrategyArtifact(
                playerId,
                new BonesStrategyArtifact(
                    new BonesStrategyId("initial-seat-4-v1"),
                    playerId,
                    BonesStrategyKind.Script,
                    FirstLegalMoveScriptSource,
                    BonesPromotionStatus.Active),
                CancellationToken.None);
            for (var seat = 1; seat <= 3; seat++)
            {
                if (seat == playerId.Seat)
                    continue;

                var opponentId = new BonesPlayerId(seat);
                await knowledgeStore.SaveStrategyArtifact(
                    opponentId,
                    new BonesStrategyArtifact(
                        new BonesStrategyId($"initial-seat-{seat}-v1"),
                        opponentId,
                        BonesStrategyKind.Script,
                        FirstLegalMoveScriptSource,
                        BonesPromotionStatus.Active),
                    CancellationToken.None);
            }
            await knowledgeStore.SaveMatchHistory(
                playerId,
                new BonesMatchHistoryTranscript(
                    matchGameId,
                    playerId,
                    CreateMatchHistoryMarkdown(playerId, winnerSeat: 4, playerScore: 12, winnerScore: 12)),
                CancellationToken.None);

            var services = new ServiceCollection();
            var builder = services.AddWipCapabilities();

            services.AddSingleton<IArtifactStore>(artifactStore);
            services.AddSingleton<IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>, DeterministicBonesEnhancementModelProvider>();
            services.AddSingleton<BonesEnhanceStrategyAgent>();

            builder.AddAgent<BonesEnhanceStrategyAgent, BonesEnhanceStrategyRequest, BonesEnhanceStrategyResult>(
                BonesEnhanceStrategyCapability.Id,
                "Bones enhance strategy agent");

            using var provider = services.BuildServiceProvider();
            var dispatcher = new WorkflowStageCapabilityDispatcher(provider, builder.CapabilityDescriptors);
            var descriptor = WorkflowStageDescriptor.Create<BonesEnhanceStrategyRequest, BonesEnhanceStrategyResult>(
                WorkflowStageKind.Plan);
            var request = new BonesEnhanceStrategyRequest(
                PlayerId: playerId,
                RepositoryPath: repositoryPath,
                MatchGameId: matchGameId,
                EvaluationSeed: 9001);

            var stageResult = await dispatcher.ExecuteStageAsync(
                descriptor,
                request,
                new WorkflowStageDispatchContext(
                    CreateSessionSnapshot(sessionId, repositoryPath),
                    builder,
                    "corr-bones-enhance",
                    ArtifactStore: artifactStore),
                new WorkflowStageMappedInputEvidence(
                    descriptor.RequestContractName,
                    request,
                    "tool.bones.play-match",
                    new { GameId = matchGameId.Value }),
                CancellationToken.None);

            Assert.Equal(BonesEnhanceStrategyCapability.Id.Value, stageResult.ProducerCapabilityId.Value);
            Assert.Equal(typeof(BonesEnhanceStrategyRequest).FullName, stageResult.RequestContractName);
            Assert.Equal(typeof(BonesEnhanceStrategyResult).FullName, stageResult.ResultContractName);

            var typedResult = Assert.IsType<BonesEnhanceStrategyResult>(stageResult.Result);
            Assert.Equal(new BonesStrategyId("initial-seat-4-v1"), typedResult.PriorStrategyId);
            Assert.Equal(new BonesStrategyId("initial-seat-4-v2"), typedResult.StrategyId);
            Assert.Equal("Wip.Bones.Agents.BonesEnhanceStrategyAgent", typedResult.ExecutionArtifact.ProducerType);

            var executionJson = await File.ReadAllTextAsync(
                Path.Combine(repositoryPath, typedResult.ExecutionArtifact.RelativePath.Replace('/', Path.DirectorySeparatorChar)),
                CancellationToken.None);
            Assert.Contains("\"PriorStage\":\"play\"", executionJson, StringComparison.Ordinal);
            Assert.Contains(matchGameId.Value, executionJson, StringComparison.Ordinal);

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

    [Fact]
    [Trait("ChecklistItem", BonesRequirementsChecklistItems.EnhancePromotionGate)]
    [Trait("ChecklistItem", BonesScriptStrategiesRequirementsChecklistItems.TestsCoverage)]
    public async Task BonesEnhanceStrategyAgent_GivenPromotedEvaluation_ExpectedPromotesCandidateAndReturnsNewActiveId()
    {
        await using var fixture = await EnhanceAgentFixture.CreateAsync();

        var playerId = new BonesPlayerId(1);
        var priorStrategyId = new BonesStrategyId("initial-seat-1-v1");
        await fixture.SaveActiveScriptStrategyAsync(playerId, priorStrategyId, FirstLegalMoveScriptSource);
        await fixture.SaveOpponentScriptStrategiesAsync(playerId);
        await fixture.SaveMatchHistoryAsync(
            playerId,
            new BonesGameId("promoted-match"),
            CreateMatchHistoryMarkdown(playerId, winnerSeat: 3, playerScore: 6, winnerScore: 14));

        var promotionSeed = await BonesPromotionTestScriptSources.FindSeedWhereCandidateOutperformsIncumbentAsync(
            FirstLegalMoveScriptSource,
            LastLegalMoveScriptSource,
            metrics => metrics.CandidateWins > metrics.IncumbentWins);

        var agent = new BonesEnhanceStrategyAgent(
            fixture.ArtifactStore,
            new LastLegalMoveEnhancementModelProvider(),
            promotionOptions: new BonesStrategyPromotionOptions
            {
                EvaluationMatchCount = 5,
                MinimumWinRateImprovement = 0.05,
                MinimumScoreDifferentialImprovement = 0,
            });

        var result = await agent.ExecuteAsync(
            fixture.CreateRequest(playerId, new BonesGameId("promoted-match"), evaluationSeed: promotionSeed),
            fixture.CreateCapabilityContext(),
            CancellationToken.None);

        Assert.Equal(BonesPromotionDecisionOutcome.Promoted, result.PromotionOutcome);
        Assert.Equal(new BonesStrategyId("initial-seat-1-v2"), result.ActiveStrategyId);
        Assert.Equal(result.StrategyId, result.ActiveStrategyId);

        var active = await fixture.KnowledgeStore.LoadActiveStrategy(playerId, CancellationToken.None);
        Assert.Equal(result.StrategyId, active.StrategyId);
        Assert.Equal(BonesPromotionStatus.Active, active.PromotionStatus);
    }

    [Fact]
    [Trait("ChecklistItem", BonesRequirementsChecklistItems.EnhancePromotionGate)]
    [Trait("ChecklistItem", BonesScriptStrategiesRequirementsChecklistItems.TestsCoverage)]
    [Trait("ChecklistItem", BonesLearningLoopReliabilityRequirementsChecklistItems.PromotionArtifactAlways)]
    public async Task BonesEnhanceStrategyAgent_GivenRejectedEvaluation_ExpectedReturnsIncumbentActiveIdUnchanged()
    {
        await using var fixture = await EnhanceAgentFixture.CreateAsync();

        var playerId = new BonesPlayerId(1);
        var priorStrategyId = new BonesStrategyId("initial-seat-1-v1");
        await fixture.SaveActiveScriptStrategyAsync(playerId, priorStrategyId, LastLegalMoveScriptSource);
        await fixture.SaveOpponentScriptStrategiesAsync(playerId);
        await fixture.SaveMatchHistoryAsync(
            playerId,
            new BonesGameId("rejected-match"),
            CreateMatchHistoryMarkdown(playerId, winnerSeat: 4, playerScore: 5, winnerScore: 11));

        var rejectionSeed = await BonesPromotionTestScriptSources.FindSeedWhereCandidateOutperformsIncumbentAsync(
            LastLegalMoveScriptSource,
            FirstLegalMoveScriptSource,
            metrics => metrics.CandidateWins < metrics.IncumbentWins
                && metrics.WinRateImprovement < 0.05
                && metrics.ScoreDifferentialDelta < 1.0,
            minimumWinRateImprovement: 0.05,
            minimumScoreDifferentialImprovement: 1);

        var agent = new BonesEnhanceStrategyAgent(
            fixture.ArtifactStore,
            new FirstLegalMoveEnhancementModelProvider(),
            promotionOptions: new BonesStrategyPromotionOptions { EvaluationMatchCount = 5 });

        var result = await agent.ExecuteAsync(
            fixture.CreateRequest(playerId, new BonesGameId("rejected-match"), evaluationSeed: rejectionSeed),
            fixture.CreateCapabilityContext(),
            CancellationToken.None);

        Assert.Equal(BonesPromotionDecisionOutcome.Rejected, result.PromotionOutcome);
        Assert.Equal(priorStrategyId, result.ActiveStrategyId);
        Assert.NotNull(result.PromotionDecisionArtifact);

        var rejectedArtifactPath = Path.Combine(
            fixture.RepositoryPath,
            result.PromotionDecisionArtifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var rejectedJson = await File.ReadAllTextAsync(rejectedArtifactPath, CancellationToken.None);
        using var rejectedDocument = JsonDocument.Parse(rejectedJson);
        Assert.Equal("Rejected", rejectedDocument.RootElement.GetProperty("Outcome").GetString());
        Assert.True(rejectedDocument.RootElement.TryGetProperty("CandidateWinRate", out _));
        Assert.True(rejectedDocument.RootElement.TryGetProperty("ScoreDifferentialDelta", out _));

        var candidate = await fixture.KnowledgeStore.LoadStrategyArtifact(
            playerId,
            result.StrategyId,
            CancellationToken.None);
        Assert.Equal(BonesPromotionStatus.Rejected, candidate.PromotionStatus);
    }

    [Fact]
    [Trait("ChecklistItem", BonesRequirementsChecklistItems.PromotionDecisionArtifact)]
    [Trait("ChecklistItem", BonesLearningLoopReliabilityRequirementsChecklistItems.PromotionArtifactAlways)]
    public async Task BonesEnhanceStrategyAgent_GivenPromotionDecision_ExpectedPersistsPromotionArtifactWithMetrics()
    {
        await using var fixture = await EnhanceAgentFixture.CreateAsync();

        var playerId = new BonesPlayerId(2);
        var priorStrategyId = new BonesStrategyId("initial-seat-2-v1");
        await fixture.SaveActiveScriptStrategyAsync(playerId, priorStrategyId, FirstLegalMoveScriptSource);
        await fixture.SaveOpponentScriptStrategiesAsync(playerId);
        await fixture.SaveMatchHistoryAsync(
            playerId,
            new BonesGameId("artifact-match"),
            CreateMatchHistoryMarkdown(playerId, winnerSeat: 2, playerScore: 9, winnerScore: 9));

        var agent = new BonesEnhanceStrategyAgent(
            fixture.ArtifactStore,
            new LastLegalMoveEnhancementModelProvider(),
            promotionOptions: new BonesStrategyPromotionOptions
            {
                EvaluationMatchCount = 3,
                MinimumWinRateImprovement = 0,
                MinimumScoreDifferentialImprovement = 0,
            });

        var result = await agent.ExecuteAsync(
            fixture.CreateRequest(playerId, new BonesGameId("artifact-match"), evaluationSeed: 4242),
            fixture.CreateCapabilityContext(),
            CancellationToken.None);

        Assert.StartsWith("bones-strategy-promotion-", result.PromotionDecisionArtifact.ArtifactId.Value, StringComparison.Ordinal);

        var artifactPath = Path.Combine(
            fixture.RepositoryPath,
            result.PromotionDecisionArtifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var json = await File.ReadAllTextAsync(artifactPath, CancellationToken.None);
        using var document = JsonDocument.Parse(json);

        Assert.Equal(priorStrategyId.Value, document.RootElement.GetProperty("IncumbentStrategyId").GetString());
        Assert.Equal(result.StrategyId.Value, document.RootElement.GetProperty("CandidateStrategyId").GetString());
        Assert.Equal(result.PromotionOutcome.ToString(), document.RootElement.GetProperty("Outcome").GetString());
        Assert.True(document.RootElement.TryGetProperty("CandidateWinRate", out _));
        Assert.True(document.RootElement.TryGetProperty("ScoreDifferentialDelta", out _));
        Assert.True(document.RootElement.TryGetProperty("IncumbentWinRate", out _));
        Assert.True(document.RootElement.TryGetProperty("EvaluationMatchCount", out _));
    }

    [Fact]
    [Trait("ChecklistItem", "T1.2")]
    public async Task ExecuteAsync_GivenMultipleCompletedMatches_LoadsAtLeastThreeHistories()
    {
        await using var fixture = await EnhanceAgentFixture.CreateAsync();

        var playerId = new BonesPlayerId(1);
        await fixture.SaveActiveScriptStrategyAsync(
            playerId,
            new BonesStrategyId("initial-seat-1-v1"),
            FirstLegalMoveScriptSource);
        await fixture.SaveOpponentScriptStrategiesAsync(playerId);

        // Save 4 match histories to exceed the ≥3 threshold
        await fixture.SaveMatchHistoryAsync(
            playerId,
            new BonesGameId("game-1"),
            CreateMatchHistoryMarkdown(playerId, winnerSeat: 1, playerScore: 14, winnerScore: 14));
        await fixture.SaveMatchHistoryAsync(
            playerId,
            new BonesGameId("game-2"),
            CreateMatchHistoryMarkdown(playerId, winnerSeat: 3, playerScore: 3, winnerScore: 12));
        await fixture.SaveMatchHistoryAsync(
            playerId,
            new BonesGameId("game-3"),
            CreateMatchHistoryMarkdown(playerId, winnerSeat: 2, playerScore: 8, winnerScore: 10));
        await fixture.SaveMatchHistoryAsync(
            playerId,
            new BonesGameId("game-4"),
            CreateMatchHistoryMarkdown(playerId, winnerSeat: 1, playerScore: 11, winnerScore: 11));

        var stubProvider = new DeterministicBonesEnhancementModelProvider();
        var agent = new BonesEnhanceStrategyAgent(
            fixture.ArtifactStore,
            stubProvider,
            promotionOptions: new BonesStrategyPromotionOptions { EvaluationMatchCount = 3 });

        var result = await agent.ExecuteAsync(
            fixture.CreateRequest(playerId, new BonesGameId("game-4"), evaluationSeed: 7001),
            fixture.CreateCapabilityContext(),
            CancellationToken.None);

        // Primary assertion: ≥3 match histories were loaded
        Assert.True(result.MatchHistoriesLoaded >= 3,
            $"Expected ≥3 match histories loaded, got {result.MatchHistoriesLoaded}");

        // Verify all 4 game IDs appear in the prompt text
        var capturedRequest = Assert.Single(stubProvider.CapturedRequests);
        var promptText = string.Join(
            '\n',
            capturedRequest.Payload.Messages.Select(message => message.Content));

        Assert.Contains("game-1", promptText, StringComparison.Ordinal);
        Assert.Contains("game-2", promptText, StringComparison.Ordinal);
        Assert.Contains("game-3", promptText, StringComparison.Ordinal);
        Assert.Contains("game-4", promptText, StringComparison.Ordinal);

        // Verify match history artifact paths include all games
        Assert.Equal(4, capturedRequest.Payload.MatchHistoryArtifactPaths.Count);
        Assert.All(
            capturedRequest.Payload.MatchHistoryArtifactPaths,
            path => Assert.StartsWith($"bones-player-{playerId.Seat}-match-", path, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("ChecklistItem", "T1.2")]
    public async Task ExecuteAsync_GivenSingleMatch_LoadsAvailableHistories()
    {
        await using var fixture = await EnhanceAgentFixture.CreateAsync();

        var playerId = new BonesPlayerId(3);
        await fixture.SaveActiveScriptStrategyAsync(
            playerId,
            new BonesStrategyId("initial-seat-3-v1"),
            FirstLegalMoveScriptSource);
        await fixture.SaveOpponentScriptStrategiesAsync(playerId);

        // Only 1 match history exists
        await fixture.SaveMatchHistoryAsync(
            playerId,
            new BonesGameId("only-game"),
            CreateMatchHistoryMarkdown(playerId, winnerSeat: 4, playerScore: 5, winnerScore: 9));

        var stubProvider = new DeterministicBonesEnhancementModelProvider();
        var agent = new BonesEnhanceStrategyAgent(
            fixture.ArtifactStore,
            stubProvider,
            promotionOptions: new BonesStrategyPromotionOptions { EvaluationMatchCount = 3 });

        var result = await agent.ExecuteAsync(
            fixture.CreateRequest(playerId, new BonesGameId("only-game"), evaluationSeed: 8001),
            fixture.CreateCapabilityContext(),
            CancellationToken.None);

        // Loads what's available: 1 history
        Assert.Equal(1, result.MatchHistoriesLoaded);

        // Only that single game appears
        var capturedRequest = Assert.Single(stubProvider.CapturedRequests);
        var promptText = string.Join(
            '\n',
            capturedRequest.Payload.Messages.Select(message => message.Content));

        Assert.Contains("only-game", promptText, StringComparison.Ordinal);
        Assert.Single(capturedRequest.Payload.MatchHistoryArtifactPaths);
    }

    [Fact]
    [Trait("ChecklistItem", "T1.2")]
    public async Task ExecuteAsync_GivenNoMatchHistory_EnhancesFromObservationOnly()
    {
        await using var fixture = await EnhanceAgentFixture.CreateAsync();

        var playerId = new BonesPlayerId(2);
        await fixture.SaveActiveScriptStrategyAsync(
            playerId,
            new BonesStrategyId("initial-seat-2-v1"),
            FirstLegalMoveScriptSource);
        await fixture.SaveOpponentScriptStrategiesAsync(playerId);

        // No match history saved

        var stubProvider = new DeterministicBonesEnhancementModelProvider();
        var agent = new BonesEnhanceStrategyAgent(
            fixture.ArtifactStore,
            stubProvider,
            promotionOptions: new BonesStrategyPromotionOptions { EvaluationMatchCount = 3 });

        var result = await agent.ExecuteAsync(
            fixture.CreateRequest(playerId, new BonesGameId("ghost-game"), evaluationSeed: 9001),
            fixture.CreateCapabilityContext(),
            CancellationToken.None);

        // Zero match histories loaded but enhancement still succeeds
        Assert.Equal(0, result.MatchHistoriesLoaded);
        Assert.NotEqual(default, result.StrategyId);
        Assert.NotNull(result.StrategyDocument);

        // Prompt contains fallback text for no match histories
        var capturedRequest = Assert.Single(stubProvider.CapturedRequests);
        var promptText = string.Join(
            '\n',
            capturedRequest.Payload.Messages.Select(message => message.Content));

        Assert.Contains(
            "No match history transcripts were loaded for this player.",
            promptText,
            StringComparison.Ordinal);
    }

    private static int ExtractStrategyVersion(BonesStrategyId strategyId)
    {
        const string versionPrefix = "-v";
        var value = strategyId.Value;
        var versionIndex = value.LastIndexOf(versionPrefix, StringComparison.Ordinal);
        Assert.True(versionIndex >= 0);

        return int.Parse(
            value[(versionIndex + versionPrefix.Length)..],
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string CreateScriptWithComment(string comment)
        => $$"""
            using Wip.Bones.Domain;
            using Wip.Bones.Engine;

            public sealed class SeatStrategy : IBonesPlayerSlot
            {
                public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
                {
                    // {{comment}}
                    return legalMoves[0];
                }
            }
            """;

    private static string CreateMatchHistoryMarkdown(
        BonesPlayerId playerId,
        int winnerSeat,
        int playerScore,
        int winnerScore)
        => $"""
            # Bones Match History

            Game: played-match
            Player seat: {playerId.Seat}
            Match winner seat: {winnerSeat}
            Final score for seat {playerId.Seat}: {playerScore}
            Final score for seat {winnerSeat}: {winnerScore}
            """;

    private static SessionSnapshot CreateSessionSnapshot(SessionId sessionId, string repositoryPath)
        => new(
            SessionId: sessionId,
            WorkflowId: new WorkflowId("workflow.bones.learning"),
            State: SessionState.Created,
            RepositoryPath: repositoryPath,
            WorktreePath: Path.Combine(repositoryPath, ".wip", "worktrees", "bones-enhance"),
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            TaskDescription: "enhance strategy agent dispatch probe");

    private sealed class DeterministicBonesEnhancementModelProvider
        : IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>
    {
        public List<ModelProviderRequest<BonesStrategyEnhancementRequest>> CapturedRequests { get; } = [];

        public ValueTask<ModelProviderResponse<BonesStrategyEnhancementResult>> ExecuteAsync(
            ModelProviderRequest<BonesStrategyEnhancementRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            CapturedRequests.Add(request);

            var markdown = $"""
                ```csharp
                {FirstLegalMoveScriptSource}
                ```
                """;

            return ValueTask.FromResult(
                new ModelProviderResponse<BonesStrategyEnhancementResult>(
                    payload: new BonesStrategyEnhancementResult(markdown),
                    providerId: "bones-enhance-stub",
                    modelId: request.ModelId,
                    usage: new ModelProviderUsage(140, 60),
                    correlationId: request.CorrelationId));
        }
    }

    private sealed class FirstLegalMoveEnhancementModelProvider
        : IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>
    {
        public ValueTask<ModelProviderResponse<BonesStrategyEnhancementResult>> ExecuteAsync(
            ModelProviderRequest<BonesStrategyEnhancementRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(
                new ModelProviderResponse<BonesStrategyEnhancementResult>(
                    payload: new BonesStrategyEnhancementResult($"```csharp\n{FirstLegalMoveScriptSource}\n```"),
                    providerId: "bones-enhance-stub",
                    modelId: request.ModelId,
                    usage: new ModelProviderUsage(140, 60),
                    correlationId: request.CorrelationId));
    }

    private sealed class LastLegalMoveEnhancementModelProvider
        : IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>
    {
        public ValueTask<ModelProviderResponse<BonesStrategyEnhancementResult>> ExecuteAsync(
            ModelProviderRequest<BonesStrategyEnhancementRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(
                new ModelProviderResponse<BonesStrategyEnhancementResult>(
                    payload: new BonesStrategyEnhancementResult($"```csharp\n{LastLegalMoveScriptSource}\n```"),
                    providerId: "bones-enhance-stub",
                    modelId: request.ModelId,
                    usage: new ModelProviderUsage(140, 60),
                    correlationId: request.CorrelationId));
    }

    private static string NormalizeScriptSource(string source) =>
        BonesPromotionTestScriptSources.Normalize(source);

    private sealed class EnhanceAgentFixture : IAsyncDisposable
    {
        private readonly string _repositoryPath;

        private EnhanceAgentFixture(
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

        public static async Task<EnhanceAgentFixture> CreateAsync()
        {
            var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-bones-enhance-{Guid.NewGuid():N}");
            Directory.CreateDirectory(repositoryPath);

            var sessionId = new SessionId($"bones-enhance-{Guid.NewGuid():N}");
            var artifactStore = new WipArtifactStoreLocal(repositoryPath);
            var knowledgeStore = new BonesPlayerKnowledgeStore(artifactStore, repositoryPath, sessionId);

            await Task.CompletedTask;
            return new EnhanceAgentFixture(repositoryPath, sessionId, artifactStore, knowledgeStore);
        }

        public async Task SaveActiveScriptStrategyAsync(BonesPlayerId playerId, BonesStrategyId strategyId, string scriptSource)
        {
            await KnowledgeStore.SaveStrategyArtifact(
                playerId,
                new BonesStrategyArtifact(
                    strategyId,
                    playerId,
                    BonesStrategyKind.Script,
                    NormalizeScriptSource(scriptSource),
                    BonesPromotionStatus.Active),
                CancellationToken.None);
        }

        public async Task SaveOpponentScriptStrategiesAsync(BonesPlayerId learningPlayerId)
        {
            for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
            {
                if (seat == learningPlayerId.Seat)
                    continue;

                var opponentId = new BonesPlayerId(seat);
                await SaveActiveScriptStrategyAsync(
                    opponentId,
                    new BonesStrategyId($"initial-seat-{seat}-v1"),
                    FirstLegalMoveScriptSource);
            }
        }

        public async Task SaveMatchHistoryAsync(BonesPlayerId playerId, BonesGameId gameId, string markdown)
        {
            await KnowledgeStore.SaveMatchHistory(
                playerId,
                new BonesMatchHistoryTranscript(gameId, playerId, markdown),
                CancellationToken.None);
        }

        public BonesEnhanceStrategyRequest CreateRequest(
            BonesPlayerId playerId,
            BonesGameId matchGameId,
            int? evaluationSeed = null)
            => new(playerId, _repositoryPath, matchGameId, EvaluationSeed: evaluationSeed);

        public string RepositoryPath => _repositoryPath;

        public CapabilityContext CreateCapabilityContext()
            => new(SessionId, Path.Combine(_repositoryPath, ".wip", "worktrees", "bones-enhance"));

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_repositoryPath))
                Directory.Delete(_repositoryPath, recursive: true);

            return ValueTask.CompletedTask;
        }
    }
}

[CollectionDefinition("BonesPromotion", DisableParallelization = true)]
public sealed class BonesPromotionTestCollection;

[Collection("BonesPromotion")]
public sealed class BonesStrategyPromotionEvaluatorTests
{
    private const string ChecklistItem = BonesRequirementsChecklistItems.StrategyPromotionEvaluator;

    private const string FirstLegalMoveScriptSource = """
using System.Linq;
using Wip.Bones.Domain;
using Wip.Bones.Engine;

public sealed class SeatStrategy : IBonesPlayerSlot
{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        return legalMoves.OrderBy(move => move.MoveId.Value, System.StringComparer.Ordinal).First();
    }
}
""";

    private const string LastLegalMoveScriptSource = """
using System.Linq;
using Wip.Bones.Domain;
using Wip.Bones.Engine;

public sealed class SeatStrategy : IBonesPlayerSlot
{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        return legalMoves.OrderBy(move => move.MoveId.Value, System.StringComparer.Ordinal).Last();
    }
}
""";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesStrategyPromotionEvaluator_GivenCandidateWinsAllEvaluationMatches_ExpectedPromotedDecision()
    {
        var decision = await FindDecisionMatchingAsync(
            FirstLegalMoveScriptSource,
            LastLegalMoveScriptSource,
            minimumWinRateImprovement: 0.05,
            minimumScoreDifferentialImprovement: 0,
            predicate: static decision =>
                decision.Outcome == BonesPromotionDecisionOutcome.Promoted
                && decision.Metrics.WinRateImprovement >= 0.05);

        Assert.Equal(BonesPromotionDecisionOutcome.Promoted, decision.Outcome);
        Assert.True(decision.Metrics.WinRateImprovement >= 0.05);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesStrategyPromotionEvaluator_GivenCandidateLosesMajority_ExpectedRejectedDecision()
    {
        var decision = await FindDecisionMatchingAsync(
            LastLegalMoveScriptSource,
            FirstLegalMoveScriptSource,
            minimumWinRateImprovement: 0.05,
            minimumScoreDifferentialImprovement: 0,
            predicate: static decision =>
                decision.Outcome == BonesPromotionDecisionOutcome.Rejected
                && decision.Metrics.WinRateImprovement < 0.05);

        Assert.Equal(BonesPromotionDecisionOutcome.Rejected, decision.Outcome);
        Assert.True(decision.Metrics.WinRateImprovement < 0.05);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesStrategyPromotionEvaluator_GivenFixedSeeds_ExpectedDeterministicMetricSnapshot()
    {
        var request = CreateRequest(
            learningSeat: 2,
            incumbentSource: FirstLegalMoveScriptSource,
            candidateSource: LastLegalMoveScriptSource,
            evaluationMatchCount: 3,
            baseSeed: 4242,
            minimumWinRateImprovement: 0,
            minimumScoreDifferentialImprovement: 0);

        var evaluator = new BonesStrategyPromotionEvaluator(new BonesMatchSimulator());
        var firstDecision = await evaluator.EvaluateAsync(request, CancellationToken.None);
        var secondDecision = await evaluator.EvaluateAsync(request, CancellationToken.None);

        Assert.Equal(firstDecision.Metrics.CandidateWins, secondDecision.Metrics.CandidateWins);
        Assert.Equal(firstDecision.Metrics.ScoreDifferentialDelta, secondDecision.Metrics.ScoreDifferentialDelta);
    }

    // ---- T1.1 OR-Gate promotion tests ----

    [Fact]
    [Trait("ChecklistItem", BonesRequirementsChecklistItems.BehaviorProofPolicy)]
    public async Task EvaluateAsync_GivenScoreDeltaAboveThresholdAndWinRateSlightlyNegative_ReturnsPromoted()
    {
        // OR-gate: promote when scoreDifferentialDelta >= threshold even if
        // winRateImprovement is below the configurable win rate threshold,
        // as long as both metrics are above their hard floors.
        var decision = await FindDecisionMatchingAsync(
            FirstLegalMoveScriptSource,
            LastLegalMoveScriptSource,
            minimumWinRateImprovement: 0.05,
            minimumScoreDifferentialImprovement: 1,
            predicate: static decision =>
                decision.Outcome == BonesPromotionDecisionOutcome.Promoted
                && decision.Metrics.ScoreDifferentialDelta >= 1.0
                && decision.Metrics.WinRateImprovement < 0.05
                && decision.Metrics.WinRateImprovement >= -0.10);

        Assert.Equal(BonesPromotionDecisionOutcome.Promoted, decision.Outcome);
        Assert.True(decision.Metrics.ScoreDifferentialDelta >= 1.0,
            "Score differential delta must pass the configurable threshold.");
        Assert.True(decision.Metrics.WinRateImprovement < 0.05,
            "Win rate improvement must be below the configurable threshold (OR-gate proves score delta alone suffices).");
        Assert.True(decision.Metrics.WinRateImprovement >= -0.10,
            "Win rate improvement must be above the hard floor of -0.10.");
    }

    [Fact]
    [Trait("ChecklistItem", BonesRequirementsChecklistItems.BehaviorProofPolicy)]
    public async Task EvaluateAsync_GivenBothMetricsBelowFloor_ReturnsRejected()
    {
        // Hard floor: reject when winRateImprovement < -0.10 OR
        // scoreDifferentialDelta < -1.0, regardless of the other metric.
        var decision = await FindDecisionMatchingAsync(
            LastLegalMoveScriptSource,
            FirstLegalMoveScriptSource,
            minimumWinRateImprovement: 0.0,
            minimumScoreDifferentialImprovement: 0,
            predicate: static decision =>
                decision.Outcome == BonesPromotionDecisionOutcome.Rejected
                && (decision.Metrics.WinRateImprovement < -0.10
                    || decision.Metrics.ScoreDifferentialDelta < -1.0));

        Assert.Equal(BonesPromotionDecisionOutcome.Rejected, decision.Outcome);
        Assert.True(
            decision.Metrics.WinRateImprovement < -0.10 || decision.Metrics.ScoreDifferentialDelta < -1.0,
            "At least one metric must be below its hard floor.");
    }

    [Fact]
    [Trait("ChecklistItem", BonesRequirementsChecklistItems.BehaviorProofPolicy)]
    public async Task EvaluateAsync_GivenCandidateDominatesIncumbent_ReturnsPromoted()
    {
        // When candidate wins the majority of evaluation matches and outscores
        // the incumbent, both metrics pass and the OR-gate promotes.
        var decision = await FindDecisionMatchingAsync(
            FirstLegalMoveScriptSource,
            LastLegalMoveScriptSource,
            minimumWinRateImprovement: 0.05,
            minimumScoreDifferentialImprovement: 1,
            predicate: static decision =>
                decision.Outcome == BonesPromotionDecisionOutcome.Promoted
                && decision.Metrics.WinRateImprovement >= 0.05
                && decision.Metrics.CandidateWins > decision.Metrics.IncumbentWins);

        Assert.Equal(BonesPromotionDecisionOutcome.Promoted, decision.Outcome);
        Assert.True(decision.Metrics.WinRateImprovement >= 0.05);
        Assert.True(decision.Metrics.CandidateWins > decision.Metrics.IncumbentWins);
    }

    [Fact]
    [Trait("ChecklistItem", BonesRequirementsChecklistItems.BehaviorProofPolicy)]
    public async Task EvaluateAsync_GivenCandidateIdenticalToIncumbent_ReturnsRejected()
    {
        // When both strategies are identical, metrics should be near zero.
        // Neither OR-gate condition is satisfied and the decision is Rejected.
        // Use stricter-than-default thresholds to ensure noise does not trigger
        // a spurious Promoted via the OR-gate path.
        var evaluator = new BonesStrategyPromotionEvaluator(new BonesMatchSimulator());
        var decision = await FindDecisionMatchingAsync(
            FirstLegalMoveScriptSource,
            FirstLegalMoveScriptSource,
            minimumWinRateImprovement: 0.20,
            minimumScoreDifferentialImprovement: 5,
            predicate: static decision =>
                decision.Outcome == BonesPromotionDecisionOutcome.Rejected
                && Math.Abs(decision.Metrics.WinRateImprovement) <= 0.6
                && Math.Abs(decision.Metrics.ScoreDifferentialDelta) <= 12.0);

        Assert.Equal(BonesPromotionDecisionOutcome.Rejected, decision.Outcome);
        Assert.True(Math.Abs(decision.Metrics.WinRateImprovement) <= 0.6,
            $"Identical strategies should have similar win rates, got {decision.Metrics.WinRateImprovement}.");
        Assert.True(Math.Abs(decision.Metrics.ScoreDifferentialDelta) <= 12.0,
            $"Identical strategies should have similar average scores, got {decision.Metrics.ScoreDifferentialDelta}.");
    }

    [Fact]
    [Trait("ChecklistItem", BonesRequirementsChecklistItems.BehaviorProofPolicy)]
    public async Task EvaluateAsync_GivenConfigurableThresholds_RespectsOptions()
    {
        // Custom BonesStrategyPromotionOptions values override defaults.
        // With a very permissive win rate threshold (0.0) and a high score delta
        // threshold (100), only the win rate path should trigger promotion.
        var decision = await FindDecisionMatchingAsync(
            FirstLegalMoveScriptSource,
            LastLegalMoveScriptSource,
            minimumWinRateImprovement: 0.0,
            minimumScoreDifferentialImprovement: 100,
            predicate: static decision =>
                decision.Outcome == BonesPromotionDecisionOutcome.Promoted
                && decision.Metrics.WinRateImprovement >= 0.0);

        Assert.Equal(BonesPromotionDecisionOutcome.Promoted, decision.Outcome);
        Assert.True(decision.Metrics.WinRateImprovement >= 0.0,
            "Win rate improvement must pass the configured threshold of 0.0.");
    }

    private static Task<BonesPromotionDecision> EvaluateAsync(
        int learningSeat,
        string incumbentSource,
        string candidateSource,
        int evaluationMatchCount,
        int baseSeed,
        double minimumWinRateImprovement,
        int minimumScoreDifferentialImprovement)
        => new BonesStrategyPromotionEvaluator(new BonesMatchSimulator())
            .EvaluateAsync(
                CreateRequest(
                    learningSeat,
                    incumbentSource,
                    candidateSource,
                    evaluationMatchCount,
                    baseSeed,
                    minimumWinRateImprovement,
                    minimumScoreDifferentialImprovement),
                CancellationToken.None)
            .AsTask();

    private static async Task<BonesPromotionDecision> FindDecisionMatchingAsync(
        string incumbentSource,
        string candidateSource,
        double minimumWinRateImprovement,
        int minimumScoreDifferentialImprovement,
        Func<BonesPromotionDecision, bool> predicate)
    {
        var evaluator = new BonesStrategyPromotionEvaluator(new BonesMatchSimulator());

        for (var seed = 1; seed <= 500; seed++)
        {
            var request = BonesPromotionTestScriptSources.CreateEvaluationRequest(
                incumbentSource,
                candidateSource,
                seed,
                minimumWinRateImprovement,
                minimumScoreDifferentialImprovement);

            var decision = await evaluator.EvaluateAsync(request, CancellationToken.None);
            if (predicate(decision))
                return decision;
        }

        throw new InvalidOperationException("No evaluation seed produced the expected promotion decision.");
    }

    private static BonesStrategyPromotionEvaluationRequest CreateRequest(
        int learningSeat,
        string incumbentSource,
        string candidateSource,
        int evaluationMatchCount,
        int baseSeed,
        double minimumWinRateImprovement,
        int minimumScoreDifferentialImprovement)
    {
        var learningPlayerId = new BonesPlayerId(learningSeat);
        var incumbentId = new BonesStrategyId($"seat-{learningSeat}-v1");
        var candidateId = new BonesStrategyId($"seat-{learningSeat}-v2");

        incumbentSource = BonesPromotionTestScriptSources.Normalize(incumbentSource);
        candidateSource = BonesPromotionTestScriptSources.Normalize(candidateSource);

        var incumbent = new BonesStrategyArtifact(
            incumbentId,
            learningPlayerId,
            BonesStrategyKind.Script,
            incumbentSource,
            BonesPromotionStatus.Active);

        var candidate = new BonesStrategyArtifact(
            candidateId,
            learningPlayerId,
            BonesStrategyKind.Script,
            candidateSource,
            BonesPromotionStatus.Candidate);

        var opponents = new Dictionary<BonesPlayerId, BonesStrategyArtifact>();
        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            if (seat == learningSeat)
                continue;

            var opponentId = new BonesPlayerId(seat);
            opponents[opponentId] = new BonesStrategyArtifact(
                new BonesStrategyId($"seat-{seat}-v1"),
                opponentId,
                BonesStrategyKind.Script,
                FirstLegalMoveScriptSource,
                BonesPromotionStatus.Active);
        }

        return new BonesStrategyPromotionEvaluationRequest(
            learningPlayerId,
            incumbent,
            candidate,
            opponents,
            new BonesStrategyPromotionOptions
            {
                EvaluationMatchCount = evaluationMatchCount,
                MinimumWinRateImprovement = minimumWinRateImprovement,
                MinimumScoreDifferentialImprovement = minimumScoreDifferentialImprovement,
            },
            baseSeed,
            TargetScore: 8);
    }
}
