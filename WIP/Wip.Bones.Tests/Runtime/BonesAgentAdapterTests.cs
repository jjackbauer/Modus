using System.Text.Json;
using Wip.Abstractions.Agents;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Agents.Runtime;
using Xunit;

namespace Wip.Bones.Tests.Runtime;

public sealed class BonesAgentAdapterTests
{
    private static readonly SessionId Session = new("session-42");

    [Fact]
    public async Task ExecuteAsync_GivenCompletedExecution_ForwardsFactoryRequestAndReturnsMappedResult()
    {
        // Arrange
        var context = CreateContext("worktree-alpha");
        using var cancellation = new CancellationTokenSource();

        var tool = new ToolDefinition(
            "bones.observe",
            "Loads observation transcripts for a seat.",
            JsonSerializer.SerializeToElement(new { type = "object" }));

        var options = new AgentLoopOptions
        {
            MaxSteps = 5,
            MaxTotalTokens = 4096,
            Timeout = TimeSpan.FromSeconds(30),
            MaxConsecutiveToolFailures = 2,
        };

        var request = new TestRequest("author a script strategy for seat 3");
        var executor = new FakeAgentExecutor(CompletedResult());

        TestRequest? factoryRequest = null;
        CapabilityContext? factoryContext = null;
        AgentExecutionRequest? builtRequest = null;

        var adapter = new BonesAgentAdapter<TestRequest, TestResult>(
            executor,
            (receivedRequest, receivedContext) =>
            {
                factoryRequest = receivedRequest;
                factoryContext = receivedContext;
                builtRequest = new AgentExecutionRequest(
                    Session,
                    "author a script strategy for seat 3",
                    "bones-test-model",
                    "You are the Bones ponder agent.",
                    new[] { tool },
                    options: options);
                return builtRequest;
            },
            (_, _, receivedResult, _) => ValueTask.FromResult(new TestResult(
                receivedResult.FinalMessage!.Content,
                receivedResult.Steps.Count,
                receivedResult.Usage)));

        // Act
        var result = await adapter.ExecuteAsync(request, context, cancellation.Token);

        // Assert
        Assert.Same(request, factoryRequest);
        Assert.Same(context, factoryContext);
        Assert.Equal(1, executor.CallCount);
        Assert.Same(builtRequest, executor.ReceivedRequest);

        var forwarded = Assert.IsType<AgentExecutionRequest>(executor.ReceivedRequest);
        Assert.Equal("session-42", forwarded.SessionId.Value);
        Assert.Equal("author a script strategy for seat 3", forwarded.Task);
        Assert.Equal("bones-test-model", forwarded.ModelId);
        Assert.Equal("You are the Bones ponder agent.", forwarded.SystemPrompt);

        var forwardedTool = Assert.Single(forwarded.Tools);
        Assert.Equal("bones.observe", forwardedTool.Name);
        Assert.Equal("Loads observation transcripts for a seat.", forwardedTool.Description);
        Assert.Equal("object", forwardedTool.ParametersSchema.GetProperty("type").GetString());

        Assert.Equal(5, forwarded.Options.MaxSteps);
        Assert.Equal(4096, forwarded.Options.MaxTotalTokens);
        Assert.Equal(TimeSpan.FromSeconds(30), forwarded.Options.Timeout);
        Assert.Equal(2, forwarded.Options.MaxConsecutiveToolFailures);

        Assert.Same(context, executor.ReceivedContext);
        Assert.Equal(cancellation.Token, executor.ReceivedCancellationToken);
        Assert.Equal("final answer", result.Answer);
        Assert.Equal(0, result.StepCount);
        Assert.Null(result.Usage);
    }

