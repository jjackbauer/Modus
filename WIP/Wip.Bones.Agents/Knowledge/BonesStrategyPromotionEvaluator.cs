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
    BonesPromotionEvaluationMetrics Metrics)
{
    public int CandidateWins => Metrics.CandidateWins;
    public int IncumbentWins => Metrics.IncumbentWins;
    public int TotalMatches => Metrics.EvaluationMatchCount;
}

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
    private readonly BonesPlayerKnowledgeStore? _knowledgeStore;

    public BonesStrategyPromotionEvaluator(
        BonesMatchSimulator simulator,
        BonesStrategyScriptHost? scriptHost = null,
        BonesGameSimulationBudget? gameBudget = null,
        IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>? playModelProvider = null,
        BonesAdaptiveEvaluationBudget? adaptiveBudget = null,
        BonesPlayerKnowledgeStore? knowledgeStore = null)
    {
        _simulator = simulator ?? throw new ArgumentNullException(nameof(simulator));
        _playerSlotFactory = new BonesStrategyPlayerSlotFactory(scriptHost, playModelProvider);
        _gameBudget = gameBudget;
        _adaptiveBudget = adaptiveBudget;
        _knowledgeStore = knowledgeStore;
    }

    internal BonesStrategyPromotionEvaluator(
        BonesMatchSimulator simulator,
        BonesStrategyPlayerSlotFactory playerSlotFactory,
        BonesGameSimulationBudget? gameBudget = null,
        BonesAdaptiveEvaluationBudget? adaptiveBudget = null,
        BonesPlayerKnowledgeStore? knowledgeStore = null)
    {
        _simulator = simulator ?? throw new ArgumentNullException(nameof(simulator));
        _playerSlotFactory = playerSlotFactory ?? throw new ArgumentNullException(nameof(playerSlotFactory));
        _gameBudget = gameBudget;
        _adaptiveBudget = adaptiveBudget;
        _knowledgeStore = knowledgeStore;
    }

    public async ValueTask<BonesPromotionDecision> EvaluateAsync(
        BonesStrategyPromotionEvaluationRequest request,
        CancellationToken cancellationToken = default,
        BonesPlayerKnowledgeStore? knowledgeStore = null)
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

        var effectiveStore = knowledgeStore ?? _knowledgeStore;
        var learningPlayerId = request.LearningPlayerId;
        var maxMatchCount = request.Options.EvaluationMatchCount;
        var adaptiveBudget = _adaptiveBudget;

        if (adaptiveBudget is null)
        {
            return await EvaluateFullAsync(request, learningPlayerId, maxMatchCount, effectiveStore, cancellationToken)
                .ConfigureAwait(false);
        }

        using var throttle = new SemaphoreSlim(Math.Max(1, request.Options.MaxConcurrentMatches));

        // Adaptive evaluation: run matches in batches and check for early stop.
        var candidateWins = 0;
        var incumbentWins = 0;
        var candidateScoreTotal = 0;
        var incumbentScoreTotal = 0;
        var matchesCompleted = 0;
        var allResults = new List<BonesMatchResult>(maxMatchCount * 2);
        var scoreDifferences = new List<double>(maxMatchCount);

        while (matchesCompleted < maxMatchCount)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var nextBatch = adaptiveBudget.DetermineMatchCount(matchesCompleted, candidateWins);
            if (nextBatch <= 0)
                break;

            var batchEnd = Math.Min(matchesCompleted + nextBatch, maxMatchCount);
            var batchMatchCount = batchEnd - matchesCompleted;
            var batchGames = batchMatchCount * 2;

            if (_gameBudget is not null && !_gameBudget.TryReserve(batchGames))
            {
                throw new BonesGameSimulationBudgetExhaustedException(
                    $"Game budget exhausted during adaptive evaluation: {_gameBudget.GamesSimulated}/{_gameBudget.MaxGamesPerRun} games used, {_gameBudget.ReservedGames} reserved.");
            }

            BonesMatchResult[] batchResults;
            try
            {
                var matchTasks = new Task<BonesMatchResult>[batchGames];
                for (var i = 0; i < batchMatchCount; i++)
                {
                    var matchIndex = matchesCompleted + i;
                    var candidateSeed = BonesSeedDerivation.Derive(request.BaseSeed, matchIndex, 1);
                    var incumbentSeed = request.Options.UsePairedEvaluationSeeds
                        ? candidateSeed
                        : BonesSeedDerivation.Derive(request.BaseSeed, matchIndex, 2);

                    matchTasks[i * 2] = RunThrottledMatchAsync(
                        new BonesGameId($"promotion-eval-{matchIndex}-c"),
                        candidateSeed,
                        request.Candidate);

                    matchTasks[i * 2 + 1] = RunThrottledMatchAsync(
                        new BonesGameId($"promotion-eval-{matchIndex}-i"),
                        incumbentSeed,
                        request.Incumbent);
                }

                batchResults = await Task.WhenAll(matchTasks).ConfigureAwait(false);
            }
            catch
            {
                _gameBudget?.ReleaseReservation(batchGames);
                throw;
            }

            _gameBudget?.RecordGames(batchGames);
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

                scoreDifferences.Add(candidateScore - incumbentScore);

                // Record evaluation match outcomes to the knowledge store.
                await RecordMatchOutcomesAsync(
                    effectiveStore,
                    candidateResult,
                    incumbentResult,
                    request);
            }

            matchesCompleted = batchEnd;

            var state = adaptiveBudget.GetState(matchesCompleted, candidateWins);
            if (state.Decision == BonesAdaptiveDecision.StopEarlyPromote)
            {
                return BuildDecision(
                    BonesPromotionDecisionOutcome.Promoted,
                    request,
                    matchesCompleted,
                    candidateWins,
                    incumbentWins,
                    candidateScoreTotal,
                    incumbentScoreTotal,
                    scoreDifferences);
            }

            if (state.Decision == BonesAdaptiveDecision.StopEarlyReject)
            {
                return BuildDecision(
                    BonesPromotionDecisionOutcome.Rejected,
                    request,
                    matchesCompleted,
                    candidateWins,
                    incumbentWins,
                    candidateScoreTotal,
                    incumbentScoreTotal,
                    scoreDifferences);
            }
        }

        // Full evaluation complete (or early stop didn't trigger).
        var decision = BuildDecision(
            default,
            request,
            matchesCompleted,
            candidateWins,
            incumbentWins,
            candidateScoreTotal,
            incumbentScoreTotal,
            scoreDifferences);

        // Apply cross-seat promotion criteria when MinimumCrossSeatCount > 1.
        if (decision.Outcome == BonesPromotionDecisionOutcome.Promoted
            && request.Options.MinimumCrossSeatCount > 1)
        {
            return await EvaluateCrossSeatQualificationAsync(
                request,
                effectiveStore,
                decision,
                cancellationToken).ConfigureAwait(false);
        }

        async Task<BonesMatchResult> RunThrottledMatchAsync(
            BonesGameId gameId,
            int seed,
            BonesStrategyArtifact strategy)
        {
            await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await RunEvaluationMatchAsync(request, gameId, seed, strategy, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                throttle.Release();
            }
        }

        return decision;
    }

    /// <summary>
    /// Evaluates whether the candidate meets the cross-seat qualification:
    /// the candidate must achieve at least MinimumWinRateImprovement from
    /// at least MinimumCrossSeatCount distinct seat positions.
    /// </summary>
    private async ValueTask<BonesPromotionDecision> EvaluateCrossSeatQualificationAsync(
        BonesStrategyPromotionEvaluationRequest request,
        BonesPlayerKnowledgeStore? knowledgeStore,
        BonesPromotionDecision primaryDecision,
        CancellationToken cancellationToken)
    {
        var seatsThatPassed = 1; // primary seat already passed
        var crossSeatMatchCount = Math.Max(1, request.Options.EvaluationMatchCount / 4);

        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            var playerId = new BonesPlayerId(seat);
            if (playerId == request.LearningPlayerId)
                continue;

            // Evaluate candidate deployed at this seat vs incumbent at learning seat
            var candidateWins = 0;
            var totalPlayed = 0;

            for (var matchIndex = 0; matchIndex < crossSeatMatchCount; matchIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var seed = BonesSeedDerivation.Derive(request.BaseSeed, seat, matchIndex, 100);
                var gameId = new BonesGameId($"cross-seat-eval-s{seat}-{matchIndex}");

                if (_gameBudget is not null && !_gameBudget.TryReserve(1))
                {
                    throw new BonesGameSimulationBudgetExhaustedException(
                        $"Game budget exhausted during cross-seat evaluation: {_gameBudget.GamesSimulated}/{_gameBudget.MaxGamesPerRun} games used, {_gameBudget.ReservedGames} reserved.");
                }

                BonesMatchResult result;
                try
                {
                    result = await RunCrossSeatMatchAsync(
                        request,
                        gameId,
                        seed,
                        candidateSeat: playerId,
                        cancellationToken).ConfigureAwait(false);
                    _gameBudget?.RecordGames(1);
                }
                catch
                {
                    _gameBudget?.ReleaseReservation(1);
                    throw;
                }

                totalPlayed++;
                if (result.Winner == playerId)
                    candidateWins++;
            }

            var winRate = candidateWins / (double)totalPlayed;
            // Compare against the baseline: at the primary seat, candidateWinRate beat incumbentWinRate.
            // For cross-seat, require candidate from this seat to achieve a win rate above the
            // incumbent's win rate at the primary seat (absolute bar).
            if (winRate >= primaryDecision.Metrics.IncumbentWinRate + request.Options.MinimumWinRateImprovement)
            {
                seatsThatPassed++;
            }

            if (seatsThatPassed >= request.Options.MinimumCrossSeatCount)
                break;
        }

        if (seatsThatPassed >= request.Options.MinimumCrossSeatCount)
            return primaryDecision;

        return new BonesPromotionDecision(
            BonesPromotionDecisionOutcome.Rejected,
            request.Incumbent.StrategyId,
            request.Candidate.StrategyId,
            primaryDecision.Metrics);
    }

    private async Task<BonesMatchResult> RunCrossSeatMatchAsync(
        BonesStrategyPromotionEvaluationRequest request,
        BonesGameId gameId,
        int seed,
        BonesPlayerId candidateSeat,
        CancellationToken cancellationToken)
    {
        var playerSlots = BuildCrossSeatPlayerSlots(request, candidateSeat);
        var config = new BonesMatchConfig(gameId, seed, request.TargetScore, playerSlots);
        return await _simulator.RunMatchAsync(config, cancellationToken).ConfigureAwait(false);
    }

    private ImmutableDictionary<BonesPlayerId, IBonesPlayerSlot> BuildCrossSeatPlayerSlots(
        BonesStrategyPromotionEvaluationRequest request,
        BonesPlayerId candidateSeat)
    {
        var slots = ImmutableDictionary.CreateBuilder<BonesPlayerId, IBonesPlayerSlot>();
        var factoryContext = request.CapabilityContext is null
            ? null
            : new BonesStrategyPlayerSlotFactoryContext(request.CapabilityContext, request.PlayModelId);

        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            var playerId = new BonesPlayerId(seat);
            if (playerId == candidateSeat)
            {
                // Deploy candidate at this cross-seat position
                slots[playerId] = _playerSlotFactory.CreatePlayerSlot(request.Candidate);
            }
            else if (playerId == request.LearningPlayerId)
            {
                // Deploy incumbent at the original learning seat
                slots[playerId] = _playerSlotFactory.CreatePlayerSlot(request.Incumbent);
            }
            else if (request.OpponentActiveStrategies.TryGetValue(playerId, out var opponentStrategy))
            {
                slots[playerId] = _playerSlotFactory.CreatePlayerSlot(opponentStrategy, factoryContext);
            }
            else
            {
                throw new InvalidOperationException(
                    $"No opponent strategy was supplied for seat {playerId.Seat} in cross-seat evaluation.");
            }
        }

        return slots.ToImmutable();
    }

    private async ValueTask<BonesPromotionDecision> EvaluateFullAsync(
        BonesStrategyPromotionEvaluationRequest request,
        BonesPlayerId learningPlayerId,
        int matchCount,
        BonesPlayerKnowledgeStore? knowledgeStore,
        CancellationToken cancellationToken)
    {
        var reservedGames = matchCount * 2;
        if (_gameBudget is not null && !_gameBudget.TryReserve(reservedGames))
        {
            throw new BonesGameSimulationBudgetExhaustedException(
                $"Game budget exhausted before evaluation: {_gameBudget.GamesSimulated}/{_gameBudget.MaxGamesPerRun} games used, {_gameBudget.ReservedGames} reserved.");
        }

        var settled = false;
        using var throttle = new SemaphoreSlim(Math.Max(1, request.Options.MaxConcurrentMatches));
        try
        {
            var matchTasks = new Task<BonesMatchResult>[matchCount * 2];

            for (var matchIndex = 0; matchIndex < matchCount; matchIndex++)
            {
                var candidateSeed = BonesSeedDerivation.Derive(request.BaseSeed, matchIndex, 1);
                var incumbentSeed = request.Options.UsePairedEvaluationSeeds
                    ? candidateSeed
                    : BonesSeedDerivation.Derive(request.BaseSeed, matchIndex, 2);

                matchTasks[matchIndex * 2] = RunThrottledMatchAsync(
                    new BonesGameId($"promotion-eval-{matchIndex}-c"),
                    candidateSeed,
                    request.Candidate);

                matchTasks[matchIndex * 2 + 1] = RunThrottledMatchAsync(
                    new BonesGameId($"promotion-eval-{matchIndex}-i"),
                    incumbentSeed,
                    request.Incumbent);
            }

            var results = await Task.WhenAll(matchTasks).ConfigureAwait(false);

            // The games were actually simulated; settle the reservation before
            // outcome persistence so a later artifact error cannot undercount.
            _gameBudget?.RecordGames(reservedGames);
            settled = true;

            var candidateWins = 0;
            var incumbentWins = 0;
            var candidateScoreTotal = 0;
            var incumbentScoreTotal = 0;
            var scoreDifferences = new double[matchCount];

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

                scoreDifferences[matchIndex] = candidateScore - incumbentScore;

                // Record evaluation match outcomes to the knowledge store.
                await RecordMatchOutcomesAsync(
                    knowledgeStore,
                    candidateResult,
                    incumbentResult,
                    request);
            }

            return BuildDecision(
                default,
                request,
                matchCount,
                candidateWins,
                incumbentWins,
                candidateScoreTotal,
                incumbentScoreTotal,
                scoreDifferences);
        }
        finally
        {
            if (!settled)
                _gameBudget?.ReleaseReservation(reservedGames);
        }

        async Task<BonesMatchResult> RunThrottledMatchAsync(
            BonesGameId gameId,
            int seed,
            BonesStrategyArtifact strategy)
        {
            await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await RunEvaluationMatchAsync(request, gameId, seed, strategy, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                throttle.Release();
            }
        }
    }

    private BonesPromotionDecision BuildDecision(
        BonesPromotionDecisionOutcome? earlyStopOutcome,
        BonesStrategyPromotionEvaluationRequest request,
        int matchCount,
        int candidateWins,
        int incumbentWins,
        int candidateScoreTotal,
        int incumbentScoreTotal,
        IReadOnlyList<double>? scoreDifferences = null)
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

        if (request.Options.GateMode == BonesPromotionGateMode.ConfidenceComposite)
        {
            var confidenceOutcome = DecideWithCompositeConfidence(
                request,
                matchCount,
                candidateWins,
                incumbentWins,
                winRateImprovement,
                scoreDifferentialDelta,
                scoreDifferences);

            return new BonesPromotionDecision(
                confidenceOutcome,
                request.Incumbent.StrategyId,
                request.Candidate.StrategyId,
                metrics);
        }

        // Legacy OR-gate: promote if EITHER metric passes its configurable threshold,
        // but reject immediately if either metric violates its hard floor.
        var belowWinRateFloor = winRateImprovement < request.Options.WinRateHardFloor;
        var belowScoreDeltaFloor = scoreDifferentialDelta < request.Options.ScoreDifferentialHardFloor;

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

    /// <summary>
    /// Composite evidence gate. Combines the paired score-differential z-score and the
    /// win-rate z-score. It requires a minimum evidence count, rejects candidates that
    /// violate either hard floor, and promotes only when the combined evidence exceeds the
    /// configured threshold (or when the paired score lower bound clears zero).
    /// </summary>
    private static BonesPromotionDecisionOutcome DecideWithCompositeConfidence(
        BonesStrategyPromotionEvaluationRequest request,
        int matchCount,
        int candidateWins,
        int incumbentWins,
        double winRateImprovement,
        double scoreDifferentialDelta,
        IReadOnlyList<double>? scoreDifferences)
    {
        const int MinimumEvidenceMatches = 4;
        if (matchCount < MinimumEvidenceMatches || scoreDifferences is null || scoreDifferences.Count < 2)
            return BonesPromotionDecisionOutcome.Rejected;

        if (winRateImprovement < request.Options.WinRateHardFloor
            || scoreDifferentialDelta < request.Options.ScoreDifferentialHardFloor)
        {
            return BonesPromotionDecisionOutcome.Rejected;
        }

        var scoreStandardError = ComputePairedStandardError(scoreDifferences);
        if (double.IsNaN(scoreStandardError) || scoreStandardError <= 1e-9)
            scoreStandardError = 1e-9;

        var scoreZ = scoreDifferentialDelta / scoreStandardError;

        var candidateRate = candidateWins / (double)matchCount;
        var incumbentRate = incumbentWins / (double)matchCount;
        var winStandardError = Math.Sqrt(
            (candidateRate * (1.0 - candidateRate) / matchCount)
            + (incumbentRate * (1.0 - incumbentRate) / matchCount));
        if (winStandardError <= 1e-9)
            winStandardError = 1e-9;

        var winZ = winRateImprovement / winStandardError;
        var compositeZ = scoreZ + winZ;
        var scoreLowerBound90 = scoreDifferentialDelta - (1.2816 * scoreStandardError);

        return compositeZ >= request.Options.ConfidenceZThreshold || scoreLowerBound90 > 0
            ? BonesPromotionDecisionOutcome.Promoted
            : BonesPromotionDecisionOutcome.Rejected;
    }

    private static double ComputePairedStandardError(IReadOnlyList<double> differences)
    {
        if (differences.Count < 2)
            return double.NaN;

        var mean = differences.Average();
        var sumSquares = 0.0;
        foreach (var difference in differences)
        {
            var delta = difference - mean;
            sumSquares += delta * delta;
        }

        var variance = sumSquares / (differences.Count - 1);
        return Math.Sqrt(variance) / Math.Sqrt(differences.Count);
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

    private static async Task RecordMatchOutcomesAsync(
        BonesPlayerKnowledgeStore? knowledgeStore,
        BonesMatchResult candidateResult,
        BonesMatchResult incumbentResult,
        BonesStrategyPromotionEvaluationRequest request)
    {
        if (knowledgeStore is null)
            return;

        var candidateOutcome = new BonesStrategyMatchOutcome(
            candidateResult.Winner,
            candidateResult.CumulativeScores);

        var incumbentOutcome = new BonesStrategyMatchOutcome(
            incumbentResult.Winner,
            incumbentResult.CumulativeScores);

        await knowledgeStore.RecordMatchOutcome(
            request.LearningPlayerId,
            request.Candidate.StrategyId,
            candidateOutcome,
            CancellationToken.None);

        await knowledgeStore.RecordMatchOutcome(
            request.LearningPlayerId,
            request.Incumbent.StrategyId,
            incumbentOutcome,
            CancellationToken.None);
    }
}
