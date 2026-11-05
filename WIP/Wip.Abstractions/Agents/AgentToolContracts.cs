namespace Wip.Abstractions.Agents;

public sealed record AgentExecutorToolRequest(
    string Task,
    string ModelId,
    string? SystemPrompt = null);

public sealed record AgentExecutorToolResult(
    bool Succeeded,
    AgentExecutionStatus Status,
    string? FinalAnswer,
    int Steps,
    string? Error);