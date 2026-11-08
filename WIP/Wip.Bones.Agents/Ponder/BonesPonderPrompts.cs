using Wip.Abstractions.Agents;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Agents.Runtime;

namespace Wip.Bones.Agents.Ponder;

/// <summary>
/// Owns the conversion from the existing Bones prompt messages to the provider-neutral runtime
/// request. The prompt text itself stays in <see cref="BonesPonderPromptBuilder"/> (the single
/// source of truth shared with the legacy provider path); this class only maps roles, task and
/// initial messages, inserts optional memory and attaches the agent-loop tools/options.
/// </summary>
internal static class BonesPonderPrompts
{
    internal static IReadOnlyList<ToolDefinition> Tools { get; } =
        [BonesAgentToolDefinitions.StrategyCompile];

    internal static AgentExecutionRequest CreateExecutionRequest(
        IReadOnlyList<BonesStrategyAuthoringMessage> messages,
        string modelId,
        SessionId sessionId,
        IReadOnlyList<ChatMessage>? memoryMessages,
        AgentLoopOptions loopOptions,
        bool includeTools = true)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(loopOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        var systemMessage = messages.FirstOrDefault(message => message.Role == "system")
            ?? throw new InvalidOperationException("Bones ponder prompts require a system message.");

        var conversation = messages
            .Where(message => message.Role != "system")
            .ToArray();

        if (conversation.Length == 0)
            throw new InvalidOperationException("Bones ponder prompts require a user task message.");

        var initialMessages = new List<ChatMessage>();
        if (memoryMessages is { Count: > 0 })
            initialMessages.AddRange(memoryMessages);

        initialMessages.AddRange(conversation
            .Take(conversation.Length - 1)
            .Select(ToChatMessage));

        return new AgentExecutionRequest(
            sessionId: sessionId,
            task: conversation[^1].Content,
            modelId: modelId,
            systemPrompt: systemMessage.Content,
            tools: includeTools ? Tools : Array.Empty<ToolDefinition>(),
            initialMessages: initialMessages,
            options: loopOptions);
    }

    internal static string ReadCompletedMarkdown(AgentExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var finalMessage = result.FinalMessage
            ?? throw new InvalidOperationException(
                "The agent runtime completed without a final assistant message.");

        return finalMessage.Content;
    }

    private static ChatMessage ToChatMessage(BonesStrategyAuthoringMessage message)
        => message.Role switch
        {
            "user" => ChatMessage.User(message.Content),
            "assistant" => ChatMessage.Assistant(message.Content),
            _ => throw new InvalidOperationException(
                $"Unsupported Bones ponder prompt role '{message.Role}'."),
        };
}
