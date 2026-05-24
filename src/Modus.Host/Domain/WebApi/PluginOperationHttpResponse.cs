using Modus.Core.Messaging;

namespace Modus.Host.Domain.WebApi;

/// <summary>
/// HTTP response DTO for plugin operation results.
/// Wraps the operation outcome with status information and optional correlation tracking.
/// </summary>
public class PluginOperationHttpResponse
{
    /// <summary>
    /// Indicates whether the operation completed successfully.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Structured or primitive payload produced by the sync operation.
    /// Serialized to JSON preserving object or primitive shape.
    /// </summary>
    public object? Payload { get; set; }

    /// <summary>
    /// Status descriptor for the operation outcome.
    /// </summary>
    public SyncResponseStatus? Status { get; set; }

    /// <summary>
    /// Optional correlation identifier echoed from the request, if provided.
    /// Used to correlate responses with requests in distributed tracing scenarios.
    /// </summary>
    public string? CorrelationId { get; set; }
}
