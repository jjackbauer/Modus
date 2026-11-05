using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Identifiers;

namespace Wip.Abstractions.Agents;

public sealed record AgentLoopOptions
{
    public int MaxSteps { get; init; } = 8;

    public int? MaxTotalTokens { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);

    public int MaxConsecutiveToolFailures { get; init; } = 3;
}

public sealed record AgentExecutionRequest
{
    public AgentExecutionRequest(
        SessionId sessionId,
        string task,
        string modelId,
        string? systemPrompt = null,
        IReadOnlyList<ToolDefinition>? tools = null,
        IReadOnlyList<ChatMessage>? initialMessages = null,
        AgentLoopOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(task))
            throw new ArgumentException("An agent task is required.", nameof(task));

        if (string.IsNullOrWhiteSpace(modelId))
            throw new ArgumentException("A model id is required.", nameof(modelId));

        SessionId = sessionId;
        Task = task;
        ModelId = modelId;
        SystemPrompt = systemPrompt;
        Tools = tools ?? Array.Empty<ToolDefinition>();
        InitialMessages = initialMessages ?? Array.Empty<ChatMessage>();
        Options = options ?? new AgentLoopOptions();
    }

    public SessionId SessionId { get; }

    public string Task { get; }

    public string ModelId { get; }

    public string? SystemPrompt { get; }

    public IReadOnlyList<ToolDefinition> Tools { get; }

    public IReadOnlyList<ChatMessage> InitialMessages { get; }

    public AgentLoopOptions Options { get; }
}

public enum AgentExecutionStatus
{
    Completed,
    MaxStepsReached,
    TokenBudgetExhausted,
    ToolFailureLimitReached,
    TimedOut,
    Failed,
}

public sealed record AgentStep(
    int Index,
    ChatMessage AssistantMessage,
    IReadOnlyList<ToolResult> ToolResults);

public sealed record AgentExecutionResult(
    AgentExecutionStatus Status,
    ChatMessage? FinalMessage,
    IReadOnlyList<AgentStep> Steps,
    ModelProviderUsage? Usage,
    string? Error)
{
    public bool Succeeded => Status == AgentExecutionStatus.Completed;
}

/// <summary>
/// Provider-neutral agent loop (WP-3.3): model turn -> tool calls -> observations -> repeat until a
/// final answer or a budget/limit/cancellation stop.
/// </summary>
public interface IAgentExecutor
{
    ValueTask<AgentExecutionResult> ExecuteAsync(
        AgentExecutionRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken);
}