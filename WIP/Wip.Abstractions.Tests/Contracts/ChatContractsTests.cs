using System.Text.Json;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Identifiers;
using Xunit;

namespace Wip.Abstractions.Tests.Contracts;

public sealed class ChatContractsTests
{
    [Fact]
    public void ChatMessage_GivenFactories_ExpectedRolesAndToolLinkage()
    {
        Assert.Equal(ChatRole.System, ChatMessage.System("rules").Role);
        Assert.Equal(ChatRole.User, ChatMessage.User("question").Role);
        Assert.Equal(ChatRole.Assistant, ChatMessage.Assistant("answer").Role);

        var tool = ChatMessage.Tool("call-1", "result", isError: true);
        Assert.Equal(ChatRole.Tool, tool.Role);
        Assert.Equal("call-1", tool.ToolCallId);
        Assert.Empty(tool.ToolCalls);
    }

    [Fact]
    public void ToolDefinition_GivenSchema_ExpectedCloneThatCannotBeMutatedByCaller()
    {
        using var document = JsonDocument.Parse("""{"type":"object","properties":{"path":{"type":"string"}}}""");
        var definition = new ToolDefinition("read_file", "Reads a file", document.RootElement);

        document.Dispose();

        Assert.Equal("read_file", definition.Name);
        Assert.Equal("object", definition.ParametersSchema.GetProperty("type").GetString());
    }

    [Fact]
    public void ToolCall_GivenArguments_ExpectedCloneSurvivesDocumentDisposal()
    {
        ToolCall call;
        using (var document = JsonDocument.Parse("""{"path":"README.md"}"""))
        {
            call = new ToolCall("call-1", "read_file", document.RootElement);
        }

        Assert.Equal("read_file", call.Name);
        Assert.Equal("README.md", call.Arguments.GetProperty("path").GetString());
    }

    [Fact]
    public void ToolResult_GivenFactories_ExpectedErrorFlag()
    {
        Assert.False(ToolResult.Success("call-1", "ok").IsError);
        Assert.True(ToolResult.Error("call-1", "boom").IsError);
    }

    [Fact]
    public void ChatRequest_GivenOptions_ExpectedValidatedSnapshot()
    {
        var messages = new List<ChatMessage> { ChatMessage.User("hi") };
        var request = new ChatRequest("deepseek-flash", messages, maxTokens: 128, temperature: 0.2);

        messages.Add(ChatMessage.Assistant("sneaky"));

        Assert.Single(request.Messages);
        Assert.Equal(128, request.MaxTokens);
        Assert.Equal(0.2, request.Temperature);
        Assert.Empty(request.Tools);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ChatRequest_GivenMissingModel_ExpectedArgumentError(string? modelId)
    {
        Assert.Throws<ArgumentException>(() => new ChatRequest(modelId!, [ChatMessage.User("hi")]));
    }

    [Fact]
    public void ChatRequest_GivenNoMessages_ExpectedArgumentError()
    {
        Assert.Throws<ArgumentException>(() => new ChatRequest("model", []));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void ChatRequest_GivenInvalidTemperature_ExpectedOutOfRange(double temperature)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChatRequest(
            "model",
            [ChatMessage.User("hi")],
            temperature: temperature));
    }

    [Fact]
    public void ChatResponseAndStreamingChunk_GivenValues_ExpectedExposed()
    {
        var usage = new ModelProviderUsage(10, 5);
        var response = new ChatResponse(
            ChatMessage.Assistant("ok"),
            modelId: "deepseek-flash",
            providerId: "deepseek",
            usage,
            correlationId: "corr-1");

        Assert.Equal("ok", response.Message.Content);
        Assert.Same(usage, response.Usage);

        var chunk = new StreamingChunk("partial", isFinal: true, usage: usage);
        Assert.Equal("partial", chunk.Delta);
        Assert.True(chunk.IsFinal);
        Assert.Null(chunk.ToolCall);
    }
}