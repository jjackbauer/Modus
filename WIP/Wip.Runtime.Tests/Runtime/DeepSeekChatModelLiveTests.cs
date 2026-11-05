using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Identifiers;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Runtime.Tests.Runtime;

/// <summary>
/// Opt-in live contract test (WP-3.6). It performs a real network call only when
/// <c>DEEPSEEK_API_KEY</c> is set; without the key it returns immediately, so the offline gate and
/// CI never depend on the network or on credentials.
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

        var options = new DeepSeekModelProviderOptions(
            baseUrl: new Uri("https://api.deepseek.com/"),
            apiKey: apiKey,
            timeout: TimeSpan.FromSeconds(60));

        using var httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var model = new DeepSeekChatModel(httpClient, options);

        var response = await model.CompleteAsync(
            new ChatRequest(
                modelId: "deepseek-chat",
                messages: [ChatMessage.User("Reply with exactly one word: pong")],
                maxTokens: 16),
            new CapabilityContext(new SessionId("session-live-deepseek"), TestRepositoryPaths.Worktree("live")),
            CancellationToken.None);

        Assert.Equal("deepseek", response.ProviderId);
        Assert.Equal("deepseek-chat", response.ModelId);
        Assert.False(string.IsNullOrWhiteSpace(response.Message.Content));
    }
}
