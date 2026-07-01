using System.Collections.Immutable;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Agents.Budget;
using Wip.Bones.Agents.Play;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Knowledge;

public enum BonesPromotionDecisionOutcome
{
    Promoted,
    Rejected,
}

public sealed record BonesPromotionEvaluationMetrics(
    int EvaluationMatchCount,
    int CandidateWins,
    int IncumbentWins,
    double CandidateWinRate,
    double IncumbentWinRate,
    double WinRateImprovement,
    double CandidateAverageScore,
    double IncumbentAverageScore,
    double ScoreDifferentialDelta,
    double StandardError,
    double ConfidenceInterval95);

public sealed record BonesPromotionDecision(
    BonesPromotionDecisionOutcome Outcome,
    BonesStrategyId IncumbentId,
    BonesStrategyId CandidateId,
    BonesPromotionEvaluationMetrics Metrics);

public sealed record BonesStrategyPromotionEvaluationRequest(
    BonesPlayerId LearningPlayerId,
    BonesStrategyArtifact Incumbent,
    BonesStrategyArtifact Candidate,
    IReadOnlyDictionary<BonesPlayerId, BonesStrategyArtifact> OpponentActiveStrategies,
    BonesStrategyPromotionOptions Options,
    int BaseSeed,
    int TargetScore = 8,
    CapabilityContext? CapabilityContext = null,
    string PlayModelId = "bones-play-turn");

public sealed class BonesStrategyPromotionEvaluator
{
    private readonly BonesMatchSimulator _simulator;
    private readonly BonesStrategyPlayerSlotFactory _playerSlotFactory;
    private readonly BonesGameSimulationBudget? _gameBudget;
    private readonly BonesAdaptiveEvaluationBudget? _adaptiveBudget;

    public BonesStrategyPromotionEvaluator(
        BonesMatchSimulator simulator,
        BonesStrategyScriptHost? scriptHost = null,
        BonesGameSimulationBudget? gameBudget = null,
        IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>? playModelProvider = null,
        BonesAdaptiveEvaluationBudget? adaptiveBudget = null)
    {
        _simulator = simulator ?? throw new ArgumentNullException(nameof(simulator));
        _playerSlotFactory = new BonesStrategyPlayerSlotFactory(scriptHost, playModelProvider);
        _gameBudget = gameBudget;
        _adaptiveBudget = adaptiveBudget;
    }

    internal BonesStrategyPromotionEvaluator(
        BonesMatchSimulator simulator,
        BonesStrategyPlayerSlotFactory playerSlotFactory,
        BonesGameSimulationBudget? gameBudget = null,
        BonesAdaptiveEvaluationBudget? adaptiveBudget = null)
    {
        _simulator = simulator ?? throw new ArgumentNullException(nameof(simulator));
        _playerSlotFactory = playerSlotFactory ?? throw new ArgumentNullException(nameof(playerSlotFactory));
        _gameBudget = gameBudget;
        _adaptiveBudget = adaptiveBudget;
    }

    public async ValueTask<BonesPromotionDecision> EvaluateAsync(
        BonesStrategyPromotionEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Options);
        ArgumentNullException.ThrowIfNull(request.OpponentActiveStrategies);

