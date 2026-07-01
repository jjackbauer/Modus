using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Play;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Bones.ModelProviders.DeepSeek;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Bones.ModelProviders.DeepSeek.Tests.HttpMapping;

public sealed class BonesDeepSeekHttpMappingProofTests
{
    private const string ChecklistItem = BonesDeepSeekRequirementsChecklistItems.HttpMappingProofTests;
    private const string ScriptMappingChecklistItem = BonesDeepSeekRequirementsChecklistItems.DeepSeekScriptAuthoringHttpMapping;
    private const string TestApiKeyEnvVar = "WIP_BONES_DEEPSEEK_HTTP_MAPPING_TEST_API_KEY";
    private const string TestApiKeyValue = "mapping-test-api-key";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    [Trait("ChecklistItem", ScriptMappingChecklistItem)]
    public async Task BonesDeepSeekHttpMappingProof_GivenAllThreeAdapterRequests_ExpectedBearerAuthConfiguredModelPromptsAndCorrelationContinuity()
    {
        Environment.SetEnvironmentVariable(TestApiKeyEnvVar, TestApiKeyValue);
        try
        {
            var capturedRequests = new List<CapturedRequest>();

            var authoringProvider = CreateAuthoringProvider(
                (request, _) =>
                {
                    capturedRequests.Add(CaptureRequest("ponder", request));
                    return Task.FromResult(CreateSuccessResponse("deepseek-chat", CreateFencedScriptResponse(), 10, 5));
                });

            var playProvider = CreatePlayProvider(
                (request, _) =>
                {
                    capturedRequests.Add(CaptureRequest("play", request));
                    return Task.FromResult(CreateSuccessResponse("deepseek-chat", "move-play-left", 12, 2));
                });

            var enhancementProvider = CreateEnhancementProvider(
                (request, _) =>
                {
                    capturedRequests.Add(CaptureRequest("enhance", request));
                    return Task.FromResult(CreateSuccessResponse("deepseek-chat", CreateEnhancedScriptResponse(), 14, 6));
                });

            var ponderResponse = await authoringProvider.ExecuteAsync(
                CreateAuthoringRequest("corr-ponder-http-proof"),
                CreateContext("session-ponder-http-proof"),
                CancellationToken.None);

            var playResponse = await playProvider.ExecuteAsync(
                CreatePlayRequest("corr-play-http-proof"),
                CreateContext("session-play-http-proof"),
                CancellationToken.None);

            var enhanceResponse = await enhancementProvider.ExecuteAsync(
                CreateEnhancementRequest("corr-enhance-http-proof"),
                CreateContext("session-enhance-http-proof"),
                CancellationToken.None);

            Assert.Equal(3, capturedRequests.Count);

            foreach (var captured in capturedRequests)
            {
                Assert.NotNull(captured.Authorization);
                Assert.Equal("Bearer", captured.Authorization!.Scheme);
                Assert.Equal(TestApiKeyValue, captured.Authorization.Parameter);
                Assert.Equal("deepseek-chat", captured.Model);
            }

            var ponderCapture = Assert.Single(capturedRequests, request => request.Stage == "ponder");
            Assert.Contains("Strategy script authoring request", ponderCapture.UserContent, StringComparison.Ordinal);
            Assert.Contains(BonesStrategyScriptApiReference.ContractHeader, ponderCapture.UserContent, StringComparison.Ordinal);
            Assert.Contains(BonesStrategyScriptApiReference.InterfaceName, ponderCapture.UserContent, StringComparison.Ordinal);
            Assert.Equal("corr-ponder-http-proof", ponderResponse.CorrelationId);

            var playCapture = Assert.Single(capturedRequests, request => request.Stage == "play");
            Assert.Contains("Allowed moves", playCapture.UserContent, StringComparison.Ordinal);
            Assert.Contains("move-play-left", playCapture.UserContent, StringComparison.Ordinal);
            Assert.Equal("corr-play-http-proof", playResponse.CorrelationId);

            var enhanceCapture = Assert.Single(capturedRequests, request => request.Stage == "enhance");
            Assert.Contains("Strategy script enhancement request", enhanceCapture.UserContent, StringComparison.Ordinal);
            Assert.Contains(BonesStrategyScriptApiReference.ContractHeader, enhanceCapture.UserContent, StringComparison.Ordinal);
            Assert.Contains(BonesStrategyScriptApiReference.InterfaceName, enhanceCapture.UserContent, StringComparison.Ordinal);
            Assert.Contains(BonesEnhancePromptBuilder.PriorScriptHeader, enhanceCapture.UserContent, StringComparison.Ordinal);
            Assert.Contains("Match outcome summary", enhanceCapture.UserContent, StringComparison.Ordinal);
            Assert.Equal("corr-enhance-http-proof", enhanceResponse.CorrelationId);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TestApiKeyEnvVar, null);
        }
    }

    private sealed record CapturedRequest(
        string Stage,
        AuthenticationHeaderValue? Authorization,
        string Model,
        string UserContent);

    private static CapturedRequest CaptureRequest(string stage, HttpRequestMessage request)
    {
        var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var messages = root.GetProperty("messages");
        var userContent = messages[1].GetProperty("content").GetString() ?? string.Empty;

        return new CapturedRequest(
            stage,
            request.Headers.Authorization,
            root.GetProperty("model").GetString() ?? string.Empty,
            userContent);
    }

    private static ModelProviderRequest<BonesStrategyAuthoringRequest> CreateAuthoringRequest(string correlationId)
    {
        var playerId = new BonesPlayerId(1);
        var payload = BonesPonderPromptBuilder.BuildAuthoringRequest(
            playerId,
            [new BonesObservationTranscript(new BonesGameId("game-1"), playerId, "# Observation\nSeat 1 opening lead.")],
            ["bones-player-1-observation-game-1"]);

        return new ModelProviderRequest<BonesStrategyAuthoringRequest>(
            payload: payload,
            modelId: "deepseek-chat",
            correlationId: correlationId);
    }

    private static ModelProviderRequest<BonesPlayTurnRequest> CreatePlayRequest(string correlationId)
        => new(
            payload: new BonesPlayTurnRequest(
                Messages:
                [
                    new BonesPlayTurnMessage("system", "Choose exactly one allowed move id."),
                    new BonesPlayTurnMessage("user", "# Play turn request\n\n# Allowed moves\n- move-play-left (move id: move-play-left)\n- move-pass (move id: move-pass)"),
                ],
                AllowedMoves:
                [
                    new BonesPlayTurnOption(new BonesMoveId("move-play-left"), "Play 3-5 on Left (move id: move-play-left)"),
                    new BonesPlayTurnOption(new BonesMoveId("move-pass"), "pass (move id: move-pass)"),
                ],
                ActiveSeat: new BonesPlayerId(2),
                StrategyId: new BonesStrategyId("strategy-1")),
            modelId: "deepseek-chat",
            correlationId: correlationId);

    private static ModelProviderRequest<BonesStrategyEnhancementRequest> CreateEnhancementRequest(string correlationId)
    {
        var playerId = new BonesPlayerId(1);
        const string priorScript = """
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

        var payload = BonesEnhancePromptBuilder.BuildEnhancementRequest(
            playerId,
            new BonesStrategyDocument(new BonesStrategyId("strategy-seat-1-v1"), playerId, priorScript),
            [new BonesMatchHistoryTranscript(
                new BonesGameId("played-match"),
                playerId,
                """
                # Bones Match History
                Match winner seat: 1
                Final score for seat 1: 40
                Final score for seat 2: 12
                """)],
            ["bones-player-1-match-played-match"]);

        return new ModelProviderRequest<BonesStrategyEnhancementRequest>(
            payload: payload,
            modelId: "deepseek-chat",
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

    private static CapabilityContext CreateContext(string sessionId)
        => new(new SessionId(sessionId), "C:/repo/.wip/worktrees/bones-http-proof");

    private static DeepSeekBonesStrategyAuthoringProvider CreateAuthoringProvider(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        => new(CreateDeepSeekProvider(handler), CreateOptions());

    private static DeepSeekBonesPlayTurnProvider CreatePlayProvider(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        => new(CreateDeepSeekProvider(handler), CreateOptions());

    private static DeepSeekBonesStrategyEnhancementProvider CreateEnhancementProvider(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        => new(CreateDeepSeekProvider(handler), CreateOptions());

    private static DeepSeekModelProvider CreateDeepSeekProvider(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        var options = new DeepSeekModelProviderOptions(
            baseUrl: new Uri("https://api.deepseek.com/v1/"),
            apiKey: $"env:{TestApiKeyEnvVar}",
            timeout: TimeSpan.FromSeconds(30));

        var client = new HttpClient(new DelegatingTestHttpMessageHandler(handler))
        {
            BaseAddress = options.BaseUrl,
            Timeout = Timeout.InfiniteTimeSpan,
        };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return new DeepSeekModelProvider(client, options);
    }

    private static BonesDeepSeekProviderOptions CreateOptions()
        => BonesDeepSeekProviderOptions.Create(
            baseUrl: "https://api.deepseek.com/v1/",
            model: "deepseek-chat",
            timeout: TimeSpan.FromSeconds(30),
            apiKeySource: $"env:{TestApiKeyEnvVar}");

    private static HttpResponseMessage CreateSuccessResponse(
        string model,
        string content,
        int promptTokens,
        int completionTokens)
    {
        var payload = new
        {
            id = "chatcmpl-bones-http-proof",
            @object = "chat.completion",
            model,
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new { role = "assistant", content },
                    finish_reason = "stop",
                },
            },
            usage = new
            {
                prompt_tokens = promptTokens,
                completion_tokens = completionTokens,
                total_tokens = promptTokens + completionTokens,
            },
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
    }

    private sealed class DelegatingTestHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }
}
