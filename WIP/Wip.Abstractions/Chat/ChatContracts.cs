using System.Text.Json;
using Wip.Abstractions.Capabilities;

namespace Wip.Abstractions.Chat;

public enum ChatRole
{
    System,
    User,
    Assistant,
    Tool,
}

public sealed record ToolDefinition
{
    public ToolDefinition(string name, string description, JsonElement parametersSchema)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A tool name is required.", nameof(name));

        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("A tool description is required.", nameof(description));

        Name = name;
        Description = description;
        ParametersSchema = parametersSchema.Clone();
    }

    public string Name { get; }

    public string Description { get; }

    public JsonElement ParametersSchema { get; }
}

public sealed record ToolCall
{
    public ToolCall(string id, string name, JsonElement arguments)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A tool call id is required.", nameof(id));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A tool name is required.", nameof(name));

        Id = id;
        Name = name;
        Arguments = arguments.Clone();
    }

    public string Id { get; }

    public string Name { get; }

    public JsonElement Arguments { get; }
}

public sealed record ToolResult(
    string ToolCallId,
    string Content,
    bool IsError = false)
{
    public static ToolResult Success(string toolCallId, string content) => new(toolCallId, content);

    public static ToolResult Error(string toolCallId, string content) => new(toolCallId, content, IsError: true);
}

public sealed record ChatMessage
{
    public ChatMessage(
        ChatRole role,
        string content,
        IReadOnlyList<ToolCall>? toolCalls = null,
        string? toolCallId = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        Role = role;
        Content = content;
        ToolCalls = toolCalls ?? Array.Empty<ToolCall>();
        ToolCallId = toolCallId;
    }

    public ChatRole Role { get; }

    public string Content { get; }

    public IReadOnlyList<ToolCall> ToolCalls { get; }

    public string? ToolCallId { get; }

    public static ChatMessage System(string content) => new(ChatRole.System, content);

    public static ChatMessage User(string content) => new(ChatRole.User, content);

    public static ChatMessage Assistant(string content, IReadOnlyList<ToolCall>? toolCalls = null)
        => new(ChatRole.Assistant, content, toolCalls);

    public static ChatMessage Tool(string toolCallId, string content, bool isError = false)
        => new(ChatRole.Tool, content, toolCallId: toolCallId);
}

public sealed record ChatRequest
{
    public ChatRequest(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        int? maxTokens = null,
        double? temperature = null,
        string? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            throw new ArgumentException("A model id is required.", nameof(modelId));

        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count == 0)
            throw new ArgumentException("A chat request needs at least one message.", nameof(messages));

        if (maxTokens is <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTokens), "Max tokens must be positive.");

        if (temperature is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(temperature), "Temperature must be between 0 and 2.");

        ModelId = modelId;
        Messages = messages.ToArray();
        Tools = tools?.ToArray() ?? Array.Empty<ToolDefinition>();
        MaxTokens = maxTokens;
        Temperature = temperature;
        CorrelationId = correlationId;
    }

    public string ModelId { get; }

    public IReadOnlyList<ChatMessage> Messages { get; }

    public IReadOnlyList<ToolDefinition> Tools { get; }

    public int? MaxTokens { get; }

    public double? Temperature { get; }

    public string? CorrelationId { get; }
}

public sealed record ChatResponse
{
    public ChatResponse(
        ChatMessage message,
        string modelId,
        string providerId,
        ModelProviderUsage? usage = null,
        string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (string.IsNullOrWhiteSpace(modelId))
            throw new ArgumentException("A model id is required.", nameof(modelId));
        if (string.IsNullOrWhiteSpace(providerId))
            throw new ArgumentException("A provider id is required.", nameof(providerId));

        Message = message;
        ModelId = modelId;
        ProviderId = providerId;
        Usage = usage;
        CorrelationId = correlationId;
    }

    public ChatMessage Message { get; }

    public string ModelId { get; }

    public string ProviderId { get; }

    public ModelProviderUsage? Usage { get; }

    public string? CorrelationId { get; }
}

public sealed record StreamingChunk
{
    public StreamingChunk(
        string delta,
        bool isFinal = false,
        ToolCall? toolCall = null,
        ModelProviderUsage? usage = null)
    {
        ArgumentNullException.ThrowIfNull(delta);
        Delta = delta;
        IsFinal = isFinal;
        ToolCall = toolCall;
        Usage = usage;
    }

    public string Delta { get; }

    public bool IsFinal { get; }

    public ToolCall? ToolCall { get; }

    public ModelProviderUsage? Usage { get; }
}

/// <summary>
/// Provider-neutral chat contract (WP-3.1). Implementations adapt a specific vendor without leaking
/// vendor types into the agent layer; <see cref="IModelProvider{TRequestPayload,TResultPayload}"/>
/// stays for backward compatibility.
/// </summary>
public interface IChatModel
{
    ValueTask<ChatResponse> CompleteAsync(
        ChatRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken);

    IAsyncEnumerable<StreamingChunk> StreamAsync(
        ChatRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken);
}