using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Bones.Agents.Configuration;

namespace Wip.Bones.Agents.Runtime;

/// <summary>
/// Stage-model adapter over the shared <see cref="IChatModel"/> contract. The Bones agents address
/// models by stage-level ids (for example <c>bones-strategy-author</c> or <c>bones-play-turn</c>);
/// this decorator rewrites any id that is not a configured DeepSeek model identifier to the
/// configured model while passing supported ids, messages and tools through untouched.
/// </summary>
public sealed class BonesStageChatModel : IChatModel
{
    private readonly IChatModel _inner;
    private readonly string _modelId;

    public BonesStageChatModel(IChatModel inner, string modelId)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        _modelId = modelId.Trim();
    }

    public ValueTask<ChatResponse> CompleteAsync(
        ChatRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _inner.CompleteAsync(Rewrite(request), context, cancellationToken);
    }

    public IAsyncEnumerable<StreamingChunk> StreamAsync(
        ChatRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _inner.StreamAsync(Rewrite(request), context, cancellationToken);
    }

    private ChatRequest Rewrite(ChatRequest request)
        => BonesDeepSeekModelIdentifier.IsSupported(request.ModelId)
            ? request
            : new ChatRequest(
                modelId: _modelId,
                messages: request.Messages,
                tools: request.Tools,
                maxTokens: request.MaxTokens,
                temperature: request.Temperature,
                correlationId: request.CorrelationId);
}