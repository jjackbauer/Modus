using Wip.Abstractions.Sessions;
using Wip.Abstractions.Workflows;

namespace Wip.Runtime.Runtime;

public sealed record WorkflowStageMappedInputEvidence(
    string MappedSourceContractName,
    object MappedSourcePayload,
    string MappedInputContractName,
    object MappedInputPayload);

/// <summary>
/// Applies a declarative workflow plan's stage maps. Unlike the legacy registry path there is no
/// process-global state, no property-name heuristic and no string-constructor fallback: a missing
/// or mismatched map fails with a deterministic error.
/// </summary>
public static class WorkflowPlanMapRuntime
{
    public static WorkflowStageMappedInputEvidence ApplyMappedStageInput(
        WorkflowStageDescriptor stageDescriptor,
        WorkflowPlan plan,
        SessionSnapshot session,
        object? previousStageResult)
    {
        ArgumentNullException.ThrowIfNull(stageDescriptor);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(session);

        if (previousStageResult is null)
        {
            if (plan.InitialRequest is { } initialRequestFactory)
            {
                var initialRequest = initialRequestFactory(session)
                    ?? throw new InvalidOperationException(
                        $"Workflow plan initial request factory returned null for stage '{stageDescriptor.Stage}'.");

                if (!stageDescriptor.RequestType.IsInstanceOfType(initialRequest))
                {
                    throw new InvalidOperationException(
                        $"Workflow plan initial request produced '{initialRequest.GetType().FullName}' but stage " +
                        $"'{stageDescriptor.Stage}' requires '{stageDescriptor.RequestContractName}'.");
                }

                return new WorkflowStageMappedInputEvidence(
                    stageDescriptor.RequestContractName,
                    initialRequest,
                    stageDescriptor.RequestContractName,
                    initialRequest);
            }

            if (stageDescriptor.Stage == WorkflowStageKind.Plan
                && stageDescriptor.RequestType == typeof(PlanStageRequest))
            {
                var planRequest = new PlanStageRequest(session.SessionId.Value);
                return new WorkflowStageMappedInputEvidence(
                    stageDescriptor.RequestContractName,
                    planRequest,
                    stageDescriptor.RequestContractName,
                    planRequest);
            }

            throw new InvalidOperationException(
                $"Workflow stage '{stageDescriptor.Stage}' has no source payload and the plan declares no initial request.");
        }

        var map = plan.ResolveMap(stageDescriptor)
            ?? throw new InvalidOperationException(
                $"Workflow plan has no stage map for stage '{stageDescriptor.Stage}' " +
                $"('{stageDescriptor.RequestContractName}'). Declare a StageMap for every mapped stage.");

        if (!map.SourceType.IsInstanceOfType(previousStageResult))
        {
            throw new InvalidOperationException(
                $"Workflow stage map '{map.SourceContractName}' -> '{map.TargetContractName}' expected " +
                $"source contract '{map.SourceContractName}' but received '{previousStageResult.GetType().FullName}'.");
        }

        if (map.TargetType != stageDescriptor.RequestType)
        {
            throw new InvalidOperationException(
                $"Workflow stage map targets contract '{map.TargetContractName}' but stage " +
                $"'{stageDescriptor.Stage}' requires '{stageDescriptor.RequestContractName}'.");
        }

        var context = new WorkflowContext(
            session.SessionId,
            session.TaskDescription,
            session.WorktreePath,
            session.RepositoryPath);
        var mappedInput = map.Map(previousStageResult, context);
        if (!stageDescriptor.RequestType.IsInstanceOfType(mappedInput))
        {
            throw new InvalidOperationException(
                $"Workflow stage map produced '{mappedInput.GetType().FullName}' but stage " +
                $"'{stageDescriptor.Stage}' requires '{stageDescriptor.RequestContractName}'.");
        }

        return new WorkflowStageMappedInputEvidence(
            map.SourceContractName,
            previousStageResult,
            map.TargetContractName,
            mappedInput);
    }
}