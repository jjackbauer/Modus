using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wip.Abstractions.Capabilities;

namespace Wip.Runtime.Runtime;

public sealed record DeepSeekChatMessage(string Role, string Content);

public sealed record DeepSeekChatCompletionRequest(IReadOnlyList<DeepSeekChatMessage> Messages);

public sealed record DeepSeekChatCompletionResult(string Content, string? FinishReason);

public sealed record DeepSeekModelProviderOptions
{
    public DeepSeekModelProviderOptions(
        Uri baseUrl,
        string apiKey,
        TimeSpan timeout,
        int maxTransientRetries = 2)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);

        if (!baseUrl.IsAbsoluteUri)
            throw new ArgumentException("BaseUrl must be an absolute URI.", nameof(baseUrl));

        if (baseUrl.Scheme != Uri.UriSchemeHttp && baseUrl.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("BaseUrl must use HTTP or HTTPS.", nameof(baseUrl));

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(apiKey));

        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be greater than zero.");

        if (maxTransientRetries < 0)
            throw new ArgumentOutOfRangeException(nameof(maxTransientRetries), "Value must be greater than or equal to zero.");

        BaseUrl = baseUrl;
        ApiKey = apiKey;
        Timeout = timeout;
        MaxTransientRetries = maxTransientRetries;
    }

    public Uri BaseUrl { get; }

    public string ApiKey { get; }

    public TimeSpan Timeout { get; }

    public int MaxTransientRetries { get; }
}

public sealed class DeepSeekModelProvider : IModelProvider<DeepSeekChatCompletionRequest, DeepSeekChatCompletionResult>
{
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
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly DeepSeekModelProviderOptions _options;

    public DeepSeekModelProvider(HttpClient httpClient, DeepSeekModelProviderOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (_httpClient.BaseAddress is null)
            _httpClient.BaseAddress = _options.BaseUrl;
    }

    public async ValueTask<ModelProviderResponse<DeepSeekChatCompletionResult>> ExecuteAsync(
        ModelProviderRequest<DeepSeekChatCompletionRequest> request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Payload);

        if (request.Payload.Messages.Count == 0)
            throw new InvalidOperationException("DeepSeek request requires at least one message.");

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_options.Timeout);
        var token = timeoutSource.Token;
        var maxAttempts = _options.MaxTransientRetries + 1;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            token.ThrowIfCancellationRequested();

            using var httpRequest = BuildHttpRequest(request);

            try
            {
                using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, token);

                if (response.IsSuccessStatusCode)
                    return await MapSuccessResponseAsync(request, response, token);

                if (IsTransient(response.StatusCode))
                {
                    if (attempt < maxAttempts)
                        continue;

                    throw new HttpRequestException(
                        $"DeepSeek request failed with transient status code {(int)response.StatusCode} ({response.StatusCode}) after {attempt} attempts.");
                }

                throw new HttpRequestException(
                    $"DeepSeek request failed with status code {(int)response.StatusCode} ({response.StatusCode}).");
            }
            catch (HttpRequestException) when (attempt < maxAttempts)
            {
            }
        }

        throw new HttpRequestException(
            $"DeepSeek request failed after {maxAttempts} attempts.");
    }

    private HttpRequestMessage BuildHttpRequest(ModelProviderRequest<DeepSeekChatCompletionRequest> request)
    {
        var apiKey = ResolveApiKey();
        var payload = new DeepSeekChatCompletionHttpRequest(
            Model: request.ModelId,
            Messages: request.Payload.Messages
                .Select(static message => new DeepSeekChatMessagePayload(message.Role, message.Content))
                .ToArray());

        var json = JsonSerializer.Serialize(payload, JsonOptions);

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return httpRequest;
    }

    private string ResolveApiKey()
    {
        if (!TryResolveEnvironmentApiKey(_options.ApiKey, out var resolvedApiKey))
            return _options.ApiKey;

        return resolvedApiKey;
    }

    private static bool TryResolveEnvironmentApiKey(string apiKey, out string resolvedApiKey)
    {
        resolvedApiKey = string.Empty;

        if (!apiKey.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
            return false;

        var variableName = apiKey[4..].Trim();
        if (string.IsNullOrWhiteSpace(variableName))
            throw new InvalidOperationException("DeepSeek request configuration is invalid: ApiKeySource must use 'env:<VARIABLE_NAME>' format.");

        var value = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"DeepSeek provider configuration is invalid: Environment variable '{variableName}' is required but was not found or empty.");
        }

        resolvedApiKey = value;
        return true;
    }

    private static async Task<ModelProviderResponse<DeepSeekChatCompletionResult>> MapSuccessResponseAsync(
        ModelProviderRequest<DeepSeekChatCompletionRequest> request,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        using var json = JsonDocument.Parse(content);

        var root = json.RootElement;
        var modelId = root.TryGetProperty("model", out var modelElement) && modelElement.ValueKind == JsonValueKind.String
            ? modelElement.GetString() ?? request.ModelId
            : request.ModelId;

        if (!root.TryGetProperty("choices", out var choicesElement)
            || choicesElement.ValueKind != JsonValueKind.Array
            || choicesElement.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("DeepSeek response did not include any choices.");
        }

        var firstChoice = choicesElement[0];
        if (!firstChoice.TryGetProperty("message", out var messageElement)
            || !messageElement.TryGetProperty("content", out var messageContentElement)
            || messageContentElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("DeepSeek response did not include assistant message content.");
        }

        var finishReason = firstChoice.TryGetProperty("finish_reason", out var finishReasonElement)
            && finishReasonElement.ValueKind == JsonValueKind.String
            ? finishReasonElement.GetString()
            : null;

        var usage = ReadUsageOrDefault(root);

        return new ModelProviderResponse<DeepSeekChatCompletionResult>(
            payload: new DeepSeekChatCompletionResult(
                Content: messageContentElement.GetString()!,
                FinishReason: finishReason),
            providerId: "deepseek",
            modelId: modelId,
            usage: usage,
            correlationId: request.CorrelationId);
    }

    private static ModelProviderUsage ReadUsageOrDefault(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usageElement)
            || usageElement.ValueKind != JsonValueKind.Object)
        {
            return new ModelProviderUsage(0, 0);
        }

        var inputTokens = usageElement.TryGetProperty("prompt_tokens", out var promptTokensElement)
            && promptTokensElement.ValueKind == JsonValueKind.Number
            && promptTokensElement.TryGetInt32(out var promptTokens)
                ? promptTokens
                : 0;

        var outputTokens = usageElement.TryGetProperty("completion_tokens", out var completionTokensElement)
            && completionTokensElement.ValueKind == JsonValueKind.Number
            && completionTokensElement.TryGetInt32(out var completionTokens)
                ? completionTokens
                : 0;

        return new ModelProviderUsage(inputTokens, outputTokens);
    }

    private static bool IsTransient(HttpStatusCode statusCode)
        => TransientStatusCodes.Contains(statusCode);

    private sealed record DeepSeekChatCompletionHttpRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<DeepSeekChatMessagePayload> Messages);

    private sealed record DeepSeekChatMessagePayload(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);
}