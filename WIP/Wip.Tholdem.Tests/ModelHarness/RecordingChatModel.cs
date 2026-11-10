using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Identifiers;

namespace Wip.Tholdem.Tests.ModelHarness;

/// <summary>
/// A local scripted <see cref="IChatModel"/>: every request is recorded, and responses or failures are
/// dequeued in the exact order the policy sends calls. It makes no network access and holds no key.
/// </summary>
internal sealed class RecordingChatModel : IChatModel
{
    private readonly Queue<object> _script = new();

    public List<ChatRequest> Requests { get; } = [];

    public int CallCount => Requests.Count;

    public void EnqueueResponse(ChatResponse response) => _script.Enqueue(response);

    public void EnqueueFailure(Exception exception) => _script.Enqueue(exception);

    public ValueTask<ChatResponse> CompleteAsync(
        ChatRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (_script.Count == 0)
        {
            throw new InvalidOperationException("The recording chat model has no scripted response left.");
        }

        var next = _script.Dequeue();
        if (next is Exception failure)
        {
            throw failure;
        }

        return ValueTask.FromResult((ChatResponse)next);
    }

    public IAsyncEnumerable<StreamingChunk> StreamAsync(
        ChatRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public static ChatResponse Response(
        string content,
        string servedModel = "deepseek-chat",
        int inputTokens = 11,
        int outputTokens = 7)
        => new(
            ChatMessage.Assistant(content),
            servedModel,
            "test-provider",
            new ModelProviderUsage(inputTokens, outputTokens));
}
