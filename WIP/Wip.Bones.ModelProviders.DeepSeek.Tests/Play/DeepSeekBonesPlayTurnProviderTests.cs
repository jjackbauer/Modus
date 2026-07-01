using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Agents.Play;
using Wip.Bones.Identifiers;
using Wip.Bones.ModelProviders.DeepSeek;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Bones.ModelProviders.DeepSeek.Tests.Play;

public sealed class DeepSeekBonesPlayTurnProviderTests
{
    private const string ChecklistItem = BonesDeepSeekRequirementsChecklistItems.PlayTurnProvider;
    private const string NegativePathChecklistItem = BonesDeepSeekRequirementsChecklistItems.NegativePathIsolationTests;

    private static readonly BonesMoveId MovePlayLeft = new("move-play-left");
    private static readonly BonesMoveId MovePass = new("move-pass");

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task DeepSeekBonesPlayTurnProvider_GivenTurnRequest_ExpectedHttpPayloadListsAllowedMoveIds()
    {
        string? capturedBody = null;

        var provider = CreateProvider(
            handler: async (request, cancellationToken) =>
            {
                capturedBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return CreateSuccessResponse(
                    model: "deepseek-chat",
                    content: "move-play-left",
                    promptTokens: 12,
                    completionTokens: 2);
            });

        await provider.ExecuteAsync(
            CreatePlayTurnRequest(correlationId: "corr-http-map-1"),
            CreateContext(),
            CancellationToken.None);

        Assert.NotNull(capturedBody);
        using var payloadDocument = JsonDocument.Parse(capturedBody!);
        var root = payloadDocument.RootElement;
        Assert.Equal("deepseek-chat", root.GetProperty("model").GetString());

        var messages = root.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());

        var userContent = messages[1].GetProperty("content").GetString()!;
        Assert.Contains("move-play-left", userContent, StringComparison.Ordinal);
        Assert.Contains("move-pass", userContent, StringComparison.Ordinal);
        Assert.Contains("# Allowed moves", userContent, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task DeepSeekBonesPlayTurnProvider_GivenModelSelectsLegalMove_ExpectedReturnsBonesPlayTurnResultWithThatMoveId()
    {
        var provider = CreateProvider(
            handler: (_, _) => Task.FromResult(
                CreateSuccessResponse(
                    model: "deepseek-chat",
                    content: "move-play-left",
                    promptTokens: 18,
                    completionTokens: 3)));

        var response = await provider.ExecuteAsync(
            CreatePlayTurnRequest(correlationId: "corr-legal-move-1"),
            CreateContext(),
            CancellationToken.None);

        Assert.Equal("deepseek", response.ProviderId);
        Assert.Equal("deepseek-chat", response.ModelId);
        Assert.Equal("move-play-left", response.Payload.SelectedMoveId);
        Assert.NotNull(response.Usage);
        Assert.Equal(18, response.Usage!.InputTokens);
        Assert.Equal(3, response.Usage.OutputTokens);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    [Trait("ChecklistItem", NegativePathChecklistItem)]
    public async Task DeepSeekBonesPlayTurnProvider_GivenModelSelectsIllegalMove_ExpectedReturnsNullParsedMoveForToolRetry()
    {
        var provider = CreateProvider(
            handler: (_, _) => Task.FromResult(
                CreateSuccessResponse(
                    model: "deepseek-chat",
                    content: "move-illegal",
                    promptTokens: 20,
                    completionTokens: 4)));

        var response = await provider.ExecuteAsync(
            CreatePlayTurnRequest(correlationId: "corr-illegal-move-1"),
            CreateContext(),
            CancellationToken.None);

        Assert.True(string.IsNullOrWhiteSpace(response.Payload.SelectedMoveId));
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    [Trait("ChecklistItem", NegativePathChecklistItem)]
    public async Task DeepSeekBonesPlayTurnProvider_GivenEndpoint503_ExpectedDeterministicHttpFailure()
    {
        var calls = 0;

        var provider = CreateProvider(
            maxTransientRetries: 2,
            handler: (_, _) =>
            {
                calls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("{\"error\":\"temporary outage\"}", Encoding.UTF8, "application/json"),
                });
            });

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.ExecuteAsync(
                CreatePlayTurnRequest(correlationId: "corr-503-1"),
                CreateContext(),
                CancellationToken.None)
            .AsTask());

        Assert.Equal(3, calls);
        Assert.Equal(
            "DeepSeek request failed with transient status code 503 (ServiceUnavailable) after 3 attempts.",
            exception.Message);
    }

