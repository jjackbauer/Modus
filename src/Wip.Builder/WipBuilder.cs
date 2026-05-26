using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Descriptors;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Policies;
using Wip.Abstractions.Workflows;

namespace Wip.Builder;

public sealed record PolicyRegistration(PolicyId PolicyId, Type PolicyType, Type RequestType);

public sealed record WorkflowRegistration(
    WorkflowId WorkflowId,
    Type WorkflowType,
    Type RequestType,
    Type ResultType,
    IWorkflowDescriptor Descriptor);

public sealed class WipBuilder
{
    private readonly List<ICapabilityDescriptor> _capabilityDescriptors = [];
    private readonly List<PolicyRegistration> _policyRegistrations = [];
    private readonly List<WorkflowRegistration> _workflowRegistrations = [];
    private bool _allowCapabilityReplacement;

    public WipBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    public IServiceCollection Services { get; }

    public IReadOnlyList<ICapabilityDescriptor> CapabilityDescriptors => _capabilityDescriptors;

    public IReadOnlyList<PolicyRegistration> PolicyRegistrations => _policyRegistrations;

    public IReadOnlyList<WorkflowRegistration> WorkflowRegistrations => _workflowRegistrations;

    public WipBuilder EnableCapabilityReplacement(bool enabled = true)
    {
        _allowCapabilityReplacement = enabled;
        return this;
    }

    public WipBuilder AddAgent<TAgent, TRequest, TResult>(CapabilityId capabilityId, string displayName)
        where TAgent : class, IAgent<TRequest, TResult>
        where TRequest : notnull
        where TResult : notnull
    {
        return AddCapability<TAgent, TRequest, TResult>(capabilityId, displayName, CapabilityKind.Agent);
    }

    public WipBuilder AddAgent<TAgent>(CapabilityId capabilityId, string displayName)
        where TAgent : class
    {
        return AddInferredCapability<TAgent>(capabilityId, displayName, CapabilityKind.Agent, typeof(IAgent<,>));
    }

    public WipBuilder AddTool<TTool, TRequest, TResult>(CapabilityId capabilityId, string displayName)
        where TTool : class, ITool<TRequest, TResult>
        where TRequest : notnull
        where TResult : notnull
    {
        return AddCapability<TTool, TRequest, TResult>(capabilityId, displayName, CapabilityKind.Tool);
    }

    public WipBuilder AddTool<TTool>(CapabilityId capabilityId, string displayName)
        where TTool : class
    {
        return AddInferredCapability<TTool>(capabilityId, displayName, CapabilityKind.Tool, typeof(ITool<,>));
    }

    public WipBuilder AddValidator<TValidator, TRequest, TResult>(CapabilityId capabilityId, string displayName)
        where TValidator : class, IValidator<TRequest, TResult>
        where TRequest : notnull
        where TResult : notnull
    {
        return AddCapability<TValidator, TRequest, TResult>(capabilityId, displayName, CapabilityKind.Validator);
    }

    public WipBuilder AddValidator<TValidator>(CapabilityId capabilityId, string displayName)
        where TValidator : class
    {
        return AddInferredCapability<TValidator>(capabilityId, displayName, CapabilityKind.Validator, typeof(IValidator<,>));
    }

    public WipBuilder AddPolicy<TPolicy, TRequest>(PolicyId policyId)
        where TPolicy : class, IPolicy<TRequest>
        where TRequest : notnull
    {
        Services.AddSingleton<TPolicy>();
        _policyRegistrations.Add(new PolicyRegistration(policyId, typeof(TPolicy), typeof(TRequest)));
        return this;
    }

    public WipBuilder AddWorkflow<TWorkflow, TRequest, TResult>(WorkflowId workflowId, string displayName)
        where TWorkflow : class, IWorkflow<TRequest, TResult>
        where TRequest : notnull
        where TResult : notnull
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(displayName));

        Services.AddSingleton<TWorkflow>();

        var descriptor = new WorkflowDescriptor<TRequest, TResult>(workflowId, displayName);
        _workflowRegistrations.Add(new WorkflowRegistration(
            workflowId,
            typeof(TWorkflow),
            typeof(TRequest),
            typeof(TResult),
            descriptor));

        return this;
    }

    private WipBuilder AddCapability<TCapability, TRequest, TResult>(
        CapabilityId capabilityId,
        string displayName,
        CapabilityKind kind)
        where TCapability : class, ICapability<TRequest, TResult>
        where TRequest : notnull
        where TResult : notnull
    {
        return AddCapability(typeof(TCapability), typeof(TRequest), typeof(TResult), capabilityId, displayName, kind);
    }

    private WipBuilder AddInferredCapability<TCapability>(
        CapabilityId capabilityId,
        string displayName,
        CapabilityKind kind,
        Type capabilityInterfaceType)
        where TCapability : class
    {
        var signature = ResolveCapabilitySignature(typeof(TCapability), capabilityInterfaceType);
        return AddCapability(typeof(TCapability), signature.RequestType, signature.ResultType, capabilityId, displayName, kind);
    }

    private WipBuilder AddCapability(
        Type capabilityType,
        Type requestType,
        Type resultType,
        CapabilityId capabilityId,
        string displayName,
        CapabilityKind kind)
    {
        var existingIndex = _capabilityDescriptors.FindIndex(existing => existing.CapabilityId.Equals(capabilityId));
        if (existingIndex >= 0)
        {
            if (!_allowCapabilityReplacement)
            {
                throw new InvalidOperationException(
                    $"Duplicate capability ID '{capabilityId.Value}' is not allowed unless replacement mode is enabled.");
            }

            var existingDescriptor = _capabilityDescriptors[existingIndex];
            RemoveCapabilityServiceRegistrations(existingDescriptor.CapabilityType);
            _capabilityDescriptors.RemoveAt(existingIndex);
        }

        Services.AddSingleton(capabilityType);

        var descriptorType = typeof(CapabilityDescriptor<,>).MakeGenericType(requestType, resultType);
        var descriptor = Activator.CreateInstance(descriptorType, capabilityId, displayName, kind, capabilityType) as ICapabilityDescriptor;
        if (descriptor is null)
        {
            throw new InvalidOperationException($"Unable to create capability descriptor for '{capabilityType.FullName}'.");
        }

        _capabilityDescriptors.Add(descriptor);
        return this;
    }

    private void RemoveCapabilityServiceRegistrations(Type capabilityType)
    {
        for (var i = Services.Count - 1; i >= 0; i--)
        {
            if (Services[i].ServiceType == capabilityType)
            {
                Services.RemoveAt(i);
            }
        }
    }

    private static (Type RequestType, Type ResultType) ResolveCapabilitySignature(Type capabilityType, Type capabilityInterfaceType)
    {
        var matches = capabilityType
            .GetInterfaces()
            .Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == capabilityInterfaceType)
            .Select(type => type.GetGenericArguments())
            .ToArray();

        if (matches.Length == 1)
        {
            return (matches[0][0], matches[0][1]);
        }

        var reason = matches.Length == 0 ? "none" : "multiple";
        throw new InvalidOperationException(
            $"Capability type '{capabilityType.Name}' must implement exactly one {capabilityInterfaceType.Name} signature, but found {reason}.");
    }
}
