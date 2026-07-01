using Wip.Abstractions.Capabilities;
using Wip.Bones.Agents.Knowledge;
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

    public BonesCoLearningEnhanceAgent(BonesEnhanceStrategyAgent singleSeatAgent)
    {
        _singleSeatAgent = singleSeatAgent ?? throw new ArgumentNullException(nameof(singleSeatAgent));
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
            }
            catch (Exception)
            {
                // Enhance failed for this seat — continue with remaining seats.
                // The failure will be recorded by the underlying BonesEnhanceStrategyAgent
                // via its own SaveEnhanceFailureArtifactAsync.
            }
        }

        // Return the last completed result as aggregate, or re-throw if all failed.
        if (lastResult is not null)
            return lastResult;

        // All 4 seats failed — throw to let the workflow handle the failure.
        throw new InvalidOperationException(
            "Co-learning enhance failed for all 4 seats. No enhancement results produced.");
    }
}
