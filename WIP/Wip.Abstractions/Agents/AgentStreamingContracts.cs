using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;

namespace Wip.Abstractions.Agents;

public enum AgentStreamEventKind
{
    Delta,
    ToolCall,
    ToolResult,
    Completed,
    Failed,
}

public sealed record AgentStreamEvent(
    AgentStreamEventKind Kind,
    string Content = "",
    ToolCall? ToolCall = null,
    ToolResult? ToolResult = null,
    AgentExecutionResult? Result = null);

/// <summary>
/// Streaming variant of the agent loop (WP-3.5): model deltas are surfaced as they arrive, tool
/// results are reported as they complete, and a final Completed/Failed event carries the result.
/// </summary>
public interface IStreamingAgentExecutor
{
    IAsyncEnumerable<AgentStreamEvent> ExecuteStreamingAsync(
        AgentExecutionRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken);
}