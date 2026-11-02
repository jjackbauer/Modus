using Microsoft.Extensions.Logging;
using Wip.Abstractions.Capabilities;
using Wip.Bones.Agents.Budget;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Enhance;

/// <summary>
/// Co-learning Enhance agent that runs Enhance for all 4 seats independently when
/// AllSeatsLearning=true, or delegates to single-seat Enhance for backward compatibility.
/// Each seat evaluates the match from its own perspective.
/// Partial failures do not abort the iteration for other seats.
/// </summary>
public sealed class BonesCoLearningEnhanceAgent : IAgent<BonesEnhanceStrategyRequest, BonesEnhanceStrategyResult>
{
    private readonly BonesEnhanceStrategyAgent _singleSeatAgent;
    private readonly StrategyPool? _strategyPool;
    private readonly ILogger<BonesCoLearningEnhanceAgent>? _logger;

    public BonesCoLearningEnhanceAgent(
        BonesEnhanceStrategyAgent singleSeatAgent,
        StrategyPool? strategyPool = null,
        ILogger<BonesCoLearningEnhanceAgent>? logger = null)
    {
        _singleSeatAgent = singleSeatAgent ?? throw new ArgumentNullException(nameof(singleSeatAgent));
        _strategyPool = strategyPool;
        _logger = logger;
    }

    public async ValueTask<BonesEnhanceStrategyResult> ExecuteAsync(
        BonesEnhanceStrategyRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.AllSeatsLearning)
            return await _singleSeatAgent.ExecuteAsync(request, context, cancellationToken);

        // Co-learning mode: run Enhance for each seat independently.
        BonesEnhanceStrategyResult? lastResult = null;
        var failedSeats = 0;

        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var playerId = new BonesPlayerId(seat);

            try
            {
                var seatRequest = new BonesEnhanceStrategyRequest(
                    playerId,
                    request.RepositoryPath,
                    request.MatchGameId,
                    request.ModelId,
                    request.EvaluationSeed,
                    request.EvaluationTargetScore);

                var seatResult = await _singleSeatAgent.ExecuteAsync(
                    seatRequest,
                    context,
                    cancellationToken);
                lastResult = seatResult;

                // Add enhanced strategy to the population pool.
                // Prefer the DI-injected pool; fall back to the static singleton for backward compatibility.
                var pool = _strategyPool ?? StrategyPool.Instance;
                if (pool is not null)
                {
                    try
                    {
                        pool.Add(new StrategyEntry(
                            seatResult.StrategyId,
                            seat,
                            BonesStrategyKind.Script,
                            seatResult.StrategyDocument.Markdown,
                            0, 0, 0, 0,
                            1500.0));
                    }
                    catch (ArgumentException)
                    {
                        // Duplicate strategy ID — already in pool from a previous session
                    }
                }

                _logger?.LogInformation(
                    "Co-learning enhance succeeded for seat {Seat} in session {SessionId}. Pool size: {PoolSize}",
                    seat,
                    context.SessionId.Value,
                    pool?.Count ?? 0);
            }
            catch (BonesGameSimulationBudgetExhaustedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failedSeats++;
                _logger?.LogError(
                    ex,
                    "Co-learning enhance failed for seat {Seat} in session {SessionId}.",
                    seat,
                    context.SessionId.Value);
            }
        }

        if (lastResult is not null)
        {
            if (failedSeats > 0)
            {
                _logger?.LogWarning(
                    "Co-learning enhance completed with {FailedSeats} failed seat(s) out of 4 in session {SessionId}.",
                    failedSeats,
                    context.SessionId.Value);
            }
            return lastResult;
        }

        throw new InvalidOperationException(
            $"Co-learning enhance failed for all 4 seats. No enhancement results produced. Session: {context.SessionId.Value}");
    }
}
