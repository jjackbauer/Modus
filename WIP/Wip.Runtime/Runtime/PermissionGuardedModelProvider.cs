using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Permissions;

namespace Wip.Runtime.Runtime;

/// <summary>
/// WP-1.7: wraps a model provider so every invocation evaluates <c>model.invoke</c>. Built-in
/// providers (no plugin origin) are trusted; plugin providers need a grant. Denials happen before
/// the inner provider is called.
/// </summary>
public sealed class PermissionGuardedModelProvider<TRequestPayload, TResultPayload>
    : IModelProvider<TRequestPayload, TResultPayload>
    where TRequestPayload : notnull
    where TResultPayload : notnull
{
    private readonly IModelProvider<TRequestPayload, TResultPayload> _inner;
    private readonly IPermissionEvaluator _evaluator;
    private readonly PluginOrigin? _pluginOrigin;

    public PermissionGuardedModelProvider(
        IModelProvider<TRequestPayload, TResultPayload> inner,
        IPermissionEvaluator evaluator,
        PluginOrigin? pluginOrigin = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _pluginOrigin = pluginOrigin;
    }

    public ValueTask<ModelProviderResponse<TResultPayload>> ExecuteAsync(
        ModelProviderRequest<TRequestPayload> request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        var decision = _evaluator.Evaluate(new PermissionRequest(
            PermissionCatalog.ModelInvoke,
            CapabilityId: null,
            PluginOrigin: _pluginOrigin,
            SessionId: context.SessionId,
            IsBuiltIn: _pluginOrigin is null));

        if (!decision.Allowed)
        {
            throw new PermissionDeniedException(PermissionCatalog.ModelInvoke, decision.Reason);
        }

        return _inner.ExecuteAsync(request, context, cancellationToken);
    }
}