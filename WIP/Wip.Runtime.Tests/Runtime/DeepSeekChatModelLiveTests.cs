using System.Text.Json;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Identifiers;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Runtime.Tests.Runtime;

/// <summary>
/// Opt-in live contract tests (WP-3.6 / live-migration check). They perform real network calls only
/// when <c>DEEPSEEK_API_KEY</c> is set; without the key they return immediately, so the offline gate
/// and CI never depend on the network or on credentials.
/// </summary>
public sealed class DeepSeekChatModelLiveTests
{
    [Fact]
    [Trait("Category", "Live")]
    public async Task CompleteAsync_GivenLiveApiKey_ExpectedNonEmptyDeepSeekAnswer()
    {
        var apiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // Opt-in: absent credentials means no call and no failure in the offline gate.
            return;
        }

        using var httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var model = CreateLiveModel(httpClient, apiKey);

        var response = await model.CompleteAsync(
            new ChatRequest(
                modelId: "deepseek-flash",
                messages: [ChatMessage.User("Reply with exactly one word: pong")],
                maxTokens: 256),
            new CapabilityContext(new SessionId("session-live-deepseek"), TestRepositoryPaths.Worktree("live")),
            CancellationToken.None);

        Assert.Equal("deepseek", response.ProviderId);
        Assert.Equal("deepseek-flash", response.ModelId);
        Assert.False(string.IsNullOrWhiteSpace(response.Message.Content));
    }

    /// <summary>
    /// Regression guard for the live 400 found during the agent-runtime migration: DeepSeek rejects
    /// function names outside <c>^[a-zA-Z0-9_-]+$</c> (for example <c>tool.repo.read_file</c>). The
    /// adapter must sanitize the wire name and restore the canonical id on the tool call.
    /// </summary>
    [Fact]
    [Trait("Category", "Live")]
    public async Task CompleteAsync_GivenLiveApiKeyAndDottedToolName_ExpectedAcceptedRequestAndRestoredToolName()
    {
        var apiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        using var httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var model = CreateLiveModel(httpClient, apiKey);

        var response = await model.CompleteAsync(
            new ChatRequest(
                modelId: "deepseek-flash",
                messages:
                [
                    ChatMessage.User(
                        "You must call the tool with name tool.repo.read_file exactly once with path README.md, then reply DONE."),
                ],
                tools:
                [
                    new ToolDefinition(
                        "tool.repo.read_file",
                        "Reads a file inside the worktree.",
                        JsonSerializer.SerializeToElement(new
                        {
                            type = "object",
                            properties = new { path = new { type = "string" } },
                            required = new[] { "path" },
                            additionalProperties = false,
                        })),
                ]),
            new CapabilityContext(new SessionId("session-live-deepseek-tools"), TestRepositoryPaths.Worktree("live-tools")),
            CancellationToken.None);

        Assert.Equal("deepseek", response.ProviderId);
        if (response.Message.ToolCalls.Count > 0)
        {
            // The canonical dotted id must come back, never the sanitized wire name.
            Assert.Equal("tool.repo.read_file", response.Message.ToolCalls[0].Name);
        }
    }

    private static DeepSeekChatModel CreateLiveModel(HttpClient httpClient, string apiKey)
    {
        var options = new DeepSeekModelProviderOptions(
            baseUrl: new Uri("https://api.deepseek.com/"),
            apiKey: apiKey,
            timeout: TimeSpan.FromSeconds(90));

        return new DeepSeekChatModel(httpClient, options);
    }
}
