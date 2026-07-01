using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Host;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BonesHostDeepSeekIntegrationTests
{
    private const string IntegrationItem = BonesDeepSeekHostRequirementsChecklistItems.HostDeepSeekIntegration;
    private const string StubStrategyBoilerplate = "Prefer matching open ends before passing.";
    private const string MockStrategyMarker = "MOCK_LLM_PONDER_RULE_ALPHA";

    [Fact]
    [Trait("ChecklistItem", IntegrationItem)]
    public async Task BonesHostDeepSeekIntegration_GivenMockHandlerReturningDistinctStrategy_ExpectedPersistedStrategyDiffersFromStubBoilerplate()
    {
        Environment.SetEnvironmentVariable("WIP_BONES_HOST_TEST_API_KEY", "integration-test-api-key");
        var handler = new DeepSeekLearningLoopMockHandler();

        try
        {
            await using var factory = CreateDeepSeekFactory(handler);
            using var client = factory.CreateClient();
            var loopHost = factory.Services.GetRequiredService<BonesLearningLoopHost>();

            var sessionId = await WaitForCompletedIterationSessionIdAsync(client, loopHost, TimeSpan.FromMinutes(3));

            var artifactStore = factory.Services.GetRequiredService<IArtifactStore>();
            var knowledgeStore = new BonesPlayerKnowledgeStore(
                artifactStore,
                factory.DataDirectory,
                new SessionId(sessionId));

            var strategy = await knowledgeStore.LoadStrategy(new BonesPlayerId(1), new SessionId(sessionId));

            Assert.Contains(MockStrategyMarker, strategy.Markdown, StringComparison.Ordinal);
            Assert.DoesNotContain(StubStrategyBoilerplate, strategy.Markdown, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WIP_BONES_HOST_TEST_API_KEY", null);
        }
    }

    [Fact]
    [Trait("ChecklistItem", IntegrationItem)]
    public async Task BonesHostDeepSeekIntegration_GivenMockHandlerReturningPlayMoves_ExpectedMatchTranscriptReflectsMockedSelections()
    {
        Environment.SetEnvironmentVariable("WIP_BONES_HOST_TEST_API_KEY", "integration-test-api-key");
        var handler = new DeepSeekLearningLoopMockHandler();

        try
        {
            await using var factory = CreateDeepSeekFactory(handler);
            using var client = factory.CreateClient();
            var loopHost = factory.Services.GetRequiredService<BonesLearningLoopHost>();

            var sessionId = await WaitForCompletedIterationSessionIdAsync(client, loopHost, TimeSpan.FromMinutes(3));

            Assert.True(handler.PlayTurnResponses.Count > 0);
            Assert.Contains(
                handler.PlayTurnResponses,
                moveId => !string.IsNullOrWhiteSpace(moveId));

            var artifactStore = factory.Services.GetRequiredService<IArtifactStore>();
            var knowledgeStore = new BonesPlayerKnowledgeStore(
                artifactStore,
                factory.DataDirectory,
                new SessionId(sessionId));

            var transcripts = await knowledgeStore.LoadMatchHistoryTranscripts(
                new BonesPlayerId(1),
                CancellationToken.None);

            var transcript = Assert.Single(transcripts);
            Assert.Contains("played", transcript.Markdown, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(StubStrategyBoilerplate, transcript.Markdown, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WIP_BONES_HOST_TEST_API_KEY", null);
        }
    }

    private static async Task<string> WaitForCompletedIterationSessionIdAsync(
        HttpClient client,
        BonesLearningLoopHost loopHost,
        TimeSpan timeout)
    {
        string? capturedSessionId = null;
        var stopRequested = false;

        await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response =>
            {
                var current = loopHost.CurrentState.CurrentIteration;
                if (!stopRequested
                    && (string.Equals(current.StageName, "Running", StringComparison.Ordinal)
                        || string.Equals(current.StageName, "Complete", StringComparison.Ordinal))
                    && !string.IsNullOrWhiteSpace(current.SessionId))
                {
                    capturedSessionId = current.SessionId;
                    loopHost.RequestStop();
                    stopRequested = true;
                }

                return stopRequested && response.IterationCount >= 1;
            },
            timeout);

        return capturedSessionId
            ?? throw new InvalidOperationException("Completed iteration did not expose a session id.");
    }

    private static BonesHostWebApplicationFactory CreateDeepSeekFactory(HttpMessageHandler handler)
        => new(
            configureConfiguration: settings =>
            {
                settings["BonesHost:ModelProvider"] = "deepseek";
                settings["BonesHost:DeepSeek:BaseUrl"] = "https://api.deepseek.com";
                settings["BonesHost:DeepSeek:Model"] = "deepseek-chat";
                settings["BonesHost:DeepSeek:TimeoutSeconds"] = "30";
                settings["BonesHost:DeepSeek:ApiKeySource"] = "env:WIP_BONES_HOST_TEST_API_KEY";
                settings["BonesHost:DefaultParameters:GameCount"] = "1";
                settings["BonesHost:DefaultParameters:TargetScore"] = "8";
                settings["BonesHost:IterationDelayMs"] = "0";
            },
            httpMessageHandler: handler);

    internal sealed class DeepSeekLearningLoopMockHandler : HttpMessageHandler
    {
        internal const string MockStrategyMarker = "MOCK_LLM_PONDER_RULE_ALPHA";

        private static readonly Regex MoveIdPattern = new(
            @"\(move id:\s*(?<moveId>[^)]+)\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public List<string> PlayTurnResponses { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            using var document = JsonDocument.Parse(body);
            var messages = document.RootElement.GetProperty("messages");
            var userContent = messages.GetArrayLength() > 1
                ? messages[1].GetProperty("content").GetString() ?? string.Empty
                : string.Empty;

            string assistantContent;
            if (userContent.Contains("Strategy authoring request", StringComparison.Ordinal))
            {
                assistantContent = $"""
                    # Bones Strategy

                    ## Rules
                    - {MockStrategyMarker}
                    - Block scoring lines after reviewing observation transcripts.
                    """;
            }
            else if (userContent.Contains("Play turn request", StringComparison.Ordinal)
                || userContent.Contains("Allowed moves", StringComparison.Ordinal))
            {
                assistantContent = SelectMockPlayMove(userContent);
                PlayTurnResponses.Add(assistantContent);
            }
            else if (userContent.Contains("Strategy enhancement request", StringComparison.Ordinal))
            {
                assistantContent = $"""
                    # Bones Strategy

                    ## Rules
                    - {MockStrategyMarker}
                    - Refine blocking after reviewing match outcomes.

                    ## Enhancements
                    - Mocked DeepSeek enhancement applied.
                    """;
            }
            else
            {
                assistantContent = "pass";
            }

            var payload = new
            {
                id = "chatcmpl-bones-host-integration",
                @object = "chat.completion",
                model = "deepseek-chat",
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        message = new { role = "assistant", content = assistantContent },
                        finish_reason = "stop",
                    },
                },
                usage = new { prompt_tokens = 10, completion_tokens = 5, total_tokens = 15 },
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            });
        }

        private static string SelectMockPlayMove(string userContent)
        {
            var moveIds = MoveIdPattern
                .Matches(userContent)
                .Select(match => match.Groups["moveId"].Value.Trim())
                .Where(moveId => !string.IsNullOrWhiteSpace(moveId))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (moveIds.Length == 0)
                return "move-pass";

            return moveIds.Length == 1 ? moveIds[0] : moveIds[^1];
        }
    }
}