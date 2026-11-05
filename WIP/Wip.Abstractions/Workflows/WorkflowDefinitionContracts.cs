using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;

namespace Wip.Abstractions.Workflows;

public enum WorkflowStageKind
{
    Plan = 1,
    Run = 2,
    Validate = 3,
    Review = 4,
    RequireApproval = 5,
    Merge = 6
}

public sealed record WorkflowStageDescriptor(
    WorkflowStageKind Stage,
    Type RequestType,
    Type ResultType,
    string RequestContractName,
    string ResultContractName)
{
    public static WorkflowStageDescriptor Create<TRequest, TResult>(WorkflowStageKind stage)
        where TRequest : notnull
        where TResult : notnull
        => new(
            stage,
            typeof(TRequest),
            typeof(TResult),
            ResolveContractName(typeof(TRequest)),
            ResolveContractName(typeof(TResult)));

    public static string ResolveContractName(Type contractType)
    {
        ArgumentNullException.ThrowIfNull(contractType);
        return contractType.FullName ?? contractType.Name;
    }
}

public sealed record WorkflowStageExecution(
    WorkflowStageDescriptor Descriptor,
    SessionState StateAfterStage,
    bool AppliedTransition)
{
    public string? MappedInputContractName { get; init; }

    public CapabilityId? ProducerCapabilityId { get; init; }

    public string? CorrelationId { get; init; }

    public ArtifactDescriptor? ExecutionArtifact { get; init; }
}

public sealed record WorkflowExecutionResult(
    WorkflowId WorkflowId,
    IReadOnlyList<WorkflowStageExecution> Stages);

/// <summary>
/// Plan-scoped stage maps. A map owns the conversion between two stage contracts; there is no
/// process-global registry and no reflection heuristic.
/// </summary>
public abstract class StageMap
{
    public abstract WorkflowStageKind FromStage { get; }

    public abstract WorkflowStageKind ToStage { get; }

    public abstract Type SourceType { get; }

    public abstract Type TargetType { get; }

    public abstract string SourceContractName { get; }

    public abstract string TargetContractName { get; }

    public abstract object Map(object source, WorkflowContext context);
}

public sealed class StageMap<TSource, TTarget> : StageMap
    where TSource : notnull
    where TTarget : notnull
{
    private readonly Func<TSource, WorkflowContext, TTarget> _map;

    public StageMap(
        WorkflowStageKind fromStage,
        WorkflowStageKind toStage,
        Func<TSource, WorkflowContext, TTarget> map)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
        FromStage = fromStage;
        ToStage = toStage;
    }

    public override WorkflowStageKind FromStage { get; }

    public override WorkflowStageKind ToStage { get; }

    public override Type SourceType => typeof(TSource);

    public override Type TargetType => typeof(TTarget);

    public override string SourceContractName => WorkflowStageDescriptor.ResolveContractName(typeof(TSource));

    public override string TargetContractName => WorkflowStageDescriptor.ResolveContractName(typeof(TTarget));

    public override object Map(object source, WorkflowContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        return _map((TSource)source, context);
    }
}

/// <summary>
/// Declarative workflow: a typed plan of stages plus the maps that carry payloads between them.
/// The runtime compiles the plan and executes the stages; <see cref="IWorkflow{TRequest,TResult}"/>
/// remains only as the legacy adapter.
/// </summary>
public interface IWorkflowDefinition
{
    WorkflowId WorkflowId { get; }

    WorkflowPlan Compile();
}

public sealed class WorkflowPlan
{
    public WorkflowPlan(
        IReadOnlyList<WorkflowStageDescriptor> stages,
        IReadOnlyList<StageMap>? maps = null,
        Func<SessionSnapshot, object?>? initialRequest = null)
    {
        ArgumentNullException.ThrowIfNull(stages);
        if (stages.Count == 0)
            throw new ArgumentException("A workflow plan must contain at least one stage.", nameof(stages));

        Stages = stages;
        Maps = maps ?? Array.Empty<StageMap>();
        InitialRequest = initialRequest;
    }

    public IReadOnlyList<WorkflowStageDescriptor> Stages { get; }

    public IReadOnlyList<StageMap> Maps { get; }

    /// <summary>
    /// Builds the request for a plan whose first stage is not the governance Plan stage (for
    /// example an Observe stage). Replaces the old process-wide initial-request factory.
    /// </summary>
    public Func<SessionSnapshot, object?>? InitialRequest { get; }

    public StageMap? ResolveMap(WorkflowStageDescriptor target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return Maps.FirstOrDefault(map => map.TargetType == target.RequestType);
    }
}