    [Fact]
    public async Task ExecuteAsync_GivenCompletedExecution_PassesExecutorStepsAndUsageToMapper()
    {
        // Arrange
        var context = CreateContext("worktree-beta");
        using var cancellation = new CancellationTokenSource();
        var request = new TestRequest("map the execution");
        var usage = new ModelProviderUsage(inputTokens: 120, outputTokens: 45);
        var step = new AgentStep(
            2,
            ChatMessage.Assistant("intermediate answer"),
            new[] { ToolResult.Success("call-9", "observation") });
        var executorResult = CompletedResult(new[] { step }, usage);
        var executor = new FakeAgentExecutor(executorResult);

        TestRequest? mapperRequest = null;
        CapabilityContext? mapperContext = null;
        AgentExecutionResult? mapperResult = null;
        CancellationToken mapperToken = default;
        var mapperCalls = 0;

        var adapter = new BonesAgentAdapter<TestRequest, TestResult>(
            executor,
            (receivedRequest, receivedContext) => new AgentExecutionRequest(
                receivedContext.SessionId,
                receivedRequest.Task,
                "bones-test-model"),
            (receivedRequest, receivedContext, receivedResult, receivedToken) =>
            {
                mapperCalls++;
                mapperRequest = receivedRequest;
                mapperContext = receivedContext;
                mapperResult = receivedResult;
                mapperToken = receivedToken;
                return ValueTask.FromResult(new TestResult(
                    receivedResult.FinalMessage!.Content,
                    receivedResult.Steps.Count,
                    receivedResult.Usage));
            });

        // Act
        var result = await adapter.ExecuteAsync(request, context, cancellation.Token);

        // Assert
        Assert.Equal(1, mapperCalls);
        Assert.Same(request, mapperRequest);
        Assert.Same(context, mapperContext);
        Assert.Equal(cancellation.Token, mapperToken);

        var mapped = Assert.IsType<AgentExecutionResult>(mapperResult);
        Assert.Same(executorResult, mapped);
        Assert.Same(step, Assert.Single(mapped.Steps));
        Assert.Same(usage, mapped.Usage);
        Assert.Equal("final answer", result.Answer);
        Assert.Equal(1, result.StepCount);
        Assert.Same(usage, result.Usage);
    }

    [Theory]
    [InlineData(AgentExecutionStatus.Failed, null, "agent execution failed")]
    [InlineData(AgentExecutionStatus.Failed, "model exploded", "model exploded")]
    [InlineData(AgentExecutionStatus.MaxStepsReached, "step budget consumed", "agent execution stopped with status MaxStepsReached: step budget consumed")]
    [InlineData(AgentExecutionStatus.TokenBudgetExhausted, "token budget consumed", "agent execution stopped with status TokenBudgetExhausted: token budget consumed")]
    [InlineData(AgentExecutionStatus.ToolFailureLimitReached, "tool failed three times", "agent execution stopped with status ToolFailureLimitReached: tool failed three times")]
    [InlineData(AgentExecutionStatus.TimedOut, "agent loop timed out", "agent execution stopped with status TimedOut: agent loop timed out")]
    public async Task ExecuteAsync_GivenNonSuccessStatus_ThrowsDeterministicInvalidOperationException(
        AgentExecutionStatus status,
        string? error,
        string expectedMessage)
    {
        // Arrange
        var context = CreateContext("worktree-gamma");
        var step = CreateStep(3);
        var executorResult = new AgentExecutionResult(status, null, new[] { step }, null, error);
        var executor = new FakeAgentExecutor(executorResult);
        var mapperCalls = 0;

        var adapter = new BonesAgentAdapter<TestRequest, TestResult>(
            executor,
            (receivedRequest, receivedContext) => new AgentExecutionRequest(
                receivedContext.SessionId,
                receivedRequest.Task,
                "bones-test-model"),
            (_, _, _, _) =>
            {
                mapperCalls++;
                return ValueTask.FromResult(new TestResult("unreachable", 0, null));
            });

        // Act
        var exception = await Assert.ThrowsAsync<BonesAgentExecutionException>(
            () => adapter.ExecuteAsync(new TestRequest("fail the execution"), context, CancellationToken.None).AsTask());

        // Assert
        Assert.Equal(status, exception.Status);
        Assert.IsAssignableFrom<InvalidOperationException>(exception);
        Assert.Same(step, Assert.Single(exception.Steps));
        Assert.Equal(expectedMessage, exception.Message);
        Assert.Equal(0, mapperCalls);
        Assert.Equal(1, executor.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_GivenExecutorCancellation_PropagatesOperationCanceledException()
    {
        // Arrange
        var context = CreateContext("worktree-delta");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var executor = new FakeAgentExecutor(async (_, _, token) =>
        {
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            return CompletedResult();
        });
        var mapperCalls = 0;

        var adapter = new BonesAgentAdapter<TestRequest, TestResult>(
            executor,
            (receivedRequest, receivedContext) => new AgentExecutionRequest(
                receivedContext.SessionId,
                receivedRequest.Task,
                "bones-test-model"),
            (_, _, _, _) =>
            {
                mapperCalls++;
                return ValueTask.FromResult(new TestResult("unreachable", 0, null));
            });

        // Act
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => adapter.ExecuteAsync(new TestRequest("cancel the execution"), context, cancellation.Token).AsTask());

        // Assert
        Assert.IsType<OperationCanceledException>(exception);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(0, mapperCalls);
        Assert.Equal(1, executor.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_GivenNullRequest_ThrowsArgumentNullExceptionWithoutCallingCollaborators()
    {
        // Arrange
        var context = CreateContext("worktree-epsilon");
        var executor = new FakeAgentExecutor(CompletedResult());
        var factoryCalls = 0;
        var adapter = new BonesAgentAdapter<TestRequest, TestResult>(
            executor,
            (_, receivedContext) =>
            {
                factoryCalls++;
                return new AgentExecutionRequest(receivedContext.SessionId, "task", "bones-test-model");
            },
            (_, _, _, _) => ValueTask.FromResult(new TestResult("unreachable", 0, null)));

        // Act
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => adapter.ExecuteAsync(null!, context, CancellationToken.None).AsTask());

        // Assert
        Assert.Equal("request", exception.ParamName);
        Assert.Equal(0, factoryCalls);
        Assert.Equal(0, executor.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_GivenNullContext_ThrowsArgumentNullExceptionWithoutCallingCollaborators()
    {
        // Arrange
        var executor = new FakeAgentExecutor(CompletedResult());
        var factoryCalls = 0;
        var adapter = new BonesAgentAdapter<TestRequest, TestResult>(
            executor,
            (_, receivedContext) =>
            {
                factoryCalls++;
                return new AgentExecutionRequest(receivedContext.SessionId, "task", "bones-test-model");
            },
            (_, _, _, _) => ValueTask.FromResult(new TestResult("unreachable", 0, null)));

        // Act
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => adapter.ExecuteAsync(new TestRequest("task"), null!, CancellationToken.None).AsTask());

        // Assert
        Assert.Equal("context", exception.ParamName);
        Assert.Equal(0, factoryCalls);
        Assert.Equal(0, executor.CallCount);
    }

    [Fact]
    public void Constructor_GivenNullExecutor_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new BonesAgentAdapter<TestRequest, TestResult>(
                null!,
                (request, context) => new AgentExecutionRequest(context.SessionId, request.Task, "bones-test-model"),
                (_, _, _, _) => ValueTask.FromResult(new TestResult("value", 0, null))));

        Assert.Equal("executor", exception.ParamName);
    }

    [Fact]
    public void Constructor_GivenNullRequestFactory_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new BonesAgentAdapter<TestRequest, TestResult>(
                new FakeAgentExecutor(CompletedResult()),
                null!,
                (_, _, _, _) => ValueTask.FromResult(new TestResult("value", 0, null))));

