using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Sessions;
using Wip.Abstractions.Workflows;

namespace Wip.Runtime.Runtime;

/// <summary>
/// Default linear executor: dispatches each stage in plan order, advances the session state under
/// the orchestrator's authority, records per-stage evidence and publishes the terminal run event.
/// </summary>
public sealed class LinearWorkflowExecutor : IWorkflowExecutor
{
    public async ValueTask<WorkflowExecutionResult> ExecuteAsync(
        WorkflowExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var dispatcher = new WorkflowStageCapabilityDispatcher(
            context.Services,
            context.Builder.CapabilityDescriptors);
        dispatcher.EnsureDispatchCapabilitiesRegistered(context.Compilation.StageDescriptors);

        var current = context.Session;
        var dispatchContext = new WorkflowStageDispatchContext(
            Session: current,
            Builder: context.Builder,
            CorrelationId: context.CorrelationId,
            ArtifactStore: context.ArtifactStore);

        var stageExecutions = new List<WorkflowStageExecution>(context.Compilation.StageDescriptors.Count);
        var stageArtifactReferences = new List<SessionEventArtifactReference>();
        object? previousStageResult = null;

        foreach (var stageDescriptor in context.Compilation.StageDescriptors)
        {
            WorkflowStageDispatchResult? dispatchResult = null;

            if (dispatcher.ShouldDispatchStage(stageDescriptor))
            {
                if (context.ArtifactStore is null)
                {
                    throw new InvalidOperationException(
                        $"Workflow stage '{stageDescriptor.Stage}' requires an artifact store to persist stage execution artifacts during run.");
                }

                var mappedInputEvidence = context.MapStageInput(stageDescriptor, current, previousStageResult);

                dispatchResult = await dispatcher.ExecuteStageAsync(
                    stageDescriptor,
                    mappedInputEvidence.MappedInputPayload,
                    dispatchContext,
                    mappedInputEvidence,
                    cancellationToken);

                previousStageResult = dispatchResult.Result;

                if (dispatchResult.ExecutionArtifact is ArtifactDescriptor executionArtifact)
                {
                    stageArtifactReferences.Add(
                        context.Authority.CreateStageExecutionReference(executionArtifact));
                }
            }

            var advancementTarget = WorkflowStageStateMapper.ResolveRunAdvancementTarget(stageDescriptor.Stage);
            var appliedTransition = false;

            if (advancementTarget is SessionState targetState
                && current.State != targetState
                && !context.Authority.IsRunBackwardAdvancement(current.State, targetState))
            {
                current = await context.Authority.AdvanceAsync(current, targetState, cancellationToken);
                appliedTransition = true;
            }

            stageExecutions.Add(new WorkflowStageExecution(stageDescriptor, current.State, appliedTransition)
            {
                MappedInputContractName = context.Compilation.ResolveMappedInputContractName(stageDescriptor),
                ProducerCapabilityId = dispatchResult?.ProducerCapabilityId,
                CorrelationId = dispatchResult is null ? null : context.CorrelationId,
                ExecutionArtifact = dispatchResult?.ExecutionArtifact
            });
        }

        await context.Authority.PublishWorkflowExecutedAsync(
            current,
            context.WorkflowId,
            context.CorrelationId,
            stageArtifactReferences,
            cancellationToken);

        return new WorkflowExecutionResult(context.WorkflowId, stageExecutions);
    }
}