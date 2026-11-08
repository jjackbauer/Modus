using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;

namespace Wip.Runtime.Runtime;

/// <summary>
/// DeepSeek adapter for the provider-neutral <see cref="IChatModel"/> contract (WP-3.5).
/// </summary>
/// <remarks>
/// Vendor payload types stay private to this class; callers only see <see cref="ChatRequest"/>,
/// <see cref="ChatResponse"/> and <see cref="StreamingChunk"/>. The adapter maps the DeepSeek
/// chat-completions HTTP API, including server-sent-event streaming, and never logs or embeds the
/// API key in errors.
/// </remarks>
public sealed class DeepSeekChatModel : IChatModel
{
    private const int MaxRetryDelayMilliseconds = 250;
    private const string ProviderId = "deepseek";

    private static readonly HashSet<HttpStatusCode> TransientStatusCodes =
    [
        HttpStatusCode.RequestTimeout,
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout,
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly DeepSeekModelProviderOptions _options;
    private readonly DeepSeekThinkingMode _thinking;

    public DeepSeekChatModel(HttpClient httpClient, DeepSeekModelProviderOptions options)
        : this(httpClient, options, DeepSeekThinkingMode.Default)
    {
    }

    /// <summary>
    /// Creates the adapter with an explicit DeepSeek thinking mode. The legacy provider path sent
    /// <see cref="DeepSeekThinkingMode.Disabled"/> by default for the Bones agents; the generic
    /// <see cref="ChatRequest"/> has no thinking field, so hosts that need parity must opt in here
    /// (otherwise a reasoning model spends completion tokens and latency on hidden reasoning).
    /// </summary>
    public DeepSeekChatModel(
        HttpClient httpClient,
        DeepSeekModelProviderOptions options,
        DeepSeekThinkingMode thinking)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _thinking = thinking;

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = _options.BaseUrl;
        }
    }

    public async ValueTask<ChatResponse> CompleteAsync(
        ChatRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        using var timeoutSource = CreateTimeoutSource(cancellationToken);
        using var response = await SendWithRetryAsync(
            request,
            stream: false,
            timeoutSource.Token).ConfigureAwait(false);

        var content = await response.Content.ReadAsStringAsync(timeoutSource.Token).ConfigureAwait(false);

        try
        {
            using var document = JsonDocument.Parse(content);
            return MapCompleteResponse(request, document.RootElement);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "DeepSeek chat response was not valid JSON.",
                ex);
        }
    }

    public async IAsyncEnumerable<StreamingChunk> StreamAsync(
        ChatRequest request,
        CapabilityContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var toolNames = ToolNameMap.Create(request);

        using var timeoutSource = CreateTimeoutSource(cancellationToken);
        using var response = await SendWithRetryAsync(
            request,
            stream: true,
            timeoutSource.Token).ConfigureAwait(false);

        await using var contentStream = await response.Content
            .ReadAsStreamAsync(timeoutSource.Token)
            .ConfigureAwait(false);

        using var reader = new StreamReader(contentStream, Encoding.UTF8);
        var toolCalls = new SortedDictionary<int, ToolCallAccumulator>();
        ModelProviderUsage? usage = null;

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(timeoutSource.Token).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (!TryReadSsePayload(line, out var payload))
            {
                continue;
            }

            if (string.Equals(payload.Trim(), "[DONE]", StringComparison.Ordinal))
            {
                break;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(payload);
            }
            catch (JsonException)
            {
                // A malformed frame is a provider error; fail closed instead of silently dropping it.
                throw new InvalidOperationException("DeepSeek stream frame was not valid JSON.");
            }

            using (document)
            {
                var root = document.RootElement;

                if (root.TryGetProperty("usage", out var usageElement)
                    && usageElement.ValueKind == JsonValueKind.Object)
                {
                    usage = ReadUsage(usageElement);
                }

                if (!root.TryGetProperty("choices", out var choicesElement)
                    || choicesElement.ValueKind != JsonValueKind.Array
                    || choicesElement.GetArrayLength() == 0)
                {
                    continue;
                }

                var choice = choicesElement[0];
                if (!choice.TryGetProperty("delta", out var deltaElement)
                    || deltaElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (deltaElement.TryGetProperty("content", out var deltaContent)
                    && deltaContent.ValueKind == JsonValueKind.String)
                {
                    var text = deltaContent.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        yield return new StreamingChunk(text);
                    }
                }

                AccumulateToolCalls(deltaElement, toolCalls);
            }
        }

        foreach (var accumulator in toolCalls.Values)
        {
            if (accumulator.TryBuild(toolNames) is { } toolCall)
            {
                yield return new StreamingChunk(string.Empty, toolCall: toolCall);
            }
        }

        yield return new StreamingChunk(string.Empty, isFinal: true, usage: usage);
    }

    private CancellationTokenSource CreateTimeoutSource(CancellationToken cancellationToken)
    {
        var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_options.Timeout);
        return timeoutSource;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        ChatRequest request,
        bool stream,
        CancellationToken cancellationToken)
    {
        var maxAttempts = _options.MaxTransientRetries + 1;

        for (var attempt = 1; ; attempt++)
        {
            using var httpRequest = BuildHttpRequest(request, stream);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient
                    .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException) when (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(MaxRetryDelayMilliseconds), cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var statusCode = response.StatusCode;
            response.Dispose();

            if (IsTransient(statusCode) && attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(MaxRetryDelayMilliseconds), cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            throw new HttpRequestException(
                IsTransient(statusCode)
                    ? $"DeepSeek chat request failed with transient status code {(int)statusCode} ({statusCode}) after {attempt} attempts."
                    : $"DeepSeek chat request failed with status code {(int)statusCode} ({statusCode}).");
        }
    }

    private HttpRequestMessage BuildHttpRequest(ChatRequest request, bool stream)
    {
        // DeepSeek requires function names to match ^[a-zA-Z0-9_-]+$; the WIP capability ids use
        // dots (tool.repo.read_file). Sanitize on the wire and restore tool-call names on the
        // response so the agent layer keeps its canonical ids.
        var toolNames = ToolNameMap.Create(request);

        var payload = new DeepSeekChatHttpRequest(
            Model: request.ModelId,
            Messages: request.Messages.Select(message => MapMessage(message, toolNames)).ToArray(),
            Tools: request.Tools.Count == 0
                ? null
                : request.Tools.Select(tool => new DeepSeekToolPayload(
                    Type: "function",
                    Function: new DeepSeekFunctionDefinition(
                        Name: toolNames.ToProvider(tool.Name),
                        Description: tool.Description,
                        Parameters: tool.ParametersSchema))).ToArray(),
            MaxTokens: request.MaxTokens,
            Temperature: request.Temperature,
            Thinking: _thinking switch
            {
                DeepSeekThinkingMode.Enabled => new DeepSeekThinkingPayload("enabled"),
                DeepSeekThinkingMode.Disabled => new DeepSeekThinkingPayload("disabled"),
                _ => null,
            },
            Stream: stream ? true : null,
            StreamOptions: stream ? new DeepSeekStreamOptions(IncludeUsage: true) : null);

        var json = JsonSerializer.Serialize(payload, JsonOptions);

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ResolveApiKey());
        httpRequest.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(stream ? "text/event-stream" : "application/json"));

        return httpRequest;
    }

    private string ResolveApiKey()
    {
        const string environmentPrefix = "env:";
        if (!_options.ApiKey.StartsWith(environmentPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return _options.ApiKey;
        }

        var variableName = _options.ApiKey[environmentPrefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(variableName))
        {
            throw new InvalidOperationException(
                "DeepSeek chat configuration is invalid: ApiKeySource must use 'env:<VARIABLE_NAME>' format.");
        }

        var value = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"DeepSeek chat configuration is invalid: Environment variable '{variableName}' is required but was not found or empty.");
        }

        return value;
    }

    private static DeepSeekMessagePayload MapMessage(ChatMessage message, ToolNameMap toolNames)
    {
        var role = message.Role switch
        {
            ChatRole.System => "system",
            ChatRole.User => "user",
            ChatRole.Assistant => "assistant",
            ChatRole.Tool => "tool",
            _ => throw new ArgumentOutOfRangeException(nameof(message), message.Role, "Unsupported chat role."),
        };

        var toolCalls = message.ToolCalls.Count == 0
            ? null
            : message.ToolCalls.Select(call => new DeepSeekToolCallPayload(
                Id: call.Id,
                Type: "function",
                Function: new DeepSeekFunctionCallPayload(
                    Name: toolNames.ToProvider(call.Name),
                    Arguments: call.Arguments.GetRawText()))).ToArray();

        return new DeepSeekMessagePayload(
            Role: role,
            Content: message.Content,
            ToolCalls: toolCalls,
            ToolCallId: message.ToolCallId);
    }

    private static ChatResponse MapCompleteResponse(ChatRequest request, JsonElement root)
    {
        var toolNames = ToolNameMap.Create(request);
        var modelId = root.TryGetProperty("model", out var modelElement)
            && modelElement.ValueKind == JsonValueKind.String
            ? modelElement.GetString() ?? request.ModelId
            : request.ModelId;

        if (!root.TryGetProperty("choices", out var choicesElement)
            || choicesElement.ValueKind != JsonValueKind.Array
            || choicesElement.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("DeepSeek chat response did not include any choices.");
        }

        var choice = choicesElement[0];
        if (!choice.TryGetProperty("message", out var messageElement)
            || messageElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("DeepSeek chat response did not include an assistant message.");
        }

        var content = messageElement.TryGetProperty("content", out var contentElement)
            && contentElement.ValueKind == JsonValueKind.String
            ? contentElement.GetString() ?? string.Empty
            : string.Empty;

        var toolCalls = messageElement.TryGetProperty("tool_calls", out var toolCallsElement)
            && toolCallsElement.ValueKind == JsonValueKind.Array
            ? toolCallsElement.EnumerateArray().Select(element => MapToolCall(element, toolNames)).ToArray()
            : Array.Empty<ToolCall>();

        var usage = root.TryGetProperty("usage", out var usageElement)
            && usageElement.ValueKind == JsonValueKind.Object
            ? ReadUsage(usageElement)
            : null;

        return new ChatResponse(
            message: ChatMessage.Assistant(content, toolCalls.Length == 0 ? null : toolCalls),
            modelId: modelId,
            providerId: ProviderId,
            usage: usage,
            correlationId: request.CorrelationId);
    }

    private static ToolCall MapToolCall(JsonElement element, ToolNameMap toolNames)
    {
        if (!element.TryGetProperty("id", out var idElement)
            || idElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(idElement.GetString()))
        {
            throw new InvalidOperationException("DeepSeek chat response included a tool call without an id.");
        }

        if (!element.TryGetProperty("function", out var functionElement)
            || !functionElement.TryGetProperty("name", out var nameElement)
            || nameElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(nameElement.GetString()))
        {
            throw new InvalidOperationException("DeepSeek chat response included a tool call without a name.");
        }

        var argumentsJson = functionElement.TryGetProperty("arguments", out var argumentsElement)
            && argumentsElement.ValueKind == JsonValueKind.String
            ? argumentsElement.GetString() ?? string.Empty
            : string.Empty;

        return new ToolCall(
            idElement.GetString()!,
            toolNames.FromProvider(nameElement.GetString()!),
            ParseToolArguments(argumentsJson));
    }

    private static JsonElement ParseToolArguments(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            using var empty = JsonDocument.Parse("{}");
            return empty.RootElement.Clone();
        }

        try
        {
            using var document = JsonDocument.Parse(argumentsJson);
            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "DeepSeek chat response included malformed tool-call arguments.",
                ex);
        }
    }

    private static ModelProviderUsage ReadUsage(JsonElement usageElement)
    {
        var inputTokens = usageElement.TryGetProperty("prompt_tokens", out var promptTokens)
            && promptTokens.ValueKind == JsonValueKind.Number
            && promptTokens.TryGetInt32(out var parsedPromptTokens)
                ? parsedPromptTokens
                : 0;

        var outputTokens = usageElement.TryGetProperty("completion_tokens", out var completionTokens)
            && completionTokens.ValueKind == JsonValueKind.Number
            && completionTokens.TryGetInt32(out var parsedCompletionTokens)
                ? parsedCompletionTokens
                : 0;

        return new ModelProviderUsage(inputTokens, outputTokens);
    }

    private static bool TryReadSsePayload(string line, out string payload)
    {
        payload = string.Empty;

        if (string.IsNullOrWhiteSpace(line) || line.StartsWith(':'))
        {
            return false;
        }

        const string dataPrefix = "data:";
        if (!line.StartsWith(dataPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        payload = line[dataPrefix.Length..].TrimStart();
        return payload.Length > 0;
    }

    private static void AccumulateToolCalls(
        JsonElement deltaElement,
        SortedDictionary<int, ToolCallAccumulator> toolCalls)
    {
        if (!deltaElement.TryGetProperty("tool_calls", out var toolCallsElement)
            || toolCallsElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var fallbackIndex = toolCalls.Count == 0 ? 0 : toolCalls.Keys.Max() + 1;

        foreach (var callElement in toolCallsElement.EnumerateArray())
        {
            var index = callElement.TryGetProperty("index", out var indexElement)
                && indexElement.ValueKind == JsonValueKind.Number
                && indexElement.TryGetInt32(out var parsedIndex)
                    ? parsedIndex
                    : fallbackIndex;

            if (!toolCalls.TryGetValue(index, out var accumulator))
            {
                accumulator = new ToolCallAccumulator();
                toolCalls[index] = accumulator;
            }

            accumulator.Absorb(callElement);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) => TransientStatusCodes.Contains(statusCode);

    private sealed class ToolCallAccumulator
    {
        private readonly StringBuilder _arguments = new();
        private string? _id;
        private string? _name;

        public void Absorb(JsonElement callElement)
        {
            if (_id is null
                && callElement.TryGetProperty("id", out var idElement)
                && idElement.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(idElement.GetString()))
            {
                _id = idElement.GetString();
            }

            if (!callElement.TryGetProperty("function", out var functionElement)
                || functionElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (_name is null
                && functionElement.TryGetProperty("name", out var nameElement)
                && nameElement.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(nameElement.GetString()))
            {
                _name = nameElement.GetString();
            }

            if (functionElement.TryGetProperty("arguments", out var argumentsElement)
                && argumentsElement.ValueKind == JsonValueKind.String)
            {
                _arguments.Append(argumentsElement.GetString());
            }
        }

        public ToolCall? TryBuild(ToolNameMap toolNames)
        {
            if (_id is null || _name is null)
            {
                return null;
            }

            return new ToolCall(_id, toolNames.FromProvider(_name), ParseToolArguments(_arguments.ToString()));
        }
    }

    /// <summary>
    /// Maps WIP tool ids (for example <c>tool.repo.read_file</c>) onto the provider's function-name
    /// grammar <c>^[a-zA-Z0-9_-]+$</c> and back. The mapping is deterministic for a request: names
    /// are sorted, sanitized and disambiguated with a numeric suffix on collision, so the same
    /// domain name always reaches the wire under the same provider name.
    /// </summary>
    private sealed class ToolNameMap
    {
        private const int MaxProviderNameLength = 64;

        private readonly Dictionary<string, string> _toProvider;
        private readonly Dictionary<string, string> _fromProvider;

        private ToolNameMap(
            Dictionary<string, string> toProvider,
            Dictionary<string, string> fromProvider)
        {
            _toProvider = toProvider;
            _fromProvider = fromProvider;
        }

        public static ToolNameMap Create(ChatRequest request)
        {
            var originals = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var tool in request.Tools)
            {
                originals.Add(tool.Name);
            }

            foreach (var message in request.Messages)
            {
                foreach (var call in message.ToolCalls)
                {
                    originals.Add(call.Name);
                }
            }

            var toProvider = new Dictionary<string, string>(StringComparer.Ordinal);
            var fromProvider = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var original in originals)
            {
                var sanitized = Sanitize(original);
                var candidate = sanitized;
                var suffix = 1;
                while (fromProvider.ContainsKey(candidate))
                {
                    suffix++;
                    candidate = AppendSuffix(sanitized, suffix);
                }

                toProvider[original] = candidate;
                fromProvider[candidate] = original;
            }

            return new ToolNameMap(toProvider, fromProvider);
        }

        public string ToProvider(string name)
            => _toProvider.TryGetValue(name, out var mapped) ? mapped : name;

        public string FromProvider(string name)
            => _fromProvider.TryGetValue(name, out var mapped) ? mapped : name;

        private static string Sanitize(string name)
        {
            var builder = new StringBuilder(name.Length);
            foreach (var character in name)
            {
                builder.Append(char.IsAsciiLetterOrDigit(character) || character is '_' or '-'
                    ? character
                    : '_');
            }

            var sanitized = builder.ToString();
            if (sanitized.Length == 0)
            {
                sanitized = "tool";
            }

            return sanitized.Length <= MaxProviderNameLength
                ? sanitized
                : sanitized[..MaxProviderNameLength];
        }

        private static string AppendSuffix(string name, int suffix)
        {
            var suffixText = "_" + suffix.ToString(CultureInfo.InvariantCulture);
            var maxLength = MaxProviderNameLength - suffixText.Length;
            var trimmed = name.Length <= maxLength ? name : name[..maxLength];
            return trimmed + suffixText;
        }
    }

    private sealed record DeepSeekChatHttpRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<DeepSeekMessagePayload> Messages,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
         JsonPropertyName("tools")] IReadOnlyList<DeepSeekToolPayload>? Tools,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
         JsonPropertyName("max_tokens")] int? MaxTokens,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
         JsonPropertyName("temperature")] double? Temperature,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
         JsonPropertyName("thinking")] DeepSeekThinkingPayload? Thinking,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
         JsonPropertyName("stream")] bool? Stream,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
         JsonPropertyName("stream_options")] DeepSeekStreamOptions? StreamOptions);

    private sealed record DeepSeekStreamOptions(
        [property: JsonPropertyName("include_usage")] bool IncludeUsage);

    private sealed record DeepSeekThinkingPayload(
        [property: JsonPropertyName("type")] string Type);

    private sealed record DeepSeekMessagePayload(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
         JsonPropertyName("tool_calls")] IReadOnlyList<DeepSeekToolCallPayload>? ToolCalls,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
         JsonPropertyName("tool_call_id")] string? ToolCallId);

    private sealed record DeepSeekToolPayload(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("function")] DeepSeekFunctionDefinition Function);

    private sealed record DeepSeekFunctionDefinition(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("parameters")] JsonElement Parameters);

    private sealed record DeepSeekToolCallPayload(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("function")] DeepSeekFunctionCallPayload Function);

    private sealed record DeepSeekFunctionCallPayload(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("arguments")] string Arguments);
}
