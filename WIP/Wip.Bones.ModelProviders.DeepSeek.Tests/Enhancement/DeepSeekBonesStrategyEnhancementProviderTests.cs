using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Bones.ModelProviders.DeepSeek;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Bones.ModelProviders.DeepSeek.Tests.Enhancement;

public sealed class DeepSeekBonesStrategyEnhancementProviderTests
{
    private const string ChecklistItem = BonesDeepSeekRequirementsChecklistItems.StrategyEnhancementProvider;
    private const string ScriptMappingChecklistItem = BonesDeepSeekRequirementsChecklistItems.DeepSeekScriptAuthoringHttpMapping;
    private const string ScriptStrategiesPromptCoverageItem = BonesDeepSeekRequirementsChecklistItems.ScriptStrategiesDeepSeekPromptCoverage;
    private const string NegativePathChecklistItem = BonesDeepSeekRequirementsChecklistItems.NegativePathIsolationTests;

    private const string PriorScriptSource = """
        using Wip.Bones.Domain;
        using Wip.Bones.Engine;

        public sealed class SeatStrategy : IBonesPlayerSlot
        {
            public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
            {
                return legalMoves[0];
            }
        }
        """;

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    [Trait("ChecklistItem", ScriptMappingChecklistItem)]
    [Trait("ChecklistItem", ScriptStrategiesPromptCoverageItem)]
    public async Task DeepSeekBonesStrategyEnhancementProvider_GivenScriptEnhancementRequest_ExpectedHttpPayloadIncludesPriorCSharpSource()
    {
        string? capturedBody = null;
        var enhancementRequest = CreateScriptEnhancementRequest(correlationId: "corr-enhance-http-map-1");

        var provider = CreateProvider(
            handler: async (request, cancellationToken) =>
            {
                capturedBody = await request.Content!.ReadAsStringAsync(cancellationToken);

                return CreateSuccessResponse(
                    model: "deepseek-chat",
                    content: CreateEnhancedScriptResponse(),
                    promptTokens: 120,
                    completionTokens: 80);
            });

        await provider.ExecuteAsync(
            enhancementRequest,
            CreateContext(),
            CancellationToken.None);

        Assert.NotNull(capturedBody);
        using var payloadDocument = JsonDocument.Parse(capturedBody!);
        var root = payloadDocument.RootElement;
        Assert.Equal("deepseek-chat", root.GetProperty("model").GetString());

        var messages = root.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());

        var systemPrompt = messages[0].GetProperty("content").GetString();
        var userContent = messages[1].GetProperty("content").GetString();
        Assert.NotNull(systemPrompt);
        Assert.NotNull(userContent);

