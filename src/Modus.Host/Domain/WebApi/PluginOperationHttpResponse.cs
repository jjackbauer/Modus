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
    /// Freeform response payload as a string.
    /// The schema and content are determined by the specific plugin operation.
    /// </summary>
    public string? Payload { get; set; }

    /// <summary>
    /// Optional structured payload for typed responses.
    /// This preserves object shape for callers that need schema-aware processing.
    /// </summary>
    public object? PayloadObject { get; set; }

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
