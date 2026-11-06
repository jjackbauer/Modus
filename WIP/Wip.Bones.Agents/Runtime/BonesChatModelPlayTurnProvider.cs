using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Bones.Agents.Play;

namespace Wip.Bones.Agents.Runtime;

/// <summary>
/// Play-turn <see cref="IModelProvider{TRequestPayload,TResultPayload}"/> implemented over the
/// provider-neutral <see cref="IChatModel"/>. Prompt construction stays in
/// <see cref="BonesPlayTurnRequest"/>; this provider only maps the play-turn messages onto a chat
/// request and parses the model answer with <see cref="BonesPlayTurnResponseParser"/>.
/// </summary>
public sealed class BonesChatModelPlayTurnProvider
    : IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>
{
    private readonly IChatModel _chatModel;

    public BonesChatModelPlayTurnProvider(IChatModel chatModel)
    {
        _chatModel = chatModel ?? throw new ArgumentNullException(nameof(chatModel));
    }

    public async ValueTask<ModelProviderResponse<BonesPlayTurnResult>> ExecuteAsync(
        ModelProviderRequest<BonesPlayTurnRequest> request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Payload);

        var chatRequest = new ChatRequest(
            modelId: request.ModelId,
            messages: request.Payload.Messages.Select(ToChatMessage).ToArray(),
            correlationId: request.CorrelationId);

        var response = await _chatModel.CompleteAsync(chatRequest, context, cancellationToken);

        var selectedMoveId = BonesPlayTurnResponseParser.Parse(
                response.Message.Content,
                request.Payload.AllowedMoves)
            ?? string.Empty;

        return new ModelProviderResponse<BonesPlayTurnResult>(
            payload: new BonesPlayTurnResult(selectedMoveId),
            providerId: response.ProviderId,
            modelId: response.ModelId,
            usage: response.Usage,
            correlationId: response.CorrelationId ?? request.CorrelationId);
    }

    private static ChatMessage ToChatMessage(BonesPlayTurnMessage message)
        => message.Role switch
        {
            "system" => ChatMessage.System(message.Content),
            "user" => ChatMessage.User(message.Content),
            "assistant" => ChatMessage.Assistant(message.Content),
            _ => throw new InvalidOperationException(
                $"Unsupported Bones play-turn prompt role '{message.Role}'."),
        };
}