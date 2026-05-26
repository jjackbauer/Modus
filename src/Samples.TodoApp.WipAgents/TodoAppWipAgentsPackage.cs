using Modus.Core.Plugins;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Workflows;
using Wip.Builder;
using Wip.Modus.Hosting;

namespace Samples.TodoApp.WipAgents;

public static class TodoAppWipBuilderExtensions
{
    public static WipBuilder AddTodoAppWipAgents(this WipBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddAgent<TodoPlanAgent, TodoPlanRequest, TodoPlanResult>(new CapabilityId("todoapp.agent.plan"), "Todo Plan Agent");
        builder.AddValidator<TodoResultValidator, TodoValidationRequest, TodoValidationResult>(new CapabilityId("todoapp.validator.result"), "Todo Result Validator");
        builder.AddWorkflow<TodoAppWorkflow, TodoWorkflowRequest, TodoWorkflowResult>(new WorkflowId("todoapp.workflow.delivery"), "Todo Delivery Workflow");

        return builder;
    }
}

public sealed class TodoAppWipPlugin : IWipHostPluginContract, IPluginLifecycle, IPluginOperationCatalog, IPluginRegistrationPolicy
{
    public PluginId PluginId => new("samples.todoapp.wipagents");

    public ContractName ContractName => new("Samples.TodoApp.WipAgents");

    public Version ContractVersion => new(1, 0, 0);

    public IReadOnlyCollection<OperationName> SupportedOperations =>
    [
        new("todoapp.agent.plan"),
        new("todoapp.validator.result")
    ];

    public void Load(PluginLoadContext context)
    {
    }

    public void Start(PluginStartContext context)
    {
    }

    public void Stop(PluginStopContext context)
    {
    }

    public void Unload(PluginUnloadContext context)
    {
    }

    public IReadOnlyList<PluginRegistrationStep> BuildRegistrationPlan(IPluginContract plugin)
    {
        return
        [
            new PluginRegistrationStep(1, PluginRegistrationStepKind.RegisterOperation, "operation:todoapp.agent.plan"),
            new PluginRegistrationStep(2, PluginRegistrationStepKind.RegisterOperation, "operation:todoapp.validator.result"),
            new PluginRegistrationStep(3, PluginRegistrationStepKind.SubscribeEvents, "events:todoapp.workflow.delivery")
        ];
    }
}

public sealed record TodoPlanRequest(string Goal);

public sealed record TodoPlanResult(string Plan);

public sealed class TodoPlanAgent : IAgent<TodoPlanRequest, TodoPlanResult>
{
    public ValueTask<TodoPlanResult> ExecuteAsync(TodoPlanRequest request, CapabilityContext context, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(new TodoPlanResult($"Plan:{request.Goal}:{context.SessionId.Value}"));
    }
}

public sealed record TodoValidationRequest(string Candidate);

public sealed record TodoValidationResult(bool Passed, string Reason);

public sealed class TodoResultValidator : IValidator<TodoValidationRequest, TodoValidationResult>
{
    public ValueTask<TodoValidationResult> ExecuteAsync(TodoValidationRequest request, CapabilityContext context, CancellationToken cancellationToken)
    {
        var passed = !string.IsNullOrWhiteSpace(request.Candidate);
        return ValueTask.FromResult(new TodoValidationResult(passed, passed ? "ok" : "candidate is required"));
    }
}

public sealed record TodoWorkflowRequest(string Title);

public sealed record TodoWorkflowResult(string Summary);

public sealed class TodoAppWorkflow : IWorkflow<TodoWorkflowRequest, TodoWorkflowResult>
{
    public WorkflowId WorkflowId => new("todoapp.workflow.delivery");

    public ValueTask<TodoWorkflowResult> ExecuteAsync(TodoWorkflowRequest request, WorkflowContext context, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(new TodoWorkflowResult($"{request.Title}:{context.Task}"));
    }
}