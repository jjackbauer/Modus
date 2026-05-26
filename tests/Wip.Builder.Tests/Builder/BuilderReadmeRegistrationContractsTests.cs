using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Policies;
using Wip.Abstractions.Workflows;
using Xunit;

namespace Wip.Builder.Tests.Builder;

public sealed class BuilderReadmeRegistrationContractsTests
{
    [Fact]
    public void BuilderReadme_GivenDuplicateCapabilityIdWithoutReplacement_RegistrationFailsDeterministically()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);

        builder.AddAgent<PlanningAgent, PlanRequest, PlanResult>(
            capabilityId: new CapabilityId("capability.duplicate"),
            displayName: "Planning agent");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            builder.AddTool<EchoTool, ToolRequest, ToolResult>(
                capabilityId: new CapabilityId("capability.duplicate"),
                displayName: "Echo tool"));

        Assert.Contains("capability.duplicate", exception.Message, StringComparison.Ordinal);
        Assert.Contains("replacement", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuilderReadme_GivenDuplicateCapabilityIdWithReplacementEnabled_NewDescriptorReplacesOldDescriptor()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services)
            .EnableCapabilityReplacement();

        builder.AddAgent<PlanningAgent, PlanRequest, PlanResult>(
            capabilityId: new CapabilityId("capability.replace"),
            displayName: "Planning agent");

        builder.AddTool<EchoTool, ToolRequest, ToolResult>(
            capabilityId: new CapabilityId("capability.replace"),
            displayName: "Echo tool");

        using var provider = services.BuildServiceProvider();
        var descriptor = Assert.Single(builder.CapabilityDescriptors);
        var oldCapability = provider.GetService<PlanningAgent>();
        var newCapability = provider.GetService<EchoTool>();

        Assert.Equal("capability.replace", descriptor.CapabilityId.Value);
        Assert.Equal(typeof(EchoTool), descriptor.CapabilityType);
        Assert.Equal(typeof(ToolRequest), descriptor.RequestType);
        Assert.Equal(typeof(ToolResult), descriptor.ResultType);
        Assert.Null(oldCapability);
        Assert.NotNull(newCapability);
    }

    [Fact]
    public void BuilderReadme_GivenInferredCapabilitySignatureAmbiguity_RegistrationFailsWithSingleSignatureRequirement()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            builder.AddTool<AmbiguousTool>(
                capabilityId: new CapabilityId("tool.ambiguous"),
                displayName: "Ambiguous tool"));

        Assert.Contains("AmbiguousTool", exception.Message, StringComparison.Ordinal);
        Assert.Contains("must implement exactly one", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuilderReadme_GivenWorkflowDisplayNameWhitespace_RegistrationFailsWithDisplayNameConstraint()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);

        var exception = Assert.Throws<ArgumentException>(() =>
            builder.AddWorkflow<LinearWorkflow, WorkflowRequest, WorkflowResult>(
                workflowId: new WorkflowId("workflow.linear"),
                displayName: "   "));

        Assert.Equal("displayName", exception.ParamName);
    }

    [Fact]
    public void BuilderReadme_GivenPolicyAndWorkflowRegistration_ResolvesAsSingletonsAndTracksTypedMetadata()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);

        builder.AddPolicy<CommandPolicy, CommandRequest>(new PolicyId("policy.local-safe"));
        builder.AddWorkflow<LinearWorkflow, WorkflowRequest, WorkflowResult>(
            workflowId: new WorkflowId("workflow.linear"),
            displayName: "Linear workflow");

        using var provider = services.BuildServiceProvider();
        var policyA = provider.GetRequiredService<CommandPolicy>();
        var policyB = provider.GetRequiredService<CommandPolicy>();
        var workflowA = provider.GetRequiredService<LinearWorkflow>();
        var workflowB = provider.GetRequiredService<LinearWorkflow>();

        var policyRegistration = Assert.Single(builder.PolicyRegistrations);
        var workflowRegistration = Assert.Single(builder.WorkflowRegistrations);

        Assert.Same(policyA, policyB);
        Assert.Same(workflowA, workflowB);
        Assert.Equal(typeof(CommandPolicy), policyRegistration.PolicyType);
        Assert.Equal(typeof(CommandRequest), policyRegistration.RequestType);
        Assert.Equal(typeof(LinearWorkflow), workflowRegistration.WorkflowType);
        Assert.Equal(typeof(WorkflowRequest), workflowRegistration.RequestType);
        Assert.Equal(typeof(WorkflowResult), workflowRegistration.ResultType);
    }

    private sealed record PlanRequest(string Goal);

    private sealed record PlanResult(string Summary);

    private sealed class PlanningAgent : IAgent<PlanRequest, PlanResult>
    {
        public ValueTask<PlanResult> ExecuteAsync(PlanRequest request, CapabilityContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new PlanResult(request.Goal));
    }

    private sealed record ToolRequest(string Command);

    private sealed record ToolResult(string Output);

    private sealed class EchoTool : ITool<ToolRequest, ToolResult>
    {
        public ValueTask<ToolResult> ExecuteAsync(ToolRequest request, CapabilityContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new ToolResult(request.Command));
    }

    private sealed class AmbiguousTool :
        ITool<ToolRequest, ToolResult>,
        ITool<PlanRequest, PlanResult>
    {
        ValueTask<ToolResult> ICapability<ToolRequest, ToolResult>.ExecuteAsync(ToolRequest request, CapabilityContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new ToolResult(request.Command));

        ValueTask<PlanResult> ICapability<PlanRequest, PlanResult>.ExecuteAsync(PlanRequest request, CapabilityContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new PlanResult(request.Goal));
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