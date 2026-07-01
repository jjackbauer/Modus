using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Agents.Budget;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Host;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Host.Tests;

[Collection("BonesHostPromotionIntegration")]
public sealed class BonesLearningLoopPromotionHostTests
{
    private const string ChecklistItem = BonesScriptStrategiesHostRequirementsChecklistItems.PromotionHostIntegration;

    private static readonly Lazy<int> PromotionEvaluationSeedLazy = new(FindPromotionSeed);

    private static int PromotionEvaluationSeed => PromotionEvaluationSeedLazy.Value;

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
    public async Task BonesLearningLoop_GivenWeakCandidateEnhancement_ExpectedIncumbentRemainsActiveAfterIteration()
    {
        await using var factory = CreatePromotionFactory();
        using var client = factory.CreateClient();
        var loopHost = factory.Services.GetRequiredService<BonesLearningLoopHost>();

        var sessionId = await WaitForCompletedIterationSessionIdAsync(client, loopHost, TimeSpan.FromMinutes(3));
        var activeStrategyId = await LoadActiveStrategyIdAsync(factory, sessionId);

        Assert.Equal("initial-seat-1-v1", activeStrategyId);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesLearningLoop_GivenStrongCandidateEnhancement_ExpectedCandidateBecomesActiveForNextIteration()
    {
        await using var factory = CreatePromotionFactory(
            enhancementScriptSource: LastLegalMoveScriptSource,
            promotionEvaluationSeed: PromotionEvaluationSeed);
            using var client = factory.CreateClient();
            var loopHost = factory.Services.GetRequiredService<BonesLearningLoopHost>();

            var sessionId = await WaitForCompletedIterationSessionIdAsync(client, loopHost, TimeSpan.FromMinutes(3));
            var activeStrategyId = await LoadActiveStrategyIdAsync(factory, sessionId);

            Assert.Equal("initial-seat-1-v2", activeStrategyId);
    }

    private static BonesHostWebApplicationFactory CreatePromotionFactory(
        int seed = 4242,
        string? enhancementScriptSource = null,
        int? promotionEvaluationSeed = null)
        => new(
            configureConfiguration: settings =>
            {
                settings["BonesHost:ResumeFromBest"] = "false";
                settings["BonesHost:DefaultParameters:Seed"] = seed.ToString();
                settings["BonesHost:DefaultParameters:TargetScore"] = "8";
                settings["BonesHost:DefaultParameters:GameCount"] = "1";
            },
            configureTestServices: services =>
            {
                var promotionOptions = new BonesStrategyPromotionOptions
                {
                    EvaluationMatchCount = 3,
                    MinimumWinRateImprovement = 0.05,
                    MinimumScoreDifferentialImprovement = 0,
                };

                services.RemoveAll<BonesStrategyPromotionOptions>();
                services.AddSingleton(promotionOptions);

                if (enhancementScriptSource is not null)
                {
                    services.RemoveAll<IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>>();
                    services.AddSingleton<IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>>(
                        new ScriptedEnhancementModelProvider(enhancementScriptSource));
                }

                services.RemoveAll<BonesEnhanceStrategyAgent>();
                services.AddSingleton<BonesEnhanceStrategyAgent>(sp => new BonesEnhanceStrategyAgent(
                    sp.GetRequiredService<IArtifactStore>(),
                    sp.GetRequiredService<IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>>(),
                    promotionOptions: promotionOptions,
                    gameBudget: sp.GetRequiredService<BonesGameSimulationBudget>(),
                    strategyLibrary: sp.GetRequiredService<BonesStrategyLibrary>(),
                    forcedEvaluationSeed: promotionEvaluationSeed));
            });

    private static async Task<string> WaitForCompletedIterationSessionIdAsync(
        HttpClient client,
        BonesLearningLoopHost loopHost,
        TimeSpan timeout)
    {
        string? capturedSessionId = null;
        var stopRequested = false;

        await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response =>
            {
                var current = loopHost.CurrentState.CurrentIteration;
                if (!stopRequested
                    && (string.Equals(current.StageName, "Running", StringComparison.Ordinal)
                        || string.Equals(current.StageName, "Complete", StringComparison.Ordinal))
                    && !string.IsNullOrWhiteSpace(current.SessionId))
                {
                    capturedSessionId = current.SessionId;
                    loopHost.RequestStop();
                    stopRequested = true;
                }

                return stopRequested && response.IterationCount >= 1;
            },
            timeout);

        return capturedSessionId
            ?? throw new InvalidOperationException("Completed iteration did not expose a session id.");
    }

