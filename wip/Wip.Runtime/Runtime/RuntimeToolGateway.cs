using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Descriptors;
using Wip.Abstractions.Identifiers;

namespace Wip.Runtime.Runtime;

public sealed class RuntimeToolGateway
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IReadOnlyDictionary<CapabilityId, ICapabilityDescriptor> _toolDescriptors;

    public RuntimeToolGateway(
        IServiceProvider serviceProvider,
        IReadOnlyCollection<ICapabilityDescriptor> capabilityDescriptors)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        ArgumentNullException.ThrowIfNull(capabilityDescriptors);

        _toolDescriptors = capabilityDescriptors
            .Where(static descriptor => descriptor.Kind == CapabilityKind.Tool)
            .ToDictionary(static descriptor => descriptor.CapabilityId);
    }

    public ValueTask<TResult> InvokeAsync<TRequest, TResult>(
        CapabilityId toolId,
        TRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
        where TRequest : notnull
        where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_toolDescriptors.TryGetValue(toolId, out var descriptor))
        {
            throw new InvalidOperationException(
                $"Tool capability '{toolId.Value}' is not registered in the runtime gateway.");
        }

        if (descriptor.RequestType != typeof(TRequest) || descriptor.ResultType != typeof(TResult))
        {
            throw new InvalidOperationException(
                $"Tool '{toolId.Value}' expects request/result '{descriptor.RequestType.Name}/{descriptor.ResultType.Name}' but received '{typeof(TRequest).Name}/{typeof(TResult).Name}'.");
        }

        var service = _serviceProvider.GetService(descriptor.CapabilityType);
        if (service is not ITool<TRequest, TResult> tool)
        {
            throw new InvalidOperationException(
                $"Tool '{toolId.Value}' of type '{descriptor.CapabilityType.FullName}' is not resolvable as ITool<{typeof(TRequest).Name}, {typeof(TResult).Name}>.");
        }

        return tool.ExecuteAsync(request, context, cancellationToken);
    }
}