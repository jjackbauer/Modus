using Wip.Abstractions.Capabilities;
using Wip.Bones.Agents.Play;
using Wip.Runtime.Runtime;

namespace Wip.Bones.ModelProviders.DeepSeek;

public sealed class DeepSeekBonesPlayTurnProvider
    : IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>
{
    private readonly DeepSeekModelProvider _deepSeekModelProvider;
    private readonly BonesDeepSeekProviderOptions _options;

    public DeepSeekBonesPlayTurnProvider(
        DeepSeekModelProvider deepSeekModelProvider,
        BonesDeepSeekProviderOptions options)
    {
        _deepSeekModelProvider = deepSeekModelProvider ?? throw new ArgumentNullException(nameof(deepSeekModelProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async ValueTask<ModelProviderResponse<BonesPlayTurnResult>> ExecuteAsync(
        ModelProviderRequest<BonesPlayTurnRequest> request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Payload);

        var messages = BonesDeepSeekMessageMapper.MapPlayTurnMessages(request.Payload.Messages);
        var chatRequest = new DeepSeekChatCompletionRequest(messages);

        var deepSeekResponse = await _deepSeekModelProvider.ExecuteAsync(
            new ModelProviderRequest<DeepSeekChatCompletionRequest>(
                payload: chatRequest,
                modelId: ResolveModelId(request.ModelId),
                correlationId: request.CorrelationId),
            context,
            cancellationToken);

        var selectedMoveId = BonesPlayTurnResponseParser.Parse(
                deepSeekResponse.Payload.Content,
                request.Payload.AllowedMoves)
            ?? string.Empty;

        return new ModelProviderResponse<BonesPlayTurnResult>(
            payload: new BonesPlayTurnResult(selectedMoveId),
            providerId: deepSeekResponse.ProviderId,
            modelId: deepSeekResponse.ModelId,
            usage: deepSeekResponse.Usage,
            correlationId: deepSeekResponse.CorrelationId);
    }

    private string ResolveModelId(string requestModelId)
    {
        if (string.IsNullOrWhiteSpace(requestModelId))
            return _options.Model.Value;

        var trimmed = requestModelId.Trim();
        return trimmed is "deepseek-chat" or "deepseek-reasoner"
            ? trimmed
            : _options.Model.Value;
    }
}