        if (request.Options.EvaluationMatchCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "EvaluationMatchCount must be at least 1.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var learningPlayerId = request.LearningPlayerId;
        var maxMatchCount = request.Options.EvaluationMatchCount;
        var adaptiveBudget = _adaptiveBudget;

        if (adaptiveBudget is null)
        {
            return await EvaluateFullAsync(request, learningPlayerId, maxMatchCount, cancellationToken)
                .ConfigureAwait(false);
        }

        // Adaptive evaluation: run matches in batches and check for early stop.
        var candidateWins = 0;
        var incumbentWins = 0;
        var candidateScoreTotal = 0;
        var incumbentScoreTotal = 0;
        var matchesCompleted = 0;
        var allResults = new List<BonesMatchResult>(maxMatchCount * 2);

        while (matchesCompleted < maxMatchCount)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var nextBatch = adaptiveBudget.DetermineMatchCount(matchesCompleted, candidateWins);
            if (nextBatch <= 0)
                break;

            var batchEnd = Math.Min(matchesCompleted + nextBatch, maxMatchCount);
            var batchMatchCount = batchEnd - matchesCompleted;
            var matchTasks = new Task<BonesMatchResult>[batchMatchCount * 2];

            for (var i = 0; i < batchMatchCount; i++)
            {
                var matchIndex = matchesCompleted + i;
                var candidateSeed = HashCode.Combine(request.BaseSeed, matchIndex, 1);
                var incumbentSeed = HashCode.Combine(request.BaseSeed, matchIndex, 2);

                matchTasks[i * 2] = RunEvaluationMatchAsync(
                    request,
                    new BonesGameId($"promotion-eval-{matchIndex}-c"),
                    candidateSeed,
                    request.Candidate,
                    cancellationToken);

                matchTasks[i * 2 + 1] = RunEvaluationMatchAsync(
                    request,
                    new BonesGameId($"promotion-eval-{matchIndex}-i"),
                    incumbentSeed,
                    request.Incumbent,
                    cancellationToken);
            }

            var batchResults = await Task.WhenAll(matchTasks).ConfigureAwait(false);
            allResults.AddRange(batchResults);

            for (var i = 0; i < batchMatchCount; i++)
            {
                var candidateResult = batchResults[i * 2];
                var incumbentResult = batchResults[i * 2 + 1];

                var candidateScore = candidateResult.CumulativeScores[learningPlayerId];
                var incumbentScore = incumbentResult.CumulativeScores[learningPlayerId];

                candidateScoreTotal += candidateScore;
                incumbentScoreTotal += incumbentScore;

                if (candidateResult.Winner == learningPlayerId)
                    candidateWins++;

                if (incumbentResult.Winner == learningPlayerId)
                    incumbentWins++;
            }

            matchesCompleted = batchEnd;

            var state = adaptiveBudget.GetState(matchesCompleted, candidateWins);
            if (state.Decision == BonesAdaptiveDecision.StopEarlyPromote)
            {
                _gameBudget?.RecordGames(matchesCompleted * 2);

                return BuildDecision(
                    BonesPromotionDecisionOutcome.Promoted,
                    request,
                    matchesCompleted,
                    candidateWins,
                    incumbentWins,
                    candidateScoreTotal,
                    incumbentScoreTotal);
            }

            if (state.Decision == BonesAdaptiveDecision.StopEarlyReject)
            {
                _gameBudget?.RecordGames(matchesCompleted * 2);

                return BuildDecision(
                    BonesPromotionDecisionOutcome.Rejected,
                    request,
                    matchesCompleted,
                    candidateWins,
                    incumbentWins,
                    candidateScoreTotal,
                    incumbentScoreTotal);
            }
        }

        // Full evaluation complete (or early stop didn't trigger).
        _gameBudget?.RecordGames(matchesCompleted * 2);

