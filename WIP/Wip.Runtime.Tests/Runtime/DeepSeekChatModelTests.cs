using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Identifiers;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Runtime.Tests.Runtime;

public sealed class DeepSeekChatModelTests
{
    private const string ChecklistItem = "DeepSeek IChatModel adapter maps chat requests/responses and SSE streaming without leaking vendor types into the agent layer [WP-3.5]";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task CompleteAsync_GivenChatRequestWithTools_ExpectedMappedHttpRequestAndTypedResponse()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var model = CreateModel(
            handler: async (request, cancellationToken) =>
            {
                capturedRequest = request;
                capturedBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return JsonResponse(
                    """
                    {
                      "id": "chatcmpl-1",
                      "model": "deepseek-chat",
                      "choices": [
                        {
                          "index": 0,
                          "message": { "role": "assistant", "content": "Done." },
                          "finish_reason": "stop"
                        }
                      ],
                      "usage": { "prompt_tokens": 7, "completion_tokens": 3, "total_tokens": 10 }
                    }
                    """);
            });

        var request = new ChatRequest(
            modelId: "deepseek-chat",
            messages:
            [
                ChatMessage.System("You are a repository agent."),
                ChatMessage.User("Read the README."),
            ],
            tools:
            [
                new ToolDefinition(
                    "tool.repo.read_file",
                    "Reads a file inside the worktree.",
                    JsonSerializer.SerializeToElement(new { type = "object", properties = new { path = new { type = "string" } }, required = new[] { "path" } })),
            ],
            maxTokens: 256,
            temperature: 0.1,
            correlationId: "corr-complete-1");

        var response = await model.CompleteAsync(
            request,
            new CapabilityContext(new SessionId("session-chat-complete"), TestRepositoryPaths.Worktree("chat-complete")),
            CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("https://api.deepseek.com/chat/completions", capturedRequest.RequestUri!.ToString());
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization!.Scheme);
        Assert.Equal("test-api-key", capturedRequest.Headers.Authorization.Parameter);
        Assert.Contains(capturedRequest.Headers.Accept, static header => header.MediaType == "application/json");

