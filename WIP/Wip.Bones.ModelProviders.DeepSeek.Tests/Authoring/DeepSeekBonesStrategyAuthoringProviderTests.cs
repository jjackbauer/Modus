using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Bones.ModelProviders.DeepSeek;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Bones.ModelProviders.DeepSeek.Tests.Authoring;

public sealed class DeepSeekBonesStrategyAuthoringProviderTests
{
    private const string ChecklistItem = BonesDeepSeekRequirementsChecklistItems.StrategyAuthoringProvider;
    private const string ScriptMappingChecklistItem = BonesDeepSeekRequirementsChecklistItems.DeepSeekScriptAuthoringHttpMapping;
    private const string ScriptStrategiesPromptCoverageItem = BonesDeepSeekRequirementsChecklistItems.ScriptStrategiesDeepSeekPromptCoverage;
    private const string NegativePathChecklistItem = BonesDeepSeekRequirementsChecklistItems.NegativePathIsolationTests;

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    [Trait("ChecklistItem", ScriptMappingChecklistItem)]
    [Trait("ChecklistItem", ScriptStrategiesPromptCoverageItem)]
    [Trait("ChecklistItem", BonesDeepSeekRequirementsChecklistItems.LearningLoopForbiddenMembers)]
    public async Task DeepSeekBonesStrategyAuthoringProvider_GivenScriptAuthoringRequest_ExpectedHttpPayloadContainsIbonesPlayerSlotRequirement()
    {
        string? capturedBody = null;
        var authoringRequest = CreateScriptAuthoringRequest(correlationId: "corr-http-map-script-1");

        var provider = CreateProvider(
            handler: async (request, cancellationToken) =>
            {
                capturedBody = await request.Content!.ReadAsStringAsync(cancellationToken);

                return CreateSuccessResponse(
                    model: "deepseek-chat",
                    content: CreateFencedScriptResponse(),
                    promptTokens: 10,
                    completionTokens: 20);
            });

        await provider.ExecuteAsync(
            authoringRequest,
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
        Assert.Contains(BonesPonderPromptBuilder.ScriptOutputFormatHeader, userContent, StringComparison.Ordinal);
        Assert.Contains("Forbidden (these members do not exist):", userContent, StringComparison.Ordinal);
        Assert.Contains("BonesBoardEnd.LowPip", userContent, StringComparison.Ordinal);
        Assert.Equal(
            authoringRequest.Payload.Messages.Single(message => message.Role == "user").Content,
            userContent);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task DeepSeekBonesStrategyAuthoringProvider_GivenSuccessfulCompletion_ExpectedReturnsSourceForParser()
    {
        const string scriptResponse = """
            ```csharp
            using Wip.Bones.Domain;
            using Wip.Bones.Engine;

            public sealed class SeatStrategy : IBonesPlayerSlot
            {
                public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
                {
                    return legalMoves[0];
                }
            }
            ```
            """;

        var provider = CreateProvider(
            handler: (_, _) => Task.FromResult(
                CreateSuccessResponse(
                    model: "deepseek-chat",
                    content: scriptResponse,
                    promptTokens: 55,
                    completionTokens: 33)));

        var response = await provider.ExecuteAsync(
            CreateScriptAuthoringRequest(correlationId: "corr-success-1"),
            CreateContext(),
            CancellationToken.None);

        Assert.Equal("deepseek", response.ProviderId);
        Assert.Equal("deepseek-chat", response.ModelId);
        Assert.Equal(scriptResponse, response.Payload.Markdown);
        Assert.Contains("IBonesPlayerSlot", response.Payload.Markdown, StringComparison.Ordinal);
        Assert.NotNull(response.Usage);
        Assert.Equal(55, response.Usage!.InputTokens);
        Assert.Equal(33, response.Usage.OutputTokens);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task DeepSeekBonesStrategyAuthoringProvider_GivenCorrelationId_ExpectedPreservedOnResponse()
    {
        var provider = CreateProvider(
            handler: (_, _) => Task.FromResult(
                CreateSuccessResponse(
                    model: "deepseek-chat",
                    content: CreateFencedScriptResponse(),
                    promptTokens: 8,
                    completionTokens: 12)));

        var response = await provider.ExecuteAsync(
            CreateScriptAuthoringRequest(correlationId: "corr-ponder-seat-1-session-abc"),
            CreateContext(),
            CancellationToken.None);

        Assert.Equal("corr-ponder-seat-1-session-abc", response.CorrelationId);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    [Trait("ChecklistItem", NegativePathChecklistItem)]
    public async Task DeepSeekBonesStrategyAuthoringProvider_GivenMissingApiKey_ExpectedDeterministicFailureWithoutArtifactWrite()
    {
        const string envVar = "WIP_BONES_DEEPSEEK_MISSING_KEY_TEST";
        var previous = Environment.GetEnvironmentVariable(envVar);

        try
        {
            Environment.SetEnvironmentVariable(envVar, null);

            var httpCalls = 0;
            var provider = CreateProvider(
                handler: (_, _) =>
                {
                    httpCalls++;
                    return Task.FromResult(
                        CreateSuccessResponse(
                            model: "deepseek-chat",
                            content: "should not run",
                            promptTokens: 1,
                            completionTokens: 1));
                },
                apiKeySource: $"env:{envVar}",
                configureApiKey: false);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                provider.ExecuteAsync(
                    CreateScriptAuthoringRequest(correlationId: "corr-missing-key"),
                    CreateContext(),
                    CancellationToken.None)
                .AsTask());

            Assert.Contains(envVar, exception.Message, StringComparison.Ordinal);
            Assert.Equal(0, httpCalls);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, previous);
        }
    }

    private static ModelProviderRequest<BonesStrategyAuthoringRequest> CreateScriptAuthoringRequest(string correlationId)
    {
        var playerId = new BonesPlayerId(1);
        var observations = new[]
        {
            new BonesObservationTranscript(
                new BonesGameId("game-1"),
                playerId,
                "# Observation\nSeat 1 watched open ends shift from 6-4 to 5-4."),
        };

        var request = BonesPonderPromptBuilder.BuildAuthoringRequest(
            playerId,
            observations,
            ["bones-player-1-observation-game-1"]);

        return new ModelProviderRequest<BonesStrategyAuthoringRequest>(
            payload: request,
            modelId: "bones-strategy-author",
            correlationId: correlationId);
    }

    private static string CreateFencedScriptResponse()
        => """
            ```csharp
            using Wip.Bones.Domain;
            using Wip.Bones.Engine;

            public sealed class SeatStrategy : IBonesPlayerSlot
            {
                public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
                {
                    return legalMoves[0];
                }
            }
            ```
            """;

    private static CapabilityContext CreateContext()
        => new(new SessionId("session-bones-strategy-authoring"), "C:/repo/.wip/worktrees/bones-1");

    private const string TestApiKeyEnvVar = "WIP_BONES_DEEPSEEK_TEST_API_KEY";

    private static DeepSeekBonesStrategyAuthoringProvider CreateProvider(
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
        return new DeepSeekBonesStrategyAuthoringProvider(deepSeekProvider, bonesOptions);
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
            id = "chatcmpl-bones-strategy",
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
