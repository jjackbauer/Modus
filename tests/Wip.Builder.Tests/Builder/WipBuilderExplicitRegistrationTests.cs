using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Policies;
using Wip.Abstractions.Workflows;
using Xunit;

namespace Wip.Builder.Tests.Builder;

public sealed class WipBuilderExplicitRegistrationTests
{
    [Fact]
    public void AddAgentTAgentTRequestTResult_GivenUniqueId_RegistersResolvableTypedCapability()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);

        builder.AddAgent<PlanAgent, PlanRequest, PlanResult>(
            capabilityId: new CapabilityId("agent.plan"),
            displayName: "Plan agent");

        var provider = services.BuildServiceProvider();
        var capability = provider.GetService<PlanAgent>();
        var descriptor = Assert.Single(builder.CapabilityDescriptors);

        Assert.NotNull(capability);
        Assert.Equal(typeof(PlanRequest), descriptor.RequestType);
        Assert.Equal(typeof(PlanResult), descriptor.ResultType);
        Assert.Equal(typeof(PlanAgent), descriptor.CapabilityType);
        Assert.Equal(CapabilityKind.Agent, descriptor.Kind);
        Assert.Equal("agent.plan", descriptor.CapabilityId.Value);
    }

    [Fact]
    public void AddToolTToolTRequestTResult_GivenUniqueId_RegistersResolvableTypedCapability()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);

        builder.AddTool<EchoTool, ToolRequest, ToolResult>(
            capabilityId: new CapabilityId("tool.echo"),
            displayName: "Echo tool");

        var provider = services.BuildServiceProvider();
        var capability = provider.GetService<EchoTool>();
        var descriptor = Assert.Single(builder.CapabilityDescriptors);

        Assert.NotNull(capability);
        Assert.Equal(typeof(ToolRequest), descriptor.RequestType);
        Assert.Equal(typeof(ToolResult), descriptor.ResultType);
        Assert.Equal(typeof(EchoTool), descriptor.CapabilityType);
        Assert.Equal(CapabilityKind.Tool, descriptor.Kind);
        Assert.Equal("tool.echo", descriptor.CapabilityId.Value);
    }

    [Fact]
    public void AddValidatorTValidatorTRequestTResult_GivenUniqueId_RegistersResolvableTypedCapability()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);

        builder.AddValidator<ApprovalValidator, ValidationRequest, ValidationResult>(
            capabilityId: new CapabilityId("validator.approval"),
            displayName: "Approval validator");

        var provider = services.BuildServiceProvider();
        var capability = provider.GetService<ApprovalValidator>();
        var descriptor = Assert.Single(builder.CapabilityDescriptors);

        Assert.NotNull(capability);
        Assert.Equal(typeof(ValidationRequest), descriptor.RequestType);
        Assert.Equal(typeof(ValidationResult), descriptor.ResultType);
        Assert.Equal(typeof(ApprovalValidator), descriptor.CapabilityType);
        Assert.Equal(CapabilityKind.Validator, descriptor.Kind);
        Assert.Equal("validator.approval", descriptor.CapabilityId.Value);
    }

    [Fact]
    public void AddAgentTAgent_GivenSingleImplementedInterface_RegistersResolvableTypedCapability()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);

        builder.AddAgent<InferredPlanAgent>(
            capabilityId: new CapabilityId("agent.plan.inferred"),
            displayName: "Inferred plan agent");

        var provider = services.BuildServiceProvider();
        var capability = provider.GetService<InferredPlanAgent>();
        var descriptor = Assert.Single(builder.CapabilityDescriptors);

        Assert.NotNull(capability);
        Assert.Equal(typeof(PlanRequest), descriptor.RequestType);
        Assert.Equal(typeof(PlanResult), descriptor.ResultType);
        Assert.Equal(typeof(InferredPlanAgent), descriptor.CapabilityType);
        Assert.Equal(CapabilityKind.Agent, descriptor.Kind);
        Assert.Equal("agent.plan.inferred", descriptor.CapabilityId.Value);
    }

    [Fact]
    public void AddToolTTool_GivenSingleImplementedInterface_RegistersResolvableTypedCapability()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);

        builder.AddTool<InferredEchoTool>(
            capabilityId: new CapabilityId("tool.echo.inferred"),
            displayName: "Inferred echo tool");

        var provider = services.BuildServiceProvider();
        var capability = provider.GetService<InferredEchoTool>();
        var descriptor = Assert.Single(builder.CapabilityDescriptors);

        Assert.NotNull(capability);
        Assert.Equal(typeof(ToolRequest), descriptor.RequestType);
        Assert.Equal(typeof(ToolResult), descriptor.ResultType);
        Assert.Equal(typeof(InferredEchoTool), descriptor.CapabilityType);
        Assert.Equal(CapabilityKind.Tool, descriptor.Kind);
        Assert.Equal("tool.echo.inferred", descriptor.CapabilityId.Value);
    }

    [Fact]
    public void AddValidatorTValidator_GivenSingleImplementedInterface_RegistersResolvableTypedCapability()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);

        builder.AddValidator<InferredApprovalValidator>(
            capabilityId: new CapabilityId("validator.approval.inferred"),
            displayName: "Inferred approval validator");

        var provider = services.BuildServiceProvider();
        var capability = provider.GetService<InferredApprovalValidator>();
        var descriptor = Assert.Single(builder.CapabilityDescriptors);

        Assert.NotNull(capability);
        Assert.Equal(typeof(ValidationRequest), descriptor.RequestType);
        Assert.Equal(typeof(ValidationResult), descriptor.ResultType);
        Assert.Equal(typeof(InferredApprovalValidator), descriptor.CapabilityType);
        Assert.Equal(CapabilityKind.Validator, descriptor.Kind);
        Assert.Equal("validator.approval.inferred", descriptor.CapabilityId.Value);
    }

    [Fact]
    public void AddAgentTAgent_GivenAmbiguousImplementedInterfaces_ThrowsDeterministicConfigurationException()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            builder.AddAgent<AmbiguousPlanAgent>(
                capabilityId: new CapabilityId("agent.plan.ambiguous"),
                displayName: "Ambiguous plan agent"));

        Assert.Contains("AmbiguousPlanAgent", exception.Message, StringComparison.Ordinal);
        Assert.Contains("IAgent`2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddToolTTool_GivenAmbiguousImplementedInterfaces_ThrowsDeterministicConfigurationException()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            builder.AddTool<AmbiguousEchoTool>(
                capabilityId: new CapabilityId("tool.echo.ambiguous"),
                displayName: "Ambiguous echo tool"));

        Assert.Contains("AmbiguousEchoTool", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ITool`2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddValidatorTValidator_GivenAmbiguousImplementedInterfaces_ThrowsDeterministicConfigurationException()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            builder.AddValidator<AmbiguousApprovalValidator>(
                capabilityId: new CapabilityId("validator.approval.ambiguous"),
                displayName: "Ambiguous approval validator"));

        Assert.Contains("AmbiguousApprovalValidator", exception.Message, StringComparison.Ordinal);
        Assert.Contains("IValidator`2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddCapability_GivenDuplicateCapabilityId_RejectsRegistrationUnlessReplaceEnabled()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);

        builder.AddAgent<PlanAgent, PlanRequest, PlanResult>(
            capabilityId: new CapabilityId("capability.duplicate"),
            displayName: "Plan agent");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            builder.AddTool<EchoTool, ToolRequest, ToolResult>(
                capabilityId: new CapabilityId("capability.duplicate"),
                displayName: "Echo tool"));

        Assert.Contains("capability.duplicate", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddCapability_GivenDuplicateCapabilityIdAndReplaceEnabled_ReplacesExistingRegistration()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services)
            .EnableCapabilityReplacement();

        builder.AddAgent<PlanAgent, PlanRequest, PlanResult>(
            capabilityId: new CapabilityId("capability.replace"),
            displayName: "Plan agent");

        builder.AddTool<EchoTool, ToolRequest, ToolResult>(
            capabilityId: new CapabilityId("capability.replace"),
            displayName: "Echo tool");

        var descriptor = Assert.Single(builder.CapabilityDescriptors);

        Assert.Equal("capability.replace", descriptor.CapabilityId.Value);
        Assert.Equal(CapabilityKind.Tool, descriptor.Kind);
        Assert.Equal(typeof(EchoTool), descriptor.CapabilityType);
        Assert.Equal(typeof(ToolRequest), descriptor.RequestType);
        Assert.Equal(typeof(ToolResult), descriptor.ResultType);
    }

    [Fact]
    public void AddPolicyTPolicyTRequest_GivenPolicyId_RegistersResolvableTypedPolicy()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);

        builder.AddPolicy<CommandPolicy, CommandRequest>(policyId: new PolicyId("policy.local-safe"));

        var provider = services.BuildServiceProvider();
        var policy = provider.GetService<CommandPolicy>();
        var registration = Assert.Single(builder.PolicyRegistrations);

        Assert.NotNull(policy);
        Assert.Equal("policy.local-safe", registration.PolicyId.Value);
        Assert.Equal(typeof(CommandPolicy), registration.PolicyType);
        Assert.Equal(typeof(CommandRequest), registration.RequestType);
    }

    [Fact]
    public void AddWorkflowTWorkflowTRequestTResult_GivenWorkflowId_RegistersResolvableTypedWorkflow()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);

        builder.AddWorkflow<LinearWorkflow, WorkflowRequest, WorkflowResult>(
            workflowId: new WorkflowId("workflow.linear"),
            displayName: "Linear workflow");

        var provider = services.BuildServiceProvider();
        var workflow = provider.GetService<LinearWorkflow>();
        var registration = Assert.Single(builder.WorkflowRegistrations);

        Assert.NotNull(workflow);
        Assert.Equal("workflow.linear", registration.WorkflowId.Value);
        Assert.Equal(typeof(LinearWorkflow), registration.WorkflowType);
        Assert.Equal(typeof(WorkflowRequest), registration.RequestType);
        Assert.Equal(typeof(WorkflowResult), registration.ResultType);
        Assert.Equal(typeof(WorkflowRequest), registration.Descriptor.RequestType);
        Assert.Equal(typeof(WorkflowResult), registration.Descriptor.ResultType);
    }

    private sealed record PlanRequest(string Task);

    private sealed record PlanResult(string Plan);

    private sealed class PlanAgent : IAgent<PlanRequest, PlanResult>
    {
        public ValueTask<PlanResult> ExecuteAsync(PlanRequest request, CapabilityContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new PlanResult(request.Task));
    }

    private sealed class InferredPlanAgent : IAgent<PlanRequest, PlanResult>
    {
        public ValueTask<PlanResult> ExecuteAsync(PlanRequest request, CapabilityContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new PlanResult(request.Task));
    }

    private sealed class AmbiguousPlanAgent :
        IAgent<PlanRequest, PlanResult>,
        IAgent<ToolRequest, ToolResult>
    {
        ValueTask<PlanResult> ICapability<PlanRequest, PlanResult>.ExecuteAsync(PlanRequest request, CapabilityContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new PlanResult(request.Task));

        ValueTask<ToolResult> ICapability<ToolRequest, ToolResult>.ExecuteAsync(ToolRequest request, CapabilityContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new ToolResult(request.Command));
    }

    private sealed record ToolRequest(string Command);

    private sealed record ToolResult(string Output);

    private sealed class EchoTool : ITool<ToolRequest, ToolResult>
    {
        public ValueTask<ToolResult> ExecuteAsync(ToolRequest request, CapabilityContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new ToolResult(request.Command));
    }

    private sealed class InferredEchoTool : ITool<ToolRequest, ToolResult>
    {
        public ValueTask<ToolResult> ExecuteAsync(ToolRequest request, CapabilityContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new ToolResult(request.Command));
    }

    private sealed class AmbiguousEchoTool :
        ITool<ToolRequest, ToolResult>,
        ITool<ValidationRequest, ValidationResult>
    {
        ValueTask<ToolResult> ICapability<ToolRequest, ToolResult>.ExecuteAsync(ToolRequest request, CapabilityContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new ToolResult(request.Command));

        ValueTask<ValidationResult> ICapability<ValidationRequest, ValidationResult>.ExecuteAsync(ValidationRequest request, CapabilityContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new ValidationResult(!string.IsNullOrWhiteSpace(request.Candidate)));
    }

    private sealed record ValidationRequest(string Candidate);

    private sealed record ValidationResult(bool IsValid);

    private sealed class ApprovalValidator : IValidator<ValidationRequest, ValidationResult>
    {
        public ValueTask<ValidationResult> ExecuteAsync(ValidationRequest request, CapabilityContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new ValidationResult(!string.IsNullOrWhiteSpace(request.Candidate)));
    }

    private sealed class InferredApprovalValidator : IValidator<ValidationRequest, ValidationResult>
    {
        public ValueTask<ValidationResult> ExecuteAsync(ValidationRequest request, CapabilityContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new ValidationResult(!string.IsNullOrWhiteSpace(request.Candidate)));
    }

    private sealed class AmbiguousApprovalValidator :
        IValidator<ValidationRequest, ValidationResult>,
        IValidator<WorkflowRequest, WorkflowResult>
    {
        ValueTask<ValidationResult> ICapability<ValidationRequest, ValidationResult>.ExecuteAsync(ValidationRequest request, CapabilityContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new ValidationResult(!string.IsNullOrWhiteSpace(request.Candidate)));

        ValueTask<WorkflowResult> ICapability<WorkflowRequest, WorkflowResult>.ExecuteAsync(WorkflowRequest request, CapabilityContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new WorkflowResult(request.Goal));
    }

    private sealed record CommandRequest(string Command);

    private sealed class CommandPolicy : IPolicy<CommandRequest>
    {
        public PolicyId PolicyId => new("policy.local-safe");

        public ValueTask<PolicyDecision> EvaluateAsync(CommandRequest request, PolicyContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(PolicyDecision.Allow());
    }

    private sealed record WorkflowRequest(string Goal);

    private sealed record WorkflowResult(string Summary);

    private sealed class LinearWorkflow : IWorkflow<WorkflowRequest, WorkflowResult>
    {
        public WorkflowId WorkflowId => new("workflow.linear");

        public ValueTask<WorkflowResult> ExecuteAsync(WorkflowRequest request, WorkflowContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new WorkflowResult(request.Goal));
    }
}