    private static ModelProviderRequest<BonesPlayTurnRequest> CreatePlayTurnRequest(string correlationId)
    {
        var allowedMoves = new[]
        {
            new BonesPlayTurnOption(MovePlayLeft, "play 3-5 on Left (move id: move-play-left)"),
            new BonesPlayTurnOption(MovePass, "pass (move id: move-pass)"),
        };

        return new(
            payload: new BonesPlayTurnRequest(
                Messages:
                [
                    new BonesPlayTurnMessage(
                        "system",
                        "Choose exactly one allowed move id from the enumerated options."),
                    new BonesPlayTurnMessage("user", BuildPlayTurnUserPrompt(allowedMoves)),
                ],
                AllowedMoves: allowedMoves,
                ActiveSeat: new BonesPlayerId(2),
                StrategyId: new BonesStrategyId("strategy-1")),
            modelId: "bones-play-turn",
            correlationId: correlationId);
    }

    private static string BuildPlayTurnUserPrompt(IReadOnlyList<BonesPlayTurnOption> allowedMoves)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Play turn request");
        builder.AppendLine();
        builder.AppendLine("# Allowed moves");
        builder.AppendLine("Select one move id from this enumerated list:");

        foreach (var option in allowedMoves)
            builder.AppendLine($"- {option.MoveId.Value}: {option.Description}");

        return builder.ToString();
    }

    private static CapabilityContext CreateContext()
        => new(new SessionId("session-bones-play-turn"), "C:/repo/.wip/worktrees/bones-play-1");

    private const string TestApiKeyEnvVar = "WIP_BONES_DEEPSEEK_TEST_API_KEY";

    private static DeepSeekBonesPlayTurnProvider CreateProvider(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        string model = "deepseek-chat",
        int maxTransientRetries = 2)
    {
        Environment.SetEnvironmentVariable(TestApiKeyEnvVar, "test-api-key");

        var bonesOptions = BonesDeepSeekProviderOptions.Create(
            baseUrl: "https://api.deepseek.com/v1/",
            model: model,
            timeout: TimeSpan.FromSeconds(30),
            apiKeySource: $"env:{TestApiKeyEnvVar}");

        var deepSeekOptions = new DeepSeekModelProviderOptions(
            baseUrl: bonesOptions.BaseUrl,
            apiKey: bonesOptions.ApiKeySource,
            timeout: bonesOptions.Timeout,
            maxTransientRetries: maxTransientRetries);

        var messageHandler = new DelegatingTestHttpMessageHandler(handler);
        var client = new HttpClient(messageHandler)
        {
            BaseAddress = deepSeekOptions.BaseUrl,
            Timeout = Timeout.InfiniteTimeSpan,
        };

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var deepSeekProvider = new DeepSeekModelProvider(client, deepSeekOptions);
        return new DeepSeekBonesPlayTurnProvider(deepSeekProvider, bonesOptions);
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
            id = "chatcmpl-bones-play-turn",
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
                },
            },
            usage = new
            {
                prompt_tokens = promptTokens,
                completion_tokens = completionTokens,
                total_tokens = promptTokens + completionTokens,
            },
        };

        var json = JsonSerializer.Serialize(payload);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class DelegatingTestHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }
}