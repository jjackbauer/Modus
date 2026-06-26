namespace Wip.Runtime.Runtime;

public sealed record AgentPlan(
    string SessionId,
    string WorkflowId,
    string Task,
    string RepositoryPath,
    string WorktreePath,
    string PolicyId,
    IReadOnlyList<string> ToolCapabilityIds,
    IReadOnlyList<string> ValidatorCapabilityIds,
    IReadOnlyList<string> Steps,
    string Markdown,
    string? ProviderCorrelationId = null);

public sealed record AgentRunStageResult(
    string Stage,
    string StateAfterStage,
    bool AppliedTransition,
    string RequestContractName,
    string ResultContractName,
    string? MappedInputContractName);

public sealed record AgentRunResult(
    string WorkflowId,
    IReadOnlyList<AgentRunStageResult> Stages)
{
    public static AgentRunResult FromWorkflowExecution(WorkflowExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new AgentRunResult(
            result.WorkflowId.Value,
            result.Stages
                .Select(static stage => new AgentRunStageResult(
                    Stage: stage.Descriptor.Stage.ToString(),
                    StateAfterStage: stage.StateAfterStage.ToString(),
                    AppliedTransition: stage.AppliedTransition,
                    RequestContractName: stage.Descriptor.RequestContractName,
                    ResultContractName: stage.Descriptor.ResultContractName,
                    MappedInputContractName: stage.MappedInputContractName))
                .ToArray());
    }
}