using Wip.Abstractions.Agents;
using Wip.Abstractions.Capabilities;

namespace Wip.Bones.Agents.Runtime;

/// <summary>
/// Adapts a Bones domain agent contract (<see cref="IAgent{TRequest,TResult}"/>) onto the shared
/// runtime agent loop (<see cref="IAgentExecutor"/>). The adapter itself owns no loop: a caller
/// supplies the domain-to-runtime request factory and the runtime-to-domain result mapper, and every
/// non-success execution result is surfaced as a <see cref="BonesAgentExecutionException"/>.
/// </summary>
public sealed class BonesAgentAdapter<TRequest, TResult> : IAgent<TRequest, TResult>
    where TRequest : notnull
    where TResult : notnull
{
    private readonly IAgentExecutor _executor;
    private readonly Func<TRequest, CapabilityContext, AgentExecutionRequest> _createRequest;
    private readonly Func<TRequest, CapabilityContext, AgentExecutionResult, CancellationToken, ValueTask<TResult>> _mapResult;

    public BonesAgentAdapter(
        IAgentExecutor executor,
        Func<TRequest, CapabilityContext, AgentExecutionRequest> createRequest,
        Func<TRequest, CapabilityContext, AgentExecutionResult, CancellationToken, ValueTask<TResult>> mapResult)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _createRequest = createRequest ?? throw new ArgumentNullException(nameof(createRequest));
        _mapResult = mapResult ?? throw new ArgumentNullException(nameof(mapResult));
    }

    public async ValueTask<TResult> ExecuteAsync(
        TRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var executionRequest = _createRequest(request, context);
        var executionResult = await _executor.ExecuteAsync(executionRequest, context, cancellationToken);

        if (executionResult.Succeeded)
        {
            return await _mapResult(request, context, executionResult, cancellationToken);
        }

        throw new BonesAgentExecutionException(
            executionResult.Status,
            executionResult.Steps,
            executionResult.Error);
    }
}