using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Runtime.Tests.Runtime;

public sealed class DeepSeekModelProviderTests
{
    private const string ChecklistItem = "Implement DeepSeek model-provider capability with deterministic HTTP request/response mapping, cancellation propagation, and bounded retry for transient failures [depends on provider options and builder registration]";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task DeepSeekProvider_GivenChatCompletionRequest_ExpectedMappedHttpRequestContainsModelMessagesAndDeterministicHeaders()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var provider = CreateProvider(
            handler: async (request, cancellationToken) =>
            {
                capturedRequest = request;
                capturedBody = await request.Content!.ReadAsStringAsync(cancellationToken);

                return CreateSuccessResponse(
                    model: "deepseek-chat",
                    content: "ok",
                    promptTokens: 3,
                    completionTokens: 5);
            });

        var response = await provider.ExecuteAsync(
            new ModelProviderRequest<DeepSeekChatCompletionRequest>(
                payload: new DeepSeekChatCompletionRequest(
                    Messages:
                    [
                        new DeepSeekChatMessage("system", "You are deterministic."),
                        new DeepSeekChatMessage("user", "Generate a short plan.")
                    ]),
                modelId: "deepseek-chat",
                correlationId: "corr-map-1"),
            new CapabilityContext(new SessionId("session-deepseek-map"), "C:/repo/.wip/worktrees/s1"),
            CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("https://api.deepseek.com/chat/completions", capturedRequest.RequestUri!.ToString());
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization!.Scheme);
        Assert.Equal("test-api-key", capturedRequest.Headers.Authorization.Parameter);
        Assert.Contains(capturedRequest.Headers.Accept, static h => h.MediaType == "application/json");

        Assert.NotNull(capturedBody);
        using var payloadDocument = JsonDocument.Parse(capturedBody!);
        var root = payloadDocument.RootElement;
        Assert.Equal("deepseek-chat", root.GetProperty("model").GetString());

        var messages = root.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("You are deterministic.", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("Generate a short plan.", messages[1].GetProperty("content").GetString());

        Assert.Equal("deepseek", response.ProviderId);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task DeepSeekProvider_GivenSuccessfulResponse_ExpectedTypedCompletionPayloadAndUsageSemantics()
    {
        var provider = CreateProvider(
            handler: (request, cancellationToken) => Task.FromResult(
                CreateSuccessResponse(
                    model: "deepseek-reasoner",
                    content: "1. Inspect\n2. Change\n3. Validate",
                    promptTokens: 42,
                    completionTokens: 15,
                    finishReason: "stop")));

        var response = await provider.ExecuteAsync(
            new ModelProviderRequest<DeepSeekChatCompletionRequest>(
                payload: new DeepSeekChatCompletionRequest(
                    Messages: [new DeepSeekChatMessage("user", "Build a plan")]),
                modelId: "deepseek-reasoner",
                correlationId: "corr-success-1"),
            new CapabilityContext(new SessionId("session-deepseek-success"), "C:/repo/.wip/worktrees/s2"),
            CancellationToken.None);

        Assert.Equal("deepseek", response.ProviderId);
        Assert.Equal("deepseek-reasoner", response.ModelId);
        Assert.Equal("corr-success-1", response.CorrelationId);
        Assert.NotNull(response.Usage);
        Assert.Equal(42, response.Usage!.InputTokens);
        Assert.Equal(15, response.Usage.OutputTokens);
        Assert.Equal("1. Inspect\n2. Change\n3. Validate", response.Payload.Content);
        Assert.Equal("stop", response.Payload.FinishReason);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task DeepSeekProvider_GivenTransientHttpFailure_ExpectedBoundedRetryThenDeterministicFailureContract()
    {
        var calls = 0;

        var provider = CreateProvider(
            maxTransientRetries: 2,
            handler: (request, cancellationToken) =>
            {
                calls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("{\"error\":\"temporary outage\"}", Encoding.UTF8, "application/json")
                });
            });

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.ExecuteAsync(
                new ModelProviderRequest<DeepSeekChatCompletionRequest>(
                    payload: new DeepSeekChatCompletionRequest(
                        Messages: [new DeepSeekChatMessage("user", "retry me")]),
                    modelId: "deepseek-chat",
                    correlationId: "corr-retry-1"),
                new CapabilityContext(new SessionId("session-deepseek-retry"), "C:/repo/.wip/worktrees/s3"),
                CancellationToken.None)
            .AsTask());

        Assert.Equal(3, calls);
        Assert.Equal(
            "DeepSeek request failed with transient status code 503 (ServiceUnavailable) after 3 attempts.",
            exception.Message);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task DeepSeekProvider_GivenCancellationRequested_ExpectedRequestAbortedWithoutBackgroundContinuation()
    {
        var calls = 0;
        var completed = 0;

        var provider = CreateProvider(handler: async (request, cancellationToken) =>
        {
            calls++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            Interlocked.Increment(ref completed);

            return CreateSuccessResponse(
                model: "deepseek-chat",
                content: "should not complete",
                promptTokens: 1,
                completionTokens: 1);
        });

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(25));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.ExecuteAsync(
                new ModelProviderRequest<DeepSeekChatCompletionRequest>(
                    payload: new DeepSeekChatCompletionRequest(
                        Messages: [new DeepSeekChatMessage("user", "cancel")]),
                    modelId: "deepseek-chat",
                    correlationId: "corr-cancel-1"),
                new CapabilityContext(new SessionId("session-deepseek-cancel"), "C:/repo/.wip/worktrees/s4"),
                cts.Token)
            .AsTask());

        Assert.Equal(1, calls);
    Assert.Equal(0, completed);
    }

    private static DeepSeekModelProvider CreateProvider(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        int maxTransientRetries = 2)
    {
        var options = new DeepSeekModelProviderOptions(
            baseUrl: new Uri("https://api.deepseek.com/", UriKind.Absolute),
            apiKey: "test-api-key",
            timeout: TimeSpan.FromSeconds(30),
            maxTransientRetries: maxTransientRetries);

        var messageHandler = new DelegatingTestHttpMessageHandler(handler);
        var client = new HttpClient(messageHandler)
        {
            BaseAddress = options.BaseUrl,
            Timeout = Timeout.InfiniteTimeSpan
        };

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return new DeepSeekModelProvider(client, options);
    }

    private static HttpResponseMessage CreateSuccessResponse(
        string model,
        string content,
        int promptTokens,
        int completionTokens,
        string finishReason = "stop")
    {
                var payload = new
                {
                        id = "chatcmpl-test",
                        @object = "chat.completion",
                        model,
                        choices = new[]
                        {
                                new
                                {
                                        index = 0,
                                        message = new
                                        {
                                                role = "assistant",
                                                content,
                                        },
                                        finish_reason = finishReason,
                                }
                        },
                        usage = new
                        {
                                prompt_tokens = promptTokens,
                                completion_tokens = completionTokens,
                                total_tokens = promptTokens + completionTokens,
                        }
                };

                var json = JsonSerializer.Serialize(payload);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class DelegatingTestHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }
}