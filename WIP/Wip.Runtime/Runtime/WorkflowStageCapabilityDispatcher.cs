using System.Reflection;
using System.Text.Json;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Descriptors;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Permissions;
using Wip.Abstractions.Sessions;
using Wip.Agent.Basic;
using Wip.Builder;
using Wip.Abstractions.Workflows;


namespace Wip.Runtime.Runtime;

public sealed record WorkflowStageDispatchResult(
    CapabilityId ProducerCapabilityId,
    string RequestContractName,
    string ResultContractName,
    object Result,
    ArtifactDescriptor? ExecutionArtifact);

public sealed record WorkflowStageDispatchContext(
    SessionSnapshot Session,
    WipBuilder Builder,
    string CorrelationId,
    PolicyId? PolicyId = null,
    IArtifactStore? ArtifactStore = null);

public sealed class WorkflowStageCapabilityDispatcher
{
    private static readonly JsonSerializerOptions ArtifactJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceProvider _serviceProvider;
    private readonly IReadOnlyDictionary<(CapabilityKind Kind, Type RequestType, Type ResultType), ICapabilityDescriptor> _capabilityIndex;

    public WorkflowStageCapabilityDispatcher(
        IServiceProvider serviceProvider,
        IReadOnlyCollection<ICapabilityDescriptor> capabilityDescriptors)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        ArgumentNullException.ThrowIfNull(capabilityDescriptors);