        Assert.Equal("createRequest", exception.ParamName);
    }

    [Fact]
    public void Constructor_GivenNullResultMapper_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new BonesAgentAdapter<TestRequest, TestResult>(
                new FakeAgentExecutor(CompletedResult()),
                (request, context) => new AgentExecutionRequest(context.SessionId, request.Task, "bones-test-model"),
                null!));

        Assert.Equal("mapResult", exception.ParamName);
    }

    private static CapabilityContext CreateContext(string worktreePath) => new(Session, worktreePath);

    private static AgentExecutionResult CompletedResult(
        IReadOnlyList<AgentStep>? steps = null,
        ModelProviderUsage? usage = null)
        => new(
            AgentExecutionStatus.Completed,
            ChatMessage.Assistant("final answer"),
            steps ?? Array.Empty<AgentStep>(),
            usage,
            null);

    private static AgentStep CreateStep(int index)
        => new(
            index,
            ChatMessage.Assistant("step answer"),
            new[] { ToolResult.Success("call-1", "observation") });

    private sealed record TestRequest(string Task);

    private sealed record TestResult(string Answer, int StepCount, ModelProviderUsage? Usage);

    private sealed class FakeAgentExecutor : IAgentExecutor
    {
        private readonly Func<AgentExecutionRequest, CapabilityContext, CancellationToken, ValueTask<AgentExecutionResult>> _execute;

        public FakeAgentExecutor(AgentExecutionResult result)
            : this((_, _, _) => ValueTask.FromResult(result))
        {
        }

        public FakeAgentExecutor(
            Func<AgentExecutionRequest, CapabilityContext, CancellationToken, ValueTask<AgentExecutionResult>> execute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        }

        public int CallCount { get; private set; }

        public AgentExecutionRequest? ReceivedRequest { get; private set; }

        public CapabilityContext? ReceivedContext { get; private set; }

        public CancellationToken ReceivedCancellationToken { get; private set; }

        public ValueTask<AgentExecutionResult> ExecuteAsync(
            AgentExecutionRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            CallCount++;
            ReceivedRequest = request;
            ReceivedContext = context;
            ReceivedCancellationToken = cancellationToken;
            return _execute(request, context, cancellationToken);
        }
    }
}