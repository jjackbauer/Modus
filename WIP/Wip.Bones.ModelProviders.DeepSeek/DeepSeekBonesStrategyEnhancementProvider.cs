using Wip.Abstractions.Capabilities;
using Wip.Bones.Agents.Enhance;
using Wip.Runtime.Runtime;

namespace Wip.Bones.ModelProviders.DeepSeek;

public sealed class DeepSeekBonesStrategyEnhancementProvider
    : IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>
{
    private readonly DeepSeekModelProvider _deepSeekModelProvider;
    private readonly BonesDeepSeekProviderOptions _options;

    public DeepSeekBonesStrategyEnhancementProvider(
        DeepSeekModelProvider deepSeekModelProvider,
        BonesDeepSeekProviderOptions options)
    {
        _deepSeekModelProvider = deepSeekModelProvider ?? throw new ArgumentNullException(nameof(deepSeekModelProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async ValueTask<ModelProviderResponse<BonesStrategyEnhancementResult>> ExecuteAsync(
        ModelProviderRequest<BonesStrategyEnhancementRequest> request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Payload);

        var messages = BonesDeepSeekMessageMapper.MapEnhancementMessages(request.Payload.Messages);
        var chatRequest = new DeepSeekChatCompletionRequest(messages);

        var deepSeekResponse = await _deepSeekModelProvider.ExecuteAsync(
            new ModelProviderRequest<DeepSeekChatCompletionRequest>(
                payload: chatRequest,
                modelId: ResolveModelId(request.ModelId),
                correlationId: request.CorrelationId),
            context,
            cancellationToken);

        return new ModelProviderResponse<BonesStrategyEnhancementResult>(
            payload: new BonesStrategyEnhancementResult(deepSeekResponse.Payload.Content),
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