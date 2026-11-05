using System.Reflection;
using System.Text.Json;
using Wip.Abstractions.Sessions;
using Wip.Abstractions.Workflows;


namespace Wip.Runtime.Runtime;

public sealed record WorkflowStageMappedInputEvidence(
    string MappedSourceContractName,
    object MappedSourcePayload,
    string MappedInputContractName,
    object MappedInputPayload);

public static class WorkflowStageMapAdapterRuntime
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    public static WorkflowStageMapAdapterDescriptor? ResolveMapAdapter(
        LinearWorkflowStageCompilation compilation,
        WorkflowStageDescriptor stageDescriptor)
        => compilation.MapAdapters.FirstOrDefault(adapter => adapter.TargetType == stageDescriptor.RequestType);

    public static WorkflowStageMappedInputEvidence ApplyMappedStageInput(
        WorkflowStageDescriptor stageDescriptor,
        LinearWorkflowStageCompilation compilation,
        SessionSnapshot session,
        object? previousStageResult)
    {
        ArgumentNullException.ThrowIfNull(stageDescriptor);
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(session);

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

        var mapAdapter = ResolveMapAdapter(compilation, stageDescriptor);
        if (mapAdapter is null)
        {
            var fallbackRequest = CreateStageRequestInstance(stageDescriptor.RequestType, session);
            return new WorkflowStageMappedInputEvidence(
                stageDescriptor.RequestContractName,
                fallbackRequest,
                stageDescriptor.RequestContractName,
                fallbackRequest);
        }

        if (previousStageResult is null)
        {
            throw new InvalidOperationException(
                $"Workflow stage '{stageDescriptor.Stage}' requires mapped input from stage '{mapAdapter.FromStage}' but no source payload is available.");
        }

        if (!mapAdapter.SourceType.IsInstanceOfType(previousStageResult))
        {
            throw new InvalidOperationException(
                $"Workflow stage map adapter '{mapAdapter.FromStage}' -> '{mapAdapter.ToStage}' expected source contract '{mapAdapter.SourceContractName}' but received '{previousStageResult.GetType().FullName}'.");
        }

        if (mapAdapter.TargetType != stageDescriptor.RequestType)
        {
            throw new InvalidOperationException(
                $"Workflow stage map adapter '{mapAdapter.FromStage}' -> '{mapAdapter.ToStage}' targets contract '{mapAdapter.TargetContractName}' but stage descriptor requires '{stageDescriptor.RequestContractName}'.");
        }

        var mappedInput = MapPayload(mapAdapter, previousStageResult, session);
        if (!stageDescriptor.RequestType.IsInstanceOfType(mappedInput))
        {
            throw new InvalidOperationException(
                $"Workflow stage map adapter '{mapAdapter.FromStage}' -> '{mapAdapter.ToStage}' produced '{mappedInput.GetType().FullName}' but stage descriptor requires '{stageDescriptor.RequestContractName}'.");
        }

        return new WorkflowStageMappedInputEvidence(
            mapAdapter.SourceContractName,
            previousStageResult,
            mapAdapter.TargetContractName,
            mappedInput);
    }

    public static string SerializeMappedPayload(object payload)
        => JsonSerializer.Serialize(payload, payload.GetType(), PayloadJsonOptions);

    private static object MapPayload(
        WorkflowStageMapAdapterDescriptor mapAdapter,
        object sourcePayload,
        SessionSnapshot session)
    {
        if (mapAdapter.SourceType == mapAdapter.TargetType)
            return sourcePayload;

        if (mapAdapter.TargetType == typeof(ValidateStageRequest))
            return new ValidateStageRequest(session.SessionId.Value);

        if (mapAdapter.TargetType == typeof(ReviewStageRequest))
            return new ReviewStageRequest(session.SessionId.Value);

        if (mapAdapter.TargetType == typeof(RequireApprovalStageRequest))
            return new RequireApprovalStageRequest(session.SessionId.Value);

        if (mapAdapter.TargetType == typeof(MergeStageRequest))
            return new MergeStageRequest(session.SessionId.Value);

        var stringConstructor = mapAdapter.TargetType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(string)],
            modifiers: null);
        if (stringConstructor is not null)
        {
            var mappedValue = ResolveStringMappingValue(mapAdapter, sourcePayload, session);
            return stringConstructor.Invoke([mappedValue]);
        }

        throw new InvalidOperationException(
            $"No runtime map adapter binding exists for '{mapAdapter.SourceContractName}' -> '{mapAdapter.TargetContractName}'.");
    }

    private static string ResolveStringMappingValue(
        WorkflowStageMapAdapterDescriptor mapAdapter,
        object sourcePayload,
        SessionSnapshot session)
    {
        if (TryReadStringProperty(sourcePayload, "Outcome", out var outcome))
            return outcome;

        if (TryReadStringProperty(sourcePayload, "Summary", out var summary))
            return summary;

        if (TryReadStringProperty(sourcePayload, "Title", out var title))
            return title;

        if (TryReadStringProperty(sourcePayload, "Task", out var task))
            return task;

        if (!string.IsNullOrWhiteSpace(session.TaskDescription))
            return session.TaskDescription;

        return session.SessionId.Value;
    }

    private static bool TryReadStringProperty(object sourcePayload, string propertyName, out string value)
    {
        var property = sourcePayload.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (property?.PropertyType == typeof(string) && property.GetValue(sourcePayload) is string propertyValue)
        {
            value = propertyValue;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static object CreateStageRequestInstance(Type requestType, SessionSnapshot session)
    {
        var task = string.IsNullOrWhiteSpace(session.TaskDescription) ? "workflow-run" : session.TaskDescription;

        if (requestType == typeof(PlanStageRequest))
            return new PlanStageRequest(session.SessionId.Value);

        if (requestType == typeof(ValidateStageRequest))
            return new ValidateStageRequest(session.SessionId.Value);

        if (requestType == typeof(ReviewStageRequest))
            return new ReviewStageRequest(session.SessionId.Value);

        if (requestType == typeof(RequireApprovalStageRequest))
            return new RequireApprovalStageRequest(session.SessionId.Value);

        if (requestType == typeof(MergeStageRequest))
            return new MergeStageRequest(session.SessionId.Value);

        var stringConstructor = requestType.GetConstructor([typeof(string)]);
        if (stringConstructor is not null)
            return stringConstructor.Invoke([task]);

        throw new InvalidOperationException(
            $"Unable to construct stage request '{requestType.FullName}' for workflow stage dispatch.");
    }
}