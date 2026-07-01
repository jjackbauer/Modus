using Microsoft.Extensions.Logging;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Delegation;

/// <summary>
/// Orchestrates delegation of uncertain move decisions to a sub-agent.
/// When the main heuristic cannot confidently choose between candidate moves,
/// the delegator invokes a sub-agent for deeper analysis. If the sub-agent
/// times out, the delegator falls back to a provided heuristic move.
/// </summary>
public sealed class BonesSubAgentDelegator
{
    private readonly IBonesSubAgent _subAgent;
    private readonly TimeSpan _timeout;
    private readonly ILogger<BonesSubAgentDelegator>? _logger;

    /// <summary>
    /// Creates a new delegator.
    /// </summary>
    /// <param name="subAgent">The sub-agent to invoke for deeper analysis.</param>
    /// <param name="timeout">Maximum time to wait for sub-agent before falling back.</param>
    /// <param name="logger">Optional logger for diagnosing delegation behavior.</param>
    public BonesSubAgentDelegator(
        IBonesSubAgent subAgent,
        TimeSpan timeout,
        ILogger<BonesSubAgentDelegator>? logger = null)
    {
        _subAgent = subAgent ?? throw new ArgumentNullException(nameof(subAgent));
        _timeout = timeout >= TimeSpan.Zero
            ? timeout
            : throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Timeout must be non-negative.");
        _logger = logger;
    }

    /// <summary>
    /// Delegate a move decision to the sub-agent.
    /// </summary>
    /// <param name="request">The delegation request with game state and candidate moves.</param>
    /// <param name="fallbackMove">The heuristic move to use if the sub-agent fails or times out.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result containing the chosen move (sub-agent or fallback) and metadata.</returns>
    public async Task<BonesSubAgentResult> DelegateAsync(
        BonesDelegationRequest request,
        BonesMove fallbackMove,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(fallbackMove);

        // Honor external cancellation immediately
        cancellationToken.ThrowIfCancellationRequested();

        var startedAt = DateTimeOffset.UtcNow;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeout);

        try
        {
            _logger?.LogDebug(
                "Delegating move decision to sub-agent with {CandidateCount} candidates and {TimeoutMs}ms timeout",
                request.CandidateMoves.Count,
                _timeout.TotalMilliseconds);

            var subAgentResult = await _subAgent.DecideAsync(request, cts.Token);

            var latency = DateTimeOffset.UtcNow - startedAt;

            _logger?.LogDebug(
                "Sub-agent returned move {MoveId} in {LatencyMs}ms: {Reasoning}",
                subAgentResult.ChosenMove.MoveId,
                latency.TotalMilliseconds,
                subAgentResult.Reasoning);

            return new BonesSubAgentResult(
                subAgentResult.ChosenMove,
                subAgentResult.Reasoning,
                latency);
        }
        catch (OperationCanceledException)
        {
            // Timeout or external cancellation during await — fall back to heuristic
            return BuildFallbackResult(request, fallbackMove, startedAt, "sub-agent timed out");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Sub-agent delegation failed, falling back to heuristic move");
            return BuildFallbackResult(request, fallbackMove, startedAt, $"sub-agent failed: {ex.Message}");
        }
    }

    private BonesSubAgentResult BuildFallbackResult(
        BonesDelegationRequest request,
        BonesMove fallbackMove,
        DateTimeOffset startedAt,
        string reason)
    {
        var latency = DateTimeOffset.UtcNow - startedAt;

        _logger?.LogInformation(
            "Falling back to heuristic move {MoveId} after {LatencyMs}ms: {Reason}",
            fallbackMove.MoveId,
            latency.TotalMilliseconds,
            reason);

        return new BonesSubAgentResult(
            fallbackMove,
            $"Fallback to heuristic: {reason}",
            latency);
    }
}
