using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Play;
using Wip.Bones.Agents.Ponder;
using Wip.Runtime.Runtime;

namespace Wip.Bones.ModelProviders.DeepSeek;

public static class BonesDeepSeekMessageMapper
{
    public static IReadOnlyList<DeepSeekChatMessage> MapStrategyAuthoringMessages(
        IReadOnlyList<BonesStrategyAuthoringMessage> messages)
        => Map(messages, static message => (message.Role, message.Content));

    public static IReadOnlyList<DeepSeekChatMessage> MapPlayTurnMessages(
        IReadOnlyList<BonesPlayTurnMessage> messages)
        => Map(messages, static message => (message.Role, message.Content));

    public static IReadOnlyList<DeepSeekChatMessage> MapEnhancementMessages(
        IReadOnlyList<BonesStrategyEnhancementMessage> messages)
        => Map(messages, static message => (message.Role, message.Content));

    private static IReadOnlyList<DeepSeekChatMessage> Map<T>(
        IReadOnlyList<T> messages,
        Func<T, (string Role, string Content)> selector)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return messages
            .Select(message =>
            {
                var (role, content) = selector(message);
                return new DeepSeekChatMessage(role, content);
            })
            .ToArray();
    }
}