        _capabilityIndex = capabilityDescriptors
            .GroupBy(descriptor => (descriptor.Kind, descriptor.RequestType, descriptor.ResultType))
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(descriptor => descriptor.CapabilityId.Value, StringComparer.Ordinal).First());
    }

    public static bool IsDispatchableStage(WorkflowStageKind stage)
        => WorkflowStageDispatchScope.IsDispatchable(stage);

    public bool ShouldDispatchStage(WorkflowStageDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (WorkflowStageDispatchScope.IsDispatchable(descriptor.Stage))
            return true;

        return descriptor.Stage == WorkflowStageKind.Validate && CanResolve(descriptor);
    }

    public static CapabilityKind ResolveRequiredCapabilityKind(WorkflowStageKind stage)
        => stage switch
        {
            WorkflowStageKind.Plan => CapabilityKind.Agent,
            WorkflowStageKind.Run => CapabilityKind.Tool,
            WorkflowStageKind.Validate => CapabilityKind.Validator,
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Stage is not dispatchable through capability resolution.")
        };

    public bool CanResolve(WorkflowStageDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (TryResolveDescriptor(descriptor, out _))
            return true;

        return descriptor.Stage == WorkflowStageKind.Plan && _serviceProvider.GetService(typeof(PlanOnlyAgent)) is not null;
    }

    public void EnsureDispatchCapabilitiesRegistered(IReadOnlyList<WorkflowStageDescriptor> stageDescriptors)
    {
        ArgumentNullException.ThrowIfNull(stageDescriptors);

        foreach (var descriptor in stageDescriptors.Where(ShouldDispatchStage))
        {
            if (!CanResolve(descriptor))
            {
                var requiredKind = ResolveRequiredCapabilityKind(descriptor.Stage);
                throw new InvalidOperationException(
                    $"Workflow stage '{descriptor.Stage}' requires a registered {requiredKind} capability for request/result contracts '{descriptor.RequestContractName}'/'{descriptor.ResultContractName}', but no matching capability was found.");
            }
        }
    }

    public ValueTask<WorkflowStageDispatchResult> ExecuteStageAsync(
        WorkflowStageDescriptor descriptor,
        object request,
        WorkflowStageDispatchContext dispatchContext,
        WorkflowStageMappedInputEvidence mappedInputEvidence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(dispatchContext);
        ArgumentNullException.ThrowIfNull(mappedInputEvidence);

        if (!ShouldDispatchStage(descriptor))
        {
            throw new InvalidOperationException(
                $"Workflow stage '{descriptor.Stage}' is not dispatchable during execution-only run.");
        }

        var capabilityContext = new CapabilityContext(
            dispatchContext.Session.SessionId,
            dispatchContext.Session.WorktreePath);

        var executeMethod = typeof(WorkflowStageCapabilityDispatcher)
            .GetMethod(nameof(ExecuteStageCoreAsync), BindingFlags.Instance | BindingFlags.NonPublic)!;

        return (ValueTask<WorkflowStageDispatchResult>)executeMethod
            .MakeGenericMethod(descriptor.RequestType, descriptor.ResultType)
            .Invoke(this, [descriptor, request, dispatchContext, capabilityContext, mappedInputEvidence, cancellationToken])!;
    }

    private async ValueTask<WorkflowStageDispatchResult> ExecuteStageCoreAsync<TRequest, TResult>(
        WorkflowStageDescriptor descriptor,
        object request,
        WorkflowStageDispatchContext dispatchContext,
        CapabilityContext capabilityContext,
        WorkflowStageMappedInputEvidence mappedInputEvidence,
        CancellationToken cancellationToken)
        where TRequest : notnull
        where TResult : notnull
    {
        if (request is not TRequest typedRequest)
        {
            throw new InvalidOperationException(
                $"Workflow stage '{descriptor.Stage}' expected request type '{typeof(TRequest).FullName}' but received '{request.GetType().FullName}'.");
        }

        TResult result;
        CapabilityId producerCapabilityId;

        if (TryResolveDescriptor(descriptor, out var capabilityDescriptor))
        {
            EnsurePermissions(capabilityDescriptor, dispatchContext.Session.SessionId);
            result = await InvokeCapabilityAsync<TRequest, TResult>(
                capabilityDescriptor,
                typedRequest,
                capabilityContext,
                cancellationToken);
            producerCapabilityId = capabilityDescriptor.CapabilityId;
        }
        else if (descriptor.Stage == WorkflowStageKind.Plan
            && _serviceProvider.GetService(typeof(PlanOnlyAgent)) is PlanOnlyAgent planOnlyAgent)
        {
            var policyId = ResolvePolicyId(dispatchContext);
            var agentContext = CreateAgentExecutionContext(dispatchContext.Session, dispatchContext.Builder, policyId);
            await planOnlyAgent.ExecuteAsync(
                new PlanOnlyAgentRequest(agentContext),
                capabilityContext,
                cancellationToken);

            if (typeof(TResult) != typeof(PlanStageResult))
            {
                throw new InvalidOperationException(
                    $"PlanOnlyAgent bridge produced '{typeof(PlanStageResult).FullName}' but stage descriptor requires '{typeof(TResult).FullName}'.");
            }

            result = (TResult)(object)new PlanStageResult(true);
            producerCapabilityId = new CapabilityId("agent.plan-only");
        }
        else
        {
            var requiredKind = ResolveRequiredCapabilityKind(descriptor.Stage);
            throw new InvalidOperationException(
                $"Workflow stage '{descriptor.Stage}' requires a registered {requiredKind} capability for request/result contracts '{descriptor.RequestContractName}'/'{descriptor.ResultContractName}', but no matching capability was found.");
        }

        var executionArtifact = dispatchContext.ArtifactStore is null
            ? throw new InvalidOperationException(
                "Artifact store is required to persist stage execution artifacts.")
            : await PersistStageExecutionArtifactAsync(
                descriptor,
                producerCapabilityId,
                dispatchContext,
                mappedInputEvidence,
                cancellationToken);

        return new WorkflowStageDispatchResult(
            producerCapabilityId,
            descriptor.RequestContractName,
            descriptor.ResultContractName,
            result,
            executionArtifact);
    }

    /// <summary>
    /// WP-1.7: a capability's declared permissions are evaluated before invocation. Built-in
    /// capabilities are allowed; plugin capabilities need a grant, and a denial is a deterministic
    /// PermissionDeniedException (no partial execution, no artifact).
    /// </summary>
    private void EnsurePermissions(ICapabilityDescriptor capabilityDescriptor, SessionId sessionId)
    {
        if (capabilityDescriptor.RequiredPermissions.Count == 0)
            return;

        var evaluator = _serviceProvider.GetService(typeof(IPermissionEvaluator)) as IPermissionEvaluator
            ?? PermissionEvaluator.EnforceAllowBuiltIns;

        foreach (var permission in capabilityDescriptor.RequiredPermissions)
        {
            var decision = evaluator.Evaluate(new PermissionRequest(
                permission.Value,
                capabilityDescriptor.CapabilityId,
                capabilityDescriptor.PluginOrigin,
                sessionId,
                IsBuiltIn: capabilityDescriptor.PluginOrigin is null));

            if (!decision.Allowed)
            {
                throw new PermissionDeniedException(permission.Value, decision.Reason);
            }
        }
    }

    private bool TryResolveDescriptor(WorkflowStageDescriptor descriptor, out ICapabilityDescriptor capabilityDescriptor)
    {
        var requiredKind = ResolveRequiredCapabilityKind(descriptor.Stage);
        return _capabilityIndex.TryGetValue((requiredKind, descriptor.RequestType, descriptor.ResultType), out capabilityDescriptor!);
    }

    private async ValueTask<TResult> InvokeCapabilityAsync<TRequest, TResult>(
        ICapabilityDescriptor capabilityDescriptor,
        TRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
        where TRequest : notnull
        where TResult : notnull
    {
        var service = _serviceProvider.GetService(capabilityDescriptor.CapabilityType)
            ?? throw new InvalidOperationException(
                $"Capability '{capabilityDescriptor.CapabilityId.Value}' of type '{capabilityDescriptor.CapabilityType.FullName}' is not registered in the service provider.");

        if (capabilityDescriptor.Kind == CapabilityKind.Agent && service is IAgent<TRequest, TResult> agent)
            return await agent.ExecuteAsync(request, context, cancellationToken);

        if (capabilityDescriptor.Kind == CapabilityKind.Tool && service is ITool<TRequest, TResult> tool)
            return await tool.ExecuteAsync(request, context, cancellationToken);

        if (capabilityDescriptor.Kind == CapabilityKind.Validator && service is IValidator<TRequest, TResult> validator)
            return await validator.ExecuteAsync(request, context, cancellationToken);

        throw new InvalidOperationException(
            $"Capability '{capabilityDescriptor.CapabilityId.Value}' of type '{capabilityDescriptor.CapabilityType.FullName}' is not resolvable as {capabilityDescriptor.Kind}<{typeof(TRequest).Name}, {typeof(TResult).Name}>.");
    }

    private async ValueTask<ArtifactDescriptor> PersistStageExecutionArtifactAsync(
        WorkflowStageDescriptor descriptor,
        CapabilityId producerCapabilityId,
        WorkflowStageDispatchContext dispatchContext,
        WorkflowStageMappedInputEvidence mappedInputEvidence,
        CancellationToken cancellationToken)
    {
        var artifactStore = dispatchContext.ArtifactStore
            ?? throw new InvalidOperationException("Artifact store is required to persist stage execution artifacts.");

        var mappedSourcePayload = SerializePayloadForArtifact(
            mappedInputEvidence.MappedSourcePayload,
            mappedInputEvidence.MappedSourcePayload.GetType());
        var mappedInputPayload = SerializePayloadForArtifact(
            mappedInputEvidence.MappedInputPayload,
            mappedInputEvidence.MappedInputPayload.GetType());

        var artifactId = new ArtifactId($"stage-execution-{descriptor.Stage.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}");
        var baseFileName = $"stage-execution-{descriptor.Stage.ToString().ToLowerInvariant()}";
        var storageFileName = $"{baseFileName}-{artifactId.Value}.json";
        var storagePath = Path.GetRelativePath(
            dispatchContext.Session.RepositoryPath,
            Path.Combine(dispatchContext.Session.ArtifactDirectory, storageFileName));

        var payload = JsonSerializer.Serialize(new
        {
            stage = descriptor.Stage.ToString(),
            producerCapabilityId = producerCapabilityId.Value,
            requestContractName = descriptor.RequestContractName,
            resultContractName = descriptor.ResultContractName,
            correlationId = dispatchContext.CorrelationId,
            storagePath,
            mappedSourceContractName = mappedInputEvidence.MappedSourceContractName,
            mappedSourcePayload,
            mappedInputContractName = mappedInputEvidence.MappedInputContractName,
            mappedInputPayload
        }, ArtifactJsonOptions);

        return await artifactStore.SaveAsync(
            dispatchContext.Session.SessionId,
            new ArtifactContent(
                artifactId: artifactId,
                kind: ArtifactKind.Json,
                fileName: baseFileName,
                content: payload,
                producerType: producerCapabilityId.Value,
                producerVersion: "1.0.0",
                producedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private static JsonElement SerializePayloadForArtifact(object payload, Type payloadType)
    {
        try
        {
            return JsonSerializer.SerializeToElement(payload, payloadType, ArtifactJsonOptions);
        }
        catch (NotSupportedException)
        {
            return JsonSerializer.SerializeToElement(
                new StageExecutionPayloadReference(
                    payloadType.FullName ?? payloadType.Name,
                    payload.ToString() ?? string.Empty),
                ArtifactJsonOptions);
        }
    }

    private sealed record StageExecutionPayloadReference(string ContractName, string Summary);

    private static PolicyId ResolvePolicyId(WorkflowStageDispatchContext dispatchContext)
    {
        if (dispatchContext.PolicyId is PolicyId explicitPolicy)
            return explicitPolicy;

        var registeredPolicy = dispatchContext.Builder.PolicyRegistrations.FirstOrDefault();
        if (registeredPolicy is null)
        {
            throw new InvalidOperationException(
                "Plan stage dispatch through PlanOnlyAgent requires a registered policy in the active builder.");
        }

        return registeredPolicy.PolicyId;
    }

    private static AgentExecutionContext CreateAgentExecutionContext(
        SessionSnapshot snapshot,
        WipBuilder builder,
        PolicyId policyId)
    {
        var tools = builder.CapabilityDescriptors
            .Where(descriptor => descriptor.Kind == CapabilityKind.Tool)
            .OrderBy(descriptor => descriptor.CapabilityId.Value, StringComparer.Ordinal)
            .ToArray();

        var validators = builder.CapabilityDescriptors
            .Where(descriptor => descriptor.Kind == CapabilityKind.Validator)
            .OrderBy(descriptor => descriptor.CapabilityId.Value, StringComparer.Ordinal)
            .ToArray();

        return new AgentExecutionContext(
            SessionId: snapshot.SessionId,
            WorkflowId: snapshot.WorkflowId,
            Task: string.IsNullOrWhiteSpace(snapshot.TaskDescription) ? "workflow-run" : snapshot.TaskDescription,
            RepositoryPath: snapshot.RepositoryPath,
            WorktreePath: snapshot.WorktreePath,
            Tools: tools,
            Validators: validators,
            PolicyId: policyId);
    }
}