        return BuildDecision(
            default,
            request,
            matchesCompleted,
            candidateWins,
            incumbentWins,
            candidateScoreTotal,
            incumbentScoreTotal);
    }

    private async ValueTask<BonesPromotionDecision> EvaluateFullAsync(
        BonesStrategyPromotionEvaluationRequest request,
        BonesPlayerId learningPlayerId,
        int matchCount,
        CancellationToken cancellationToken)
    {
        var matchTasks = new Task<BonesMatchResult>[matchCount * 2];

        for (var matchIndex = 0; matchIndex < matchCount; matchIndex++)
        {
            var candidateSeed = HashCode.Combine(request.BaseSeed, matchIndex, 1);
            var incumbentSeed = HashCode.Combine(request.BaseSeed, matchIndex, 2);

            matchTasks[matchIndex * 2] = RunEvaluationMatchAsync(
                request,
                new BonesGameId($"promotion-eval-{matchIndex}-c"),
                candidateSeed,
                request.Candidate,
                cancellationToken);

            matchTasks[matchIndex * 2 + 1] = RunEvaluationMatchAsync(
                request,
                new BonesGameId($"promotion-eval-{matchIndex}-i"),
                incumbentSeed,
                request.Incumbent,
                cancellationToken);
        }

        var results = await Task.WhenAll(matchTasks).ConfigureAwait(false);

        var candidateWins = 0;
        var incumbentWins = 0;
        var candidateScoreTotal = 0;
        var incumbentScoreTotal = 0;

        for (var matchIndex = 0; matchIndex < matchCount; matchIndex++)
        {
            var candidateResult = results[matchIndex * 2];
            var incumbentResult = results[matchIndex * 2 + 1];

            var candidateScore = candidateResult.CumulativeScores[learningPlayerId];
            var incumbentScore = incumbentResult.CumulativeScores[learningPlayerId];

            candidateScoreTotal += candidateScore;
            incumbentScoreTotal += incumbentScore;

            if (candidateResult.Winner == learningPlayerId)
                candidateWins++;

            if (incumbentResult.Winner == learningPlayerId)
                incumbentWins++;
        }

        _gameBudget?.RecordGames(matchCount * 2);

        return BuildDecision(
            default,
            request,
            matchCount,
            candidateWins,
            incumbentWins,
            candidateScoreTotal,
            incumbentScoreTotal);
    }

    private BonesPromotionDecision BuildDecision(
        BonesPromotionDecisionOutcome? earlyStopOutcome,
        BonesStrategyPromotionEvaluationRequest request,
        int matchCount,
        int candidateWins,
        int incumbentWins,
        int candidateScoreTotal,
        int incumbentScoreTotal)
    {
        var candidateWinRate = candidateWins / (double)matchCount;
        var incumbentWinRate = incumbentWins / (double)matchCount;
        var winRateImprovement = candidateWinRate - incumbentWinRate;
        var candidateAverageScore = candidateScoreTotal / (double)matchCount;
        var incumbentAverageScore = incumbentScoreTotal / (double)matchCount;
        var scoreDifferentialDelta = candidateAverageScore - incumbentAverageScore;
        var standardError = Math.Sqrt(candidateWinRate * (1.0 - candidateWinRate) / matchCount);
        var confidenceInterval95 = 1.96 * standardError;

        var metrics = new BonesPromotionEvaluationMetrics(
            matchCount,
            candidateWins,
            incumbentWins,
            candidateWinRate,
            incumbentWinRate,
            winRateImprovement,
            candidateAverageScore,
            incumbentAverageScore,
            scoreDifferentialDelta,
            standardError,
            confidenceInterval95);

        // If early stop already determined outcome, use it.
        if (earlyStopOutcome.HasValue)
        {
            return new BonesPromotionDecision(
                earlyStopOutcome.Value,
                request.Incumbent.StrategyId,
                request.Candidate.StrategyId,
                metrics);
        }

        // T1.1 OR-gate: promote if EITHER metric passes its configurable threshold,
        // but reject immediately if either metric violates its hard floor.
        const double winRateHardFloor = -0.10;
        const double scoreDeltaHardFloor = -1.0;

        var belowWinRateFloor = winRateImprovement < winRateHardFloor;
        var belowScoreDeltaFloor = scoreDifferentialDelta < scoreDeltaHardFloor;

        if (belowWinRateFloor || belowScoreDeltaFloor)
        {
            return new BonesPromotionDecision(
                BonesPromotionDecisionOutcome.Rejected,
                request.Incumbent.StrategyId,
                request.Candidate.StrategyId,
                metrics);
        }

        var passesWinRate = winRateImprovement >= request.Options.MinimumWinRateImprovement;
        var passesScoreDelta = scoreDifferentialDelta >= request.Options.MinimumScoreDifferentialImprovement;
        var outcome = passesWinRate || passesScoreDelta
            ? BonesPromotionDecisionOutcome.Promoted
            : BonesPromotionDecisionOutcome.Rejected;

        return new BonesPromotionDecision(
            outcome,
            request.Incumbent.StrategyId,
            request.Candidate.StrategyId,
            metrics);
    }

    private BonesMatchResult RunEvaluationMatch(
        BonesStrategyPromotionEvaluationRequest request,
        BonesGameId gameId,
        int seed,
        BonesStrategyArtifact learningPlayerStrategy)
    {
        var playerSlots = BuildPlayerSlots(request, learningPlayerStrategy);
        var config = new BonesMatchConfig(gameId, seed, request.TargetScore, playerSlots);
        return _simulator.RunMatch(config);
    }

    private async Task<BonesMatchResult> RunEvaluationMatchAsync(
        BonesStrategyPromotionEvaluationRequest request,
        BonesGameId gameId,
        int seed,
        BonesStrategyArtifact learningPlayerStrategy,
        CancellationToken cancellationToken)
    {
        var playerSlots = BuildPlayerSlots(request, learningPlayerStrategy);
        var config = new BonesMatchConfig(gameId, seed, request.TargetScore, playerSlots);
        return await _simulator.RunMatchAsync(config, cancellationToken).ConfigureAwait(false);
    }

    private ImmutableDictionary<BonesPlayerId, IBonesPlayerSlot> BuildPlayerSlots(
        BonesStrategyPromotionEvaluationRequest request,
        BonesStrategyArtifact learningPlayerStrategy)
    {
        var slots = ImmutableDictionary.CreateBuilder<BonesPlayerId, IBonesPlayerSlot>();
        var factoryContext = request.CapabilityContext is null
            ? null
            : new BonesStrategyPlayerSlotFactoryContext(request.CapabilityContext, request.PlayModelId);

        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            var playerId = new BonesPlayerId(seat);
            if (playerId == request.LearningPlayerId)
            {
                slots[playerId] = _playerSlotFactory.CreatePlayerSlot(learningPlayerStrategy);
                continue;
            }

            if (!request.OpponentActiveStrategies.TryGetValue(playerId, out var opponentStrategy))
            {
                throw new InvalidOperationException(
                    $"No active opponent strategy was supplied for seat {playerId.Seat}.");
            }

            slots[playerId] = _playerSlotFactory.CreatePlayerSlot(opponentStrategy, factoryContext);
        }

        return slots.ToImmutable();
    }

    public BonesStrategyPlayerSlotDispatchKind ResolveOpponentDispatchKind(BonesStrategyArtifact strategy)
        => _playerSlotFactory.ResolveDispatchKind(strategy);
}
