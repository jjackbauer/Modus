using Wip.Abstractions.Agents;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Tools;

namespace Wip.Runtime.Runtime;

/// <summary>
/// Deterministic agent loop over <see cref="IChatModel"/> and <see cref="IToolInvoker"/> (WP-3.3).
/// The loop appends tool observations as tool messages and stops on a final answer, step budget,
/// token budget, repeated tool failures, timeout or caller cancellation.
/// </summary>
public sealed class AgentExecutor : IAgentExecutor, IStreamingAgentExecutor
{
    private readonly IChatModel _chatModel;
    private readonly IToolInvoker _toolInvoker;

    public AgentExecutor(IChatModel chatModel, IToolInvoker toolInvoker)
    {
        _chatModel = chatModel ?? throw new ArgumentNullException(nameof(chatModel));
        _toolInvoker = toolInvoker ?? throw new ArgumentNullException(nameof(toolInvoker));
    }

    public async ValueTask<AgentExecutionResult> ExecuteAsync(
        AgentExecutionRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var options = request.Options;
        var messages = new List<ChatMessage>();
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            messages.Add(ChatMessage.System(request.SystemPrompt));
        }

        messages.AddRange(request.InitialMessages);
        messages.Add(ChatMessage.User(request.Task));

        var steps = new List<AgentStep>();
        var totalTokens = 0;
        var consecutiveFailures = 0;

        using var loopTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (options.Timeout > TimeSpan.Zero)
        {
            loopTimeout.CancelAfter(options.Timeout);
        }

