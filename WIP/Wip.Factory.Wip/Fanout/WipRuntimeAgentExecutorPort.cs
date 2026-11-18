using Wip.Abstractions.Agents;
using Wip.Builder;
using Wip.Runtime.Runtime;

namespace Wip.Factory.Wip.Fanout;

/// <summary>
/// The production execution port. It delegates to <see cref="WipRuntimeOrchestrator.RunAgentAsync"/>,
/// which is the only orchestrator entry point that does not take the process-wide authority gate, so
/// factory dispatch is not serialized behind governance operations (F-E.3).
/// </summary>
public sealed class WipRuntimeAgentExecutorPort : IFactoryAgentExecutionPort
{
    private readonly WipRuntimeOrchestrator _orchestrator;

    public WipRuntimeAgentExecutorPort(WipRuntimeOrchestrator orchestrator)
        => _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));

    public ValueTask<AgentExecutionResult> ExecuteAsync(
        AgentExecutionRequest request,
        WipBuilder builder,
        IServiceProvider scopedServices,
        CancellationToken cancellationToken = default)
        => _orchestrator.RunAgentAsync(request, builder, scopedServices, cancellationToken);
}
