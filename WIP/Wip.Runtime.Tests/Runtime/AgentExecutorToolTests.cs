using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Agents;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Identifiers;
using Wip.Builder;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Runtime.Tests.Runtime;

public sealed class AgentExecutorToolTests
{
    [Fact]
    public async Task ExecuteAsync_GivenRegisteredChatModel_ExpectedAgentResult()
    {
        var services = new ServiceCollection();
        var builder = services.AddWipCapabilities();
        builder.AddAgentExecutorTool();
        services.AddSingleton<IChatModel>(new FinalAnswerChatModel("done"));

        await using var provider = services.BuildServiceProvider();
        var tool = provider.GetRequiredService<AgentExecutorTool>();

        var result = await tool.ExecuteAsync(
            new AgentExecutorToolRequest("answer", "model.test"),
            new CapabilityContext(new SessionId("session-agent-tool"), WorktreePath: Path.GetTempPath()),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("done", result.FinalAnswer);
        Assert.Equal(1, result.Steps);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_GivenNoChatModel_ExpectedDeterministicError()
    {
        var services = new ServiceCollection();
        var builder = services.AddWipCapabilities();
        builder.AddAgentExecutorTool();

        await using var provider = services.BuildServiceProvider();
        var tool = provider.GetRequiredService<AgentExecutorTool>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => tool.ExecuteAsync(
            new AgentExecutorToolRequest("answer", "model.test"),
            new CapabilityContext(new SessionId("session-agent-tool"), WorktreePath: Path.GetTempPath()),
            CancellationToken.None).AsTask());
    }

    private sealed class FinalAnswerChatModel(string answer) : IChatModel
    {
        public ValueTask<ChatResponse> CompleteAsync(
            ChatRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new ChatResponse(ChatMessage.Assistant(answer), "model.test", "test"));

        public IAsyncEnumerable<StreamingChunk> StreamAsync(
            ChatRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}