using Modus.Core.Messaging;
using Modus.Core.Plugins;

namespace Modus.Host.Domain.WebApi;

/// <summary>
/// Dispatcher that routes synchronous operation requests to the appropriate plugin's ISyncResponder.
/// Iterates through registered responders and delegates to the first one that handles the operation.
/// </summary>
public class PluginOperationSyncResponderDispatcher : ISyncResponder
{
    private readonly IEnumerable<ISyncResponder> _responders;

    public PluginOperationSyncResponderDispatcher(IEnumerable<ISyncResponder> responders)
    {
        _responders = responders ?? throw new ArgumentNullException(nameof(responders));
    }

    /// <summary>
    /// Dispatches the request to the first responder that handles the operation.
    /// If no responder handles it, returns a rejected response with operation-not-found status.
    /// </summary>
    public SyncResponse Handle(SyncRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Try each responder until one successfully handles the operation
        foreach (var responder in _responders)
        {
            var response = responder.Handle(request);
            
            // If the responder handled it (didn't reject with unsupported-operation),
            // return its response
            if (response.Success || response.Status != SyncResponseStatus.Rejected)
            {
                return response;
            }
        }

        // No responder handled this operation
        return new SyncResponse(
            Success: false,
            Payload: new SyncErrorPayload(
                Code: "operation-not-found",
                Message: $"No sync responder handled operation '{request.Operation.Value}'."),
            Status: SyncResponseStatus.Rejected,
            CorrelationId: request.CorrelationId);
    }
}
