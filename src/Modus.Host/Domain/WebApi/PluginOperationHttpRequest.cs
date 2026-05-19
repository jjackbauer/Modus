namespace Modus.Host.Domain.WebApi;

/// <summary>
/// HTTP request DTO for plugin operation invocations.
/// Carries the request payload and optional correlation identifier.
/// </summary>
public class PluginOperationHttpRequest
{
    /// <summary>
    /// Optional correlation identifier to track the request across distributed systems.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Freeform request payload as a string.
    /// The schema and content are determined by the specific plugin operation.
    /// </summary>
    public string? Payload { get; set; }
}