        Assert.Contains(BonesStrategyScriptApiReference.InterfaceName, systemPrompt, StringComparison.Ordinal);
        Assert.Contains(BonesStrategyScriptApiReference.ContractHeader, userContent, StringComparison.Ordinal);
        Assert.Contains(BonesStrategyScriptApiReference.InterfaceName, userContent, StringComparison.Ordinal);
        Assert.Contains(BonesEnhancePromptBuilder.PriorScriptHeader, userContent, StringComparison.Ordinal);
        Assert.Contains(PriorScriptSource, userContent, StringComparison.Ordinal);
        Assert.Contains(BonesEnhancePromptBuilder.MatchOutcomeSummaryHeader, userContent, StringComparison.Ordinal);
        Assert.Contains("Outcome: Win", userContent, StringComparison.Ordinal);
        Assert.Equal(
            enhancementRequest.Payload.Messages.Single(message => message.Role == "user").Content,
            userContent);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task DeepSeekBonesStrategyEnhancementProvider_GivenSuccessfulCompletion_ExpectedReturnsEnhancedScriptWithProviderIdDeepSeek()
    {
        const string enhancedScript = """
            ```csharp
            using Wip.Bones.Domain;
            using Wip.Bones.Engine;

            public sealed class SeatStrategy : IBonesPlayerSlot
            {
                public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
                {
                    return legalMoves[^1];
                }
            }
            ```
            """;

        var provider = CreateProvider(
            handler: (_, _) => Task.FromResult(
                CreateSuccessResponse(
                    model: "deepseek-chat",
                    content: enhancedScript,
                    promptTokens: 140,
                    completionTokens: 60)));

        var response = await provider.ExecuteAsync(
            CreateScriptEnhancementRequest(correlationId: "corr-enhance-success-1"),
            CreateContext(),
            CancellationToken.None);

        Assert.Equal("deepseek", response.ProviderId);
        Assert.Equal("deepseek-chat", response.ModelId);
        Assert.Equal(enhancedScript, response.Payload.Markdown);
        Assert.Contains("IBonesPlayerSlot", response.Payload.Markdown, StringComparison.Ordinal);
        Assert.NotNull(response.Usage);
        Assert.Equal(140, response.Usage!.InputTokens);
        Assert.Equal(60, response.Usage.OutputTokens);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    [Trait("ChecklistItem", NegativePathChecklistItem)]
    public async Task DeepSeekBonesStrategyEnhancementProvider_GivenMalformedEmptyChoices_ExpectedDeterministicParseFailure()
    {
        var provider = CreateProvider(
            handler: (_, _) => Task.FromResult(CreateEmptyChoicesResponse()));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ExecuteAsync(
                CreateScriptEnhancementRequest(correlationId: "corr-enhance-empty-choices"),
                CreateContext(),
                CancellationToken.None)
            .AsTask());

        Assert.Contains("choices", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ModelProviderRequest<BonesStrategyEnhancementRequest> CreateScriptEnhancementRequest(string correlationId)
    {
        var playerId = new BonesPlayerId(1);
        var priorStrategy = new BonesStrategyDocument(
            new BonesStrategyId("strategy-seat-1-v1"),
            playerId,
            PriorScriptSource);

        var matchHistories = new[]
        {
            new BonesMatchHistoryTranscript(
                new BonesGameId("played-match"),
                playerId,
                """
                # Bones Match History
                Match winner seat: 1
                Final score for seat 1: 42
                Final score for seat 2: 18
                """),
        };

        var request = BonesEnhancePromptBuilder.BuildEnhancementRequest(
            playerId,
            priorStrategy,
            matchHistories,
            ["bones-player-1-match-played-match"]);

        return new ModelProviderRequest<BonesStrategyEnhancementRequest>(
            payload: request,
            modelId: "bones-strategy-enhance",
            correlationId: correlationId);
    }

    private static string CreateEnhancedScriptResponse()
        => """
            ```csharp
            using Wip.Bones.Domain;
            using Wip.Bones.Engine;

            public sealed class SeatStrategy : IBonesPlayerSlot
            {
                public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
                {
                    return legalMoves[^1];
                }
            }
            ```
            """;

    private static CapabilityContext CreateContext()
        => new(new SessionId("session-bones-strategy-enhancement"), "C:/repo/.wip/worktrees/bones-1");

    private const string TestApiKeyEnvVar = "WIP_BONES_DEEPSEEK_TEST_API_KEY";

    private static DeepSeekBonesStrategyEnhancementProvider CreateProvider(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        string? apiKeySource = null,
        string model = "deepseek-chat",
        bool configureApiKey = true)
    {
        var resolvedApiKeySource = apiKeySource ?? $"env:{TestApiKeyEnvVar}";

        if (configureApiKey && resolvedApiKeySource == $"env:{TestApiKeyEnvVar}")
            Environment.SetEnvironmentVariable(TestApiKeyEnvVar, "test-api-key");

        var bonesOptions = BonesDeepSeekProviderOptions.Create(
            baseUrl: "https://api.deepseek.com/v1/",
            model: model,
            timeout: TimeSpan.FromSeconds(30),
            apiKeySource: resolvedApiKeySource);

        var deepSeekOptions = new DeepSeekModelProviderOptions(
            baseUrl: bonesOptions.BaseUrl,
            apiKey: bonesOptions.ApiKeySource,
            timeout: bonesOptions.Timeout);

        var messageHandler = new DelegatingTestHttpMessageHandler(handler);
        var client = new HttpClient(messageHandler)
        {
            BaseAddress = deepSeekOptions.BaseUrl,
            Timeout = Timeout.InfiniteTimeSpan,
        };

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var deepSeekProvider = new DeepSeekModelProvider(client, deepSeekOptions);
        return new DeepSeekBonesStrategyEnhancementProvider(deepSeekProvider, bonesOptions);
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
            id = "chatcmpl-bones-enhance",
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

    private static HttpResponseMessage CreateEmptyChoicesResponse()
    {
        var payload = new
        {
            id = "chatcmpl-bones-enhance-empty",
            @object = "chat.completion",
            model = "deepseek-chat",
            choices = Array.Empty<object>(),
            usage = new
            {
                prompt_tokens = 10,
                completion_tokens = 0,
                total_tokens = 10,
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
