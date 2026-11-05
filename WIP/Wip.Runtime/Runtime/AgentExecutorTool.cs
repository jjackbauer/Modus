using Wip.Abstractions.Agents;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Descriptors;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Tools;
using Microsoft.Extensions.DependencyInjection;
using Wip.Builder;

namespace Wip.Runtime.Runtime;

/// <summary>
/// Workflow-facing capability that runs the agent loop (WP-3.6). The host registers a chat model
/// and, optionally, a tool invoker/executor; the default composition uses the builder's tools.
/// </summary>
public sealed class AgentExecutorTool : ITool<AgentExecutorToolRequest, AgentExecutorToolResult>
{
    private readonly IServiceProvider _services;
    private readonly IReadOnlyCollection<ICapabilityDescriptor> _descriptors;

    public AgentExecutorTool(
        IServiceProvider services,
        IReadOnlyCollection<ICapabilityDescriptor> descriptors)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _descriptors = descriptors ?? throw new ArgumentNullException(nameof(descriptors));
    }

    public async ValueTask<AgentExecutorToolResult> ExecuteAsync(
        AgentExecutorToolRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var chatModel = _services.GetService(typeof(IChatModel)) as IChatModel
            ?? throw new InvalidOperationException("No IChatModel is registered for the agent executor tool.");

        var toolInvoker = _services.GetService(typeof(IToolInvoker)) as IToolInvoker
            ?? new RuntimeToolInvoker(_services, _descriptors);

        var executor = _services.GetService(typeof(IAgentExecutor)) as IAgentExecutor
            ?? new AgentExecutor(chatModel, toolInvoker);

        var result = await executor.ExecuteAsync(
            new AgentExecutionRequest(
                context.SessionId,
                request.Task,
                request.ModelId,
                request.SystemPrompt),
            context,
            cancellationToken);

        return new AgentExecutorToolResult(
            result.Succeeded,
            result.Status,
            result.FinalMessage?.Content,
            result.Steps.Count,
            result.Error);
    }
}

public static class AgentExecutorToolRegistration
{
    public static WipBuilder AddAgentExecutorTool(this WipBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<IReadOnlyCollection<ICapabilityDescriptor>>(builder.CapabilityDescriptors);
        builder.AddTool<AgentExecutorTool, AgentExecutorToolRequest, AgentExecutorToolResult>(
            new CapabilityId("tool.agent.run"),
            "Agent executor");

        return builder;
    }
}