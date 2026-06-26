using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Policies;
using Wip.Abstractions.Workflows;
using Xunit;

namespace Wip.Abstractions.Tests.Contracts;

public sealed class AbstractionsReadmeContractsTests
{
    [Fact]
    public async Task AbstractionsReadme_GivenWorkflowContractExamples_ExecuteAsyncRoundTripMatchesDeclaredRequestResultTypes()
    {
        var sessionId = new SessionId("session-123");
        var workflowContext = new WorkflowContext(sessionId, "plan roadmap", "C:/repo/modus");
        var capabilityContext = new CapabilityContext(sessionId, "C:/repo/modus");

        var workflow = new PlanningWorkflow();
        var agent = new PlanningAgent();

        PlanRequest request = new("document abstractions");

        PlanResult workflowResult = await workflow.ExecuteAsync(request, workflowContext, CancellationToken.None);
        PlanResult agentResult = await agent.ExecuteAsync(request, capabilityContext, CancellationToken.None);

        Assert.Equal("wf:document abstractions:plan roadmap", workflowResult.Summary);
        Assert.Equal("agent:document abstractions:session-123", agentResult.Summary);
        Assert.Equal(typeof(PlanRequest), request.GetType());
        Assert.Equal(typeof(PlanResult), workflowResult.GetType());
        Assert.Equal(typeof(PlanResult), agentResult.GetType());
        Assert.Equal(new WorkflowId("workflow.planning"), workflow.WorkflowId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AbstractionsReadme_GivenInvalidPolicyReasonExample_DenyFactoryRejectsWhitespaceReason(string? reason)
    {
        var exception = Assert.Throws<ArgumentException>(() => PolicyDecision.Deny(reason!));

        Assert.Equal("reason", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void AbstractionsReadme_GivenInvalidTypedIdentifierValue_ConstructorsRejectWhitespace(string? value)
    {
        AssertIdentifierFactoryThrows(value, static input => new CapabilityId(input!));
        AssertIdentifierFactoryThrows(value, static input => new ArtifactId(input!));
        AssertIdentifierFactoryThrows(value, static input => new SessionId(input!));
        AssertIdentifierFactoryThrows(value, static input => new WorkflowId(input!));
        AssertIdentifierFactoryThrows(value, static input => new PolicyId(input!));
    }

    [Fact]
    public void AbstractionsReadme_GivenValidTypedIdentifierValue_ToStringReturnsOriginalValue()
    {
        const string value = "policy.local-safe";
        var policyId = new PolicyId(value);

        Assert.Equal(value, policyId.Value);
        Assert.Equal(value, policyId.ToString());
    }

    [Fact]
    public async Task AbstractionsReadme_GivenGenericPolicyResultContract_EvaluateAsyncReturnsDeclaredTypedResult()
    {
        var policy = new EchoPolicy();
        var request = new PlanRequest("guard command");
        var context = new PolicyContext(
            SessionId: new SessionId("session-456"),
            WorkflowId: new WorkflowId("workflow.planning"),
            WorktreePath: "C:/repo/modus",
            OperationName: "tool.invoke");

        EchoPolicyResult result = await policy.EvaluateAsync(request, context, CancellationToken.None);

        Assert.Equal("allow:guard command:tool.invoke", result.Summary);
        Assert.Equal(typeof(EchoPolicyResult), result.GetType());
    }

    private static void AssertIdentifierFactoryThrows(string? value, Func<string?, object> factory)
    {
        var exception = Assert.Throws<ArgumentException>(() => factory(value));
        Assert.Equal("value", exception.ParamName);
    }

    private sealed record PlanRequest(string Goal);

    private sealed record PlanResult(string Summary);

    private sealed class PlanningWorkflow : IWorkflow<PlanRequest, PlanResult>
    {
        public WorkflowId WorkflowId => new("workflow.planning");

        public ValueTask<PlanResult> ExecuteAsync(PlanRequest request, WorkflowContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new PlanResult($"wf:{request.Goal}:{context.Task}"));
    }

    private sealed class PlanningAgent : IAgent<PlanRequest, PlanResult>
    {
        public ValueTask<PlanResult> ExecuteAsync(PlanRequest request, CapabilityContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new PlanResult($"agent:{request.Goal}:{context.SessionId.Value}"));
    }

    private sealed record EchoPolicyResult(string Summary);

    private sealed class EchoPolicy : IPolicy<PlanRequest, EchoPolicyResult>
    {
        public PolicyId PolicyId => new("policy.echo");

        public ValueTask<EchoPolicyResult> EvaluateAsync(PlanRequest request, PolicyContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new EchoPolicyResult($"allow:{request.Goal}:{context.OperationName}"));
    }
}