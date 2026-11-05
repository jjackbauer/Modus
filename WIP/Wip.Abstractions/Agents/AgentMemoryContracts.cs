using Wip.Abstractions.Chat;
using Wip.Abstractions.Identifiers;

namespace Wip.Abstractions.Agents;

public sealed record AgentMemoryOptions
{
    public int MaxMessages { get; init; } = 50;
}

public sealed record AgentMemorySnapshot(IReadOnlyList<ChatMessage> Messages)
{
    public static AgentMemorySnapshot Empty { get; } = new(Array.Empty<ChatMessage>());
}

/// <summary>
/// Session-scoped agent memory (WP-3.4). Implementations persist the conversation across turns,
/// isolate sessions, and can summarize older turns for prompt budgets.
/// </summary>
public interface IAgentMemory
{
    ValueTask<AgentMemorySnapshot> LoadAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask AppendAsync(SessionId sessionId, ChatMessage message, CancellationToken cancellationToken);

    ValueTask<string> SummarizeAsync(SessionId sessionId, int maxMessages, CancellationToken cancellationToken);
}