    private static async Task<string> LoadActiveStrategyIdAsync(
        BonesHostWebApplicationFactory factory,
        string sessionId)
    {
        var artifactStore = factory.Services.GetRequiredService<IArtifactStore>();
        var knowledgeStore = new BonesPlayerKnowledgeStore(
            artifactStore,
            factory.DataDirectory,
            new SessionId(sessionId));

        var active = await knowledgeStore.LoadActiveStrategy(new BonesPlayerId(1), CancellationToken.None);
        return active.StrategyId.Value;
    }

    private static int FindPromotionSeed()
    {
        var evaluator = new BonesStrategyPromotionEvaluator(new BonesMatchSimulator());
        var learningPlayerId = new BonesPlayerId(1);
        var incumbent = CreateScriptArtifact(learningPlayerId, "initial-seat-1-v1", FirstLegalMoveScriptSource);
        var candidate = CreateScriptArtifact(learningPlayerId, "initial-seat-1-v2", LastLegalMoveScriptSource);
        var opponents = CreateOpponentArtifacts(learningPlayerId, FirstLegalMoveScriptSource);

        for (var seed = 1; seed <= 500; seed++)
        {
            var decision = evaluator.EvaluateAsync(
                new BonesStrategyPromotionEvaluationRequest(
                    learningPlayerId,
                    incumbent,
                    candidate,
                    opponents,
                    new BonesStrategyPromotionOptions
                    {
                        EvaluationMatchCount = 3,
                        MinimumWinRateImprovement = 0.05,
                        MinimumScoreDifferentialImprovement = 0,
                    },
                    seed,
                    TargetScore: 8),
                CancellationToken.None).AsTask().GetAwaiter().GetResult();

            if (decision.Outcome == BonesPromotionDecisionOutcome.Promoted)
                return seed;
        }

        throw new InvalidOperationException("No evaluation seed produced a promoted decision for host integration.");
    }

    private static Dictionary<BonesPlayerId, BonesStrategyArtifact> CreateOpponentArtifacts(
        BonesPlayerId learningPlayerId,
        string scriptSource)
    {
        var opponents = new Dictionary<BonesPlayerId, BonesStrategyArtifact>();
        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            if (seat == learningPlayerId.Seat)
                continue;

            var opponentId = new BonesPlayerId(seat);
            opponents[opponentId] = CreateScriptArtifact(opponentId, $"initial-seat-{seat}-v1", scriptSource);
        }

        return opponents;
    }

    private static BonesStrategyArtifact CreateScriptArtifact(
        BonesPlayerId playerId,
        string strategyId,
        string scriptSource)
        => new(
            new BonesStrategyId(strategyId),
            playerId,
            BonesStrategyKind.Script,
            scriptSource,
            BonesPromotionStatus.Active);

    private sealed class ScriptedEnhancementModelProvider(string scriptSource)
        : IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>
    {
        public ValueTask<ModelProviderResponse<BonesStrategyEnhancementResult>> ExecuteAsync(
            ModelProviderRequest<BonesStrategyEnhancementRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(
                new ModelProviderResponse<BonesStrategyEnhancementResult>(
                    payload: new BonesStrategyEnhancementResult($"```csharp\n{scriptSource}\n```"),
                    providerId: "bones-enhance-test",
                    modelId: request.ModelId,
                    usage: new ModelProviderUsage(64, 32),
                    correlationId: request.CorrelationId));
    }
}