        try
        {
            for (var stepIndex = 0; stepIndex < Math.Max(1, options.MaxSteps); stepIndex++)
            {
                ChatResponse response;
                try
                {
                    response = await _chatModel.CompleteAsync(
                        new ChatRequest(request.ModelId, messages, request.Tools),
                        context,
                        loopTimeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return Result(AgentExecutionStatus.TimedOut, null, steps, totalTokens,
                        $"Agent loop exceeded {options.Timeout.TotalSeconds:0.###}s.");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return Result(AgentExecutionStatus.Failed, null, steps, totalTokens,
                        $"model-error: {ex.GetType().Name}: {ex.Message}");
                }

                totalTokens += (response.Usage?.InputTokens ?? 0) + (response.Usage?.OutputTokens ?? 0);
                messages.Add(response.Message);

                var toolCalls = response.Message.ToolCalls;
                if (toolCalls.Count == 0)
                {
                    steps.Add(new AgentStep(stepIndex, response.Message, Array.Empty<ToolResult>()));
                    return Result(AgentExecutionStatus.Completed, response.Message, steps, totalTokens, error: null);
                }

                var toolResults = new List<ToolResult>(toolCalls.Count);
                foreach (var call in toolCalls)
                {
                    var toolResult = await _toolInvoker.InvokeAsync(call, context, loopTimeout.Token);
                    toolResults.Add(toolResult);
                    messages.Add(ChatMessage.Tool(call.Id, toolResult.Content, toolResult.IsError));

                    if (toolResult.IsError)
                    {
                        consecutiveFailures++;
                    }
                    else
                    {
                        consecutiveFailures = 0;
                    }

                    if (consecutiveFailures >= Math.Max(1, options.MaxConsecutiveToolFailures))
                    {
                        steps.Add(new AgentStep(stepIndex, response.Message, toolResults));
                        return Result(
                            AgentExecutionStatus.ToolFailureLimitReached,
                            null,
                            steps,
                            totalTokens,
                            $"Tool failure limit ({options.MaxConsecutiveToolFailures}) reached.");
                    }
                }

                steps.Add(new AgentStep(stepIndex, response.Message, toolResults));

                if (options.MaxTotalTokens is > 0 && totalTokens >= options.MaxTotalTokens.Value)
                {
                    return Result(AgentExecutionStatus.TokenBudgetExhausted, null, steps, totalTokens,
                        $"Token budget ({options.MaxTotalTokens.Value}) exhausted.");
                }
            }

            return Result(AgentExecutionStatus.MaxStepsReached, null, steps, totalTokens,
                $"Max steps ({options.MaxSteps}) reached without a final answer.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    private static AgentExecutionResult Result(
        AgentExecutionStatus status,
        ChatMessage? finalMessage,
        IReadOnlyList<AgentStep> steps,
        int totalTokens,
        string? error)
        => new(
            status,
            finalMessage,
            steps.ToArray(),
            totalTokens > 0 ? new ModelProviderUsage(totalTokens, 0) : null,
            error);
    public async IAsyncEnumerable<AgentStreamEvent> ExecuteStreamingAsync(
        AgentExecutionRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var options = request.Options;
        var messages = new List<ChatMessage>();
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            messages.Add(ChatMessage.System(request.SystemPrompt));
        }

        messages.AddRange(request.InitialMessages);
        messages.Add(ChatMessage.User(request.Task));

        var steps = new List<AgentStep>();
        var totalTokens = 0;
        var consecutiveFailures = 0;

        using var loopTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (options.Timeout > TimeSpan.Zero)
        {
            loopTimeout.CancelAfter(options.Timeout);
        }

        for (var stepIndex = 0; stepIndex < Math.Max(1, options.MaxSteps); stepIndex++)
        {
            var content = new System.Text.StringBuilder();
            var toolCalls = new List<ToolCall>();
            var modelFailed = false;
            string? modelFailure = null;

            IAsyncEnumerator<StreamingChunk> enumerator;
            try
            {
                enumerator = _chatModel.StreamAsync(
                    new ChatRequest(request.ModelId, messages, request.Tools),
                    context,
                    loopTimeout.Token).GetAsyncEnumerator(loopTimeout.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                modelFailed = true;
                modelFailure = $"model-error: {ex.GetType().Name}: {ex.Message}";
                enumerator = EmptyChunks().GetAsyncEnumerator(loopTimeout.Token);
            }

            var streamTimedOut = false;
            if (modelFailure is null)
            {
                try
                {
                    while (true)
                    {
                        StreamingChunk chunk;
                        try
                        {
                            if (!await enumerator.MoveNextAsync())
                            {
                                break;
                            }

                            chunk = enumerator.Current;
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        {
                            streamTimedOut = true;
                            break;
                        }
                        catch (Exception ex)
                        {
                            modelFailed = true;
                            modelFailure = $"model-error: {ex.GetType().Name}: {ex.Message}";
                            break;
                        }

                        if (!string.IsNullOrEmpty(chunk.Delta))
                        {
                            content.Append(chunk.Delta);
                            yield return new AgentStreamEvent(AgentStreamEventKind.Delta, chunk.Delta);
                        }

                        if (chunk.ToolCall is not null)
                        {
                            toolCalls.Add(chunk.ToolCall);
                            yield return new AgentStreamEvent(AgentStreamEventKind.ToolCall, ToolCall: chunk.ToolCall);
                        }

                        totalTokens += (chunk.Usage?.InputTokens ?? 0) + (chunk.Usage?.OutputTokens ?? 0);
                    }
                }
                finally
                {
                    await enumerator.DisposeAsync();
                }
            }

            if (streamTimedOut)
            {
                yield return new AgentStreamEvent(
                    AgentStreamEventKind.Failed,
                    Content: "stream-timeout",
                    Result: Result(
                        AgentExecutionStatus.TimedOut,
                        null,
                        steps,
                        totalTokens,
                        $"Agent stream exceeded {options.Timeout.TotalSeconds:0.###}s."));
                yield break;
            }

            if (modelFailed)
            {
                yield return new AgentStreamEvent(
                    AgentStreamEventKind.Failed,
                    Content: modelFailure ?? "model-error",
                    Result: Result(AgentExecutionStatus.Failed, null, steps, totalTokens, modelFailure));
                yield break;
            }

            var assistantMessage = ChatMessage.Assistant(content.ToString(), toolCalls);
            messages.Add(assistantMessage);

            if (toolCalls.Count == 0)
            {
                steps.Add(new AgentStep(stepIndex, assistantMessage, Array.Empty<ToolResult>()));
                yield return new AgentStreamEvent(
                    AgentStreamEventKind.Completed,
                    assistantMessage.Content,
                    Result: Result(AgentExecutionStatus.Completed, assistantMessage, steps, totalTokens, error: null));
                yield break;
            }

            var toolResults = new List<ToolResult>(toolCalls.Count);
            foreach (var call in toolCalls)
            {
                var toolResult = await _toolInvoker.InvokeAsync(call, context, loopTimeout.Token);
                toolResults.Add(toolResult);
                messages.Add(ChatMessage.Tool(call.Id, toolResult.Content, toolResult.IsError));
                consecutiveFailures = toolResult.IsError ? consecutiveFailures + 1 : 0;

                yield return new AgentStreamEvent(AgentStreamEventKind.ToolResult, ToolResult: toolResult);

                if (consecutiveFailures >= Math.Max(1, options.MaxConsecutiveToolFailures))
                {
                    steps.Add(new AgentStep(stepIndex, assistantMessage, toolResults));
                    yield return new AgentStreamEvent(
                        AgentStreamEventKind.Failed,
                        Content: "tool-failure-limit",
                        Result: Result(
                            AgentExecutionStatus.ToolFailureLimitReached,
                            null,
                            steps,
                            totalTokens,
                            $"Tool failure limit ({options.MaxConsecutiveToolFailures}) reached."));
                    yield break;
                }
            }

            steps.Add(new AgentStep(stepIndex, assistantMessage, toolResults));

            if (options.MaxTotalTokens is > 0 && totalTokens >= options.MaxTotalTokens.Value)
            {
                yield return new AgentStreamEvent(
                    AgentStreamEventKind.Failed,
                    Content: "token-budget",
                    Result: Result(
                        AgentExecutionStatus.TokenBudgetExhausted,
                        null,
                        steps,
                        totalTokens,
                        $"Token budget ({options.MaxTotalTokens.Value}) exhausted."));
                yield break;
            }
        }

        yield return new AgentStreamEvent(
            AgentStreamEventKind.Failed,
            Content: "max-steps",
            Result: Result(
                AgentExecutionStatus.MaxStepsReached,
                null,
                steps,
                totalTokens,
                $"Max steps ({options.MaxSteps}) reached without a final answer."));
    }

    private static async IAsyncEnumerable<StreamingChunk> EmptyChunks()
    {
        await Task.CompletedTask;
        yield break;
    }
}