        Assert.NotNull(capturedBody);
        using var document = JsonDocument.Parse(capturedBody!);
        var root = document.RootElement;
        Assert.Equal("deepseek-chat", root.GetProperty("model").GetString());
        Assert.Equal(256, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal(0.1, root.GetProperty("temperature").GetDouble(), precision: 5);
        Assert.False(root.TryGetProperty("stream", out _));
        Assert.False(root.TryGetProperty("thinking", out _));

        var messages = root.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("You are a repository agent.", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());

        var tool = root.GetProperty("tools")[0];
        Assert.Equal("function", tool.GetProperty("type").GetString());
        // DeepSeek only accepts ^[a-zA-Z0-9_-]+$ function names; the dotted WIP id is sanitized on
        // the wire and restored on tool-call responses.
        Assert.Equal("tool_repo_read_file", tool.GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("object", tool.GetProperty("function").GetProperty("parameters").GetProperty("type").GetString());

        Assert.Equal("Done.", response.Message.Content);
        Assert.Equal(ChatRole.Assistant, response.Message.Role);
        Assert.Equal("deepseek", response.ProviderId);
        Assert.Equal("deepseek-chat", response.ModelId);
        Assert.Equal("corr-complete-1", response.CorrelationId);
        Assert.NotNull(response.Usage);
        Assert.Equal(7, response.Usage!.InputTokens);
        Assert.Equal(3, response.Usage.OutputTokens);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task CompleteAsync_GivenToolCallResponse_ExpectedParsedToolCallArgumentsAndToolMessageMapping()
    {
        string? capturedBody = null;
        var model = CreateModel(
            handler: async (request, cancellationToken) =>
            {
                capturedBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return JsonResponse(
                    """
                    {
                      "model": "deepseek-chat",
                      "choices": [
                        {
                          "index": 0,
                          "message": {
                            "role": "assistant",
                            "content": null,
                            "tool_calls": [
                              {
                                "id": "call-1",
                                "type": "function",
                                "function": {
                                  "name": "tool_repo_write_file",
                                  "arguments": "{\"path\":\"docs/note.md\",\"content\":\"hello\"}"
                                }
                              }
                            ]
                          },
                          "finish_reason": "tool_calls"
                        }
                      ]
                    }
                    """);
            });

        var priorCall = new ToolCall(
            "call-0",
            "tool.repo.read_file",
            JsonSerializer.SerializeToElement(new { path = "README.md" }));

        var response = await model.CompleteAsync(
            new ChatRequest(
                "deepseek-chat",
                [
                    ChatMessage.User("Update the note."),
                    ChatMessage.Assistant("Reading first.", [priorCall]),
                    ChatMessage.Tool("call-0", "README contents"),
                ],
                tools:
                [
                    new ToolDefinition(
                        "tool.repo.write_file",
                        "Writes a file inside the worktree.",
                        JsonSerializer.SerializeToElement(new { type = "object" })),
                ]),
            new CapabilityContext(new SessionId("session-chat-tools"), TestRepositoryPaths.Worktree("chat-tools")),
            CancellationToken.None);

        using var requestDocument = JsonDocument.Parse(capturedBody!);
        var messages = requestDocument.RootElement.GetProperty("messages");
        var assistant = messages[1];
        Assert.Equal("assistant", assistant.GetProperty("role").GetString());
        Assert.Equal("call-0", assistant.GetProperty("tool_calls")[0].GetProperty("id").GetString());
        Assert.Equal(
            "tool_repo_read_file",
            assistant.GetProperty("tool_calls")[0].GetProperty("function").GetProperty("name").GetString());
        Assert.Equal(
            "call-0",
            messages[2].GetProperty("tool_call_id").GetString());

        Assert.Equal(string.Empty, response.Message.Content);
        var toolCall = Assert.Single(response.Message.ToolCalls);
        Assert.Equal("call-1", toolCall.Id);
        Assert.Equal("tool.repo.write_file", toolCall.Name);
        Assert.Equal("docs/note.md", toolCall.Arguments.GetProperty("path").GetString());
        Assert.Equal("hello", toolCall.Arguments.GetProperty("content").GetString());
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task CompleteAsync_GivenTransientStatus_ExpectedBoundedRetryThenDeterministicFailure()
    {
        var calls = 0;
        var model = CreateModel(
            maxTransientRetries: 2,
            handler: (_, _) =>
            {
                calls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            });

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            model.CompleteAsync(
                new ChatRequest("deepseek-chat", [ChatMessage.User("retry")]),
                new CapabilityContext(new SessionId("session-chat-retry"), TestRepositoryPaths.Worktree("chat-retry")),
                CancellationToken.None).AsTask());

        Assert.Equal(3, calls);
        Assert.Equal(
            "DeepSeek chat request failed with transient status code 503 (ServiceUnavailable) after 3 attempts.",
            exception.Message);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task CompleteAsync_GivenCancellation_ExpectedRequestAbortedWithoutContinuation()
    {
        var calls = 0;
        var completed = 0;
        var model = CreateModel(
            handler: async (_, cancellationToken) =>
            {
                calls++;
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                Interlocked.Increment(ref completed);
                return JsonResponse("{}");
            });

        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(25));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            model.CompleteAsync(
                new ChatRequest("deepseek-chat", [ChatMessage.User("cancel")]),
                new CapabilityContext(new SessionId("session-chat-cancel"), TestRepositoryPaths.Worktree("chat-cancel")),
                cancellation.Token).AsTask());

        Assert.Equal(1, calls);
        Assert.Equal(0, completed);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task StreamAsync_GivenSseDeltasAndUsage_ExpectedDeltaChunksAndFinalChunk()
    {
        string? capturedBody = null;
        HttpRequestMessage? capturedRequest = null;
        var model = CreateModel(
            handler: async (request, cancellationToken) =>
            {
                capturedRequest = request;
                capturedBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return SseResponse(
                    """
                    data: {"model":"deepseek-chat","choices":[{"index":0,"delta":{"content":"Hel"}}]}
                    """,
                    """
                    data: {"model":"deepseek-chat","choices":[{"index":0,"delta":{"content":"lo"}}]}
                    """,
                    """
                    data: {"model":"deepseek-chat","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}
                    """,
                    """
                    data: {"model":"deepseek-chat","choices":[],"usage":{"prompt_tokens":5,"completion_tokens":2}}
                    """,
                    "data: [DONE]");
            });

        var chunks = await CollectAsync(model.StreamAsync(
            new ChatRequest(
                "deepseek-chat",
                [ChatMessage.User("Say hello.")],
                maxTokens: 16),
            new CapabilityContext(new SessionId("session-chat-stream"), TestRepositoryPaths.Worktree("chat-stream")),
            CancellationToken.None));

        using var requestDocument = JsonDocument.Parse(capturedBody!);
        Assert.True(requestDocument.RootElement.GetProperty("stream").GetBoolean());
        Assert.True(requestDocument.RootElement.GetProperty("stream_options").GetProperty("include_usage").GetBoolean());
        Assert.Contains(
            capturedRequest!.Headers.Accept,
            static header => header.MediaType == "text/event-stream");

        Assert.Equal(["Hel", "lo"], chunks.Where(static chunk => !chunk.IsFinal).Select(static chunk => chunk.Delta));
        var final = Assert.Single(chunks.Where(static chunk => chunk.IsFinal));
        Assert.Equal(string.Empty, final.Delta);
        Assert.NotNull(final.Usage);
        Assert.Equal(5, final.Usage!.InputTokens);
        Assert.Equal(2, final.Usage.OutputTokens);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task StreamAsync_GivenSplitToolCallDeltas_ExpectedSingleAssembledToolCall()
    {
        var model = CreateModel(
            handler: (_, _) => Task.FromResult(SseResponse(
                """
                data: {"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"call-1","type":"function","function":{"name":"tool_repo_write_file","arguments":"{\"path\":\"docs/note.md\","}}]}}]}
                """,
                """
                data: {"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"content\":\"hello\"}"}}]}}]}
                """,
                "data: [DONE]")));

        var chunks = await CollectAsync(model.StreamAsync(
            new ChatRequest(
                "deepseek-chat",
                [ChatMessage.User("Write the note.")],
                tools:
                [
                    new ToolDefinition(
                        "tool.repo.write_file",
                        "Writes a file inside the worktree.",
                        JsonSerializer.SerializeToElement(new { type = "object" })),
                ]),
            new CapabilityContext(new SessionId("session-chat-stream-tools"), TestRepositoryPaths.Worktree("chat-stream-tools")),
            CancellationToken.None));

        var toolCallChunk = Assert.Single(chunks.Where(static chunk => chunk.ToolCall is not null));
        var toolCall = toolCallChunk.ToolCall!;
        Assert.Equal("call-1", toolCall.Id);
        Assert.Equal("tool.repo.write_file", toolCall.Name);
        Assert.Equal("docs/note.md", toolCall.Arguments.GetProperty("path").GetString());
        Assert.Equal("hello", toolCall.Arguments.GetProperty("content").GetString());
        Assert.Single(chunks.Where(static chunk => chunk.IsFinal));
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task CompleteAsync_GivenCollidingSanitizedToolNames_ExpectedDeterministicDistinctWireNamesAndRestoredCalls()
    {
        string? capturedBody = null;
        var model = CreateModel(
            handler: async (request, cancellationToken) =>
            {
                capturedBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return JsonResponse(
                    """
                    {
                      "model": "deepseek-chat",
                      "choices": [
                        {
                          "index": 0,
                          "message": {
                            "role": "assistant",
                            "content": null,
                            "tool_calls": [
                              { "id": "call-1", "type": "function", "function": { "name": "a_b_2", "arguments": "{}" } }
                            ]
                          },
                          "finish_reason": "tool_calls"
                        }
                      ]
                    }
                    """);
            });

        var response = await model.CompleteAsync(
            new ChatRequest(
                "deepseek-chat",
                [ChatMessage.User("Use a tool.")],
                tools:
                [
                    new ToolDefinition("a.b", "Dotted name.", JsonSerializer.SerializeToElement(new { type = "object" })),
                    new ToolDefinition("a_b", "Underscored name.", JsonSerializer.SerializeToElement(new { type = "object" })),
                ]),
            new CapabilityContext(new SessionId("session-chat-tool-collision"), TestRepositoryPaths.Worktree("chat-tool-collision")),
            CancellationToken.None);

        using var document = JsonDocument.Parse(capturedBody!);
        var wireNames = document.RootElement.GetProperty("tools")
            .EnumerateArray()
            .Select(static tool => tool.GetProperty("function").GetProperty("name").GetString())
            .ToArray();
        Assert.Equal(["a_b", "a_b_2"], wireNames);

        var toolCall = Assert.Single(response.Message.ToolCalls);
        Assert.Equal("a_b", toolCall.Name);
        Assert.Equal("call-1", toolCall.Id);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task CompleteAsync_GivenEnvironmentApiKeySource_ExpectedResolvedBearerToken()
    {
        const string variableName = "WIP_TEST_DEEPSEEK_CHAT_KEY";
        var previous = Environment.GetEnvironmentVariable(variableName);
        Environment.SetEnvironmentVariable(variableName, "resolved-key");

        try
        {
            HttpRequestMessage? capturedRequest = null;
            var model = CreateModel(
                apiKey: $"env:{variableName}",
                handler: (request, _) =>
                {
                    capturedRequest = request;
                    return Task.FromResult(JsonResponse(
                        """
                        {
                          "model": "deepseek-chat",
                          "choices": [{ "index": 0, "message": { "role": "assistant", "content": "ok" } }]
                        }
                        """));
                });

            await model.CompleteAsync(
                new ChatRequest("deepseek-chat", [ChatMessage.User("key source")]),
                new CapabilityContext(new SessionId("session-chat-key"), TestRepositoryPaths.Worktree("chat-key")),
                CancellationToken.None);

            Assert.Equal("resolved-key", capturedRequest!.Headers.Authorization!.Parameter);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, previous);
        }
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task CompleteAsync_GivenDisabledThinking_ExpectedThinkingPayloadOnTheWire()
    {
        string? capturedBody = null;
        var model = CreateModel(
            thinking: DeepSeekThinkingMode.Disabled,
            handler: async (request, cancellationToken) =>
            {
                capturedBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return JsonResponse(
                    """
                    {
                      "model": "deepseek-flash",
                      "choices": [{ "index": 0, "message": { "role": "assistant", "content": "ok" } }]
                    }
                    """);
            });

        await model.CompleteAsync(
            new ChatRequest("deepseek-flash", [ChatMessage.User("hi")]),
            new CapabilityContext(new SessionId("session-chat-thinking"), TestRepositoryPaths.Worktree("chat-thinking")),
            CancellationToken.None);

        using var document = JsonDocument.Parse(capturedBody!);
        Assert.Equal("disabled", document.RootElement.GetProperty("thinking").GetProperty("type").GetString());
    }

    private static DeepSeekChatModel CreateModel(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        int maxTransientRetries = 2,
        string apiKey = "test-api-key",
        DeepSeekThinkingMode thinking = DeepSeekThinkingMode.Default)
    {
        var options = new DeepSeekModelProviderOptions(
            baseUrl: new Uri("https://api.deepseek.com/", UriKind.Absolute),
            apiKey: apiKey,
            timeout: TimeSpan.FromSeconds(30),
            maxTransientRetries: maxTransientRetries);

        var client = new HttpClient(new DelegatingTestHttpMessageHandler(handler))
        {
            BaseAddress = options.BaseUrl,
            Timeout = Timeout.InfiniteTimeSpan,
        };

        return new DeepSeekChatModel(client, options, thinking);
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage SseResponse(params string[] frames)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                string.Join("\n\n", frames) + "\n\n",
                Encoding.UTF8,
                "text/event-stream"),
        };

    private static async Task<List<StreamingChunk>> CollectAsync(IAsyncEnumerable<StreamingChunk> stream)
    {
        var chunks = new List<StreamingChunk>();
        await foreach (var chunk in stream)
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    private sealed class DelegatingTestHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }
}
