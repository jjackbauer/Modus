using System.Reflection;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Descriptors;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Policies;
using Wip.Abstractions.Workflows;
using Xunit;

namespace Wip.Abstractions.Tests.Contracts;

public sealed class TypedCapabilityContractsTests
{
    [Fact]
    public void CapabilityDescriptor_GivenTypedAgentRegistration_StoresConcreteRequestAndResultTypes()
    {
        var descriptor = CapabilityDescriptor.For<TestAgent, CreateTaskRequest, CreateTaskResult>(
            capabilityId: new CapabilityId("agent.plan-only"),
            displayName: "Plan only agent",
            kind: CapabilityKind.Agent);

        Assert.Equal(typeof(CreateTaskRequest), descriptor.RequestType);
        Assert.Equal(typeof(CreateTaskResult), descriptor.ResultType);
        Assert.Equal(typeof(TestAgent), descriptor.CapabilityType);
        Assert.Equal(CapabilityKind.Agent, descriptor.Kind);
    }

    [Fact]
    public void TypedCapabilityContract_GivenReflectionScan_FindsNoObjectBasedPublicExecutionInterfaces()
    {
        var assembly = typeof(ICapability<,>).Assembly;

        var objectBasedExecutions = assembly
            .GetExportedTypes()
            .Where(static type => type.IsInterface)
            .SelectMany(static type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Where(static method => method.Name is "ExecuteAsync" or "EvaluateAsync")
            .Where(static method => MethodUsesObjectPayload(method))
            .ToArray();

        Assert.Empty(objectBasedExecutions);
    }

    [Fact]
    public void CapabilityDescriptor_GivenObjectRequestType_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            CapabilityDescriptor.For<ObjectRequestAgent, object, CreateTaskResult>(
                capabilityId: new CapabilityId("agent.object-request"),
                displayName: "Object request agent",
                kind: CapabilityKind.Agent));

        Assert.Equal("TRequest", exception.ParamName);
    }

    [Fact]
    public void CapabilityDescriptor_GivenObjectResultType_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            CapabilityDescriptor.For<ObjectResultAgent, CreateTaskRequest, object>(
                capabilityId: new CapabilityId("agent.object-result"),
                displayName: "Object result agent",
                kind: CapabilityKind.Agent));

        Assert.Equal("TResult", exception.ParamName);
    }

    [Fact]
    public void CapabilityDescriptor_GivenCapabilityTypeDoesNotImplementTypedContract_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new CapabilityDescriptor<CreateTaskRequest, CreateTaskResult>(
                capabilityId: new CapabilityId("agent.invalid-type"),
                displayName: "Invalid type",
                kind: CapabilityKind.Agent,
                capabilityType: typeof(string)));

        Assert.Equal("capabilityType", exception.ParamName);
    }

    [Fact]
    public void PolicyDescriptor_GivenTypedPolicyRegistration_StoresConcreteRequestAndResultTypes()
    {
        var descriptor = PolicyDescriptor.For<TestPolicy, CreateTaskRequest, PolicyDecision>(
            policyId: new PolicyId("policy.local-safe"));

        Assert.Equal(new PolicyId("policy.local-safe"), descriptor.PolicyId);
        Assert.Equal(typeof(CreateTaskRequest), descriptor.RequestType);
        Assert.Equal(typeof(PolicyDecision), descriptor.ResultType);
        Assert.Equal(typeof(TestPolicy), descriptor.PolicyType);
    }

    [Fact]
    public void PolicyDescriptor_GivenObjectResultType_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            PolicyDescriptor.For<ObjectResultPolicy, CreateTaskRequest, object>(
                policyId: new PolicyId("policy.object-result")));

        Assert.Equal("TResult", exception.ParamName);
    }

    [Fact]
    public void WorkflowDescriptor_GivenTypedRequestAndResult_StoresConcreteContractTypes()
    {
        var descriptor = new WorkflowDescriptor<CreateTaskRequest, CreateTaskResult>(
            workflowId: new WorkflowId("workflow.plan"),
            displayName: "Plan workflow");

        Assert.Equal(new WorkflowId("workflow.plan"), descriptor.WorkflowId);
        Assert.Equal("Plan workflow", descriptor.DisplayName);
        Assert.Equal(typeof(CreateTaskRequest), descriptor.RequestType);
        Assert.Equal(typeof(CreateTaskResult), descriptor.ResultType);
    }

    [Fact]
    public void WorkflowDescriptor_GivenObjectRequestType_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new WorkflowDescriptor<object, CreateTaskResult>(
                workflowId: new WorkflowId("workflow.object-request"),
                displayName: "Object workflow"));

        Assert.Equal("TRequest", exception.ParamName);
    }

    private static bool MethodUsesObjectPayload(MethodInfo method)
    {
        var parameterTypes = method.GetParameters().Select(static parameter => parameter.ParameterType);
        if (parameterTypes.Any(static parameterType => parameterType == typeof(object)))
            return true;

        var returnType = method.ReturnType;
        if (!returnType.IsGenericType)
            return false;

        var genericType = returnType.GetGenericTypeDefinition();
        if (genericType != typeof(ValueTask<>) && genericType != typeof(Task<>))
            return false;

        return returnType.GetGenericArguments()[0] == typeof(object);
    }

    private sealed record CreateTaskRequest(string Goal);

    private sealed record CreateTaskResult(string PlanSummary);

    private sealed class TestAgent : IAgent<CreateTaskRequest, CreateTaskResult>
    {
        public ValueTask<CreateTaskResult> ExecuteAsync(
            CreateTaskRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new CreateTaskResult($"Plan: {request.Goal}"));
    }

    private sealed class ObjectRequestAgent : IAgent<object, CreateTaskResult>
    {
        public ValueTask<CreateTaskResult> ExecuteAsync(
            object request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new CreateTaskResult($"Plan: {request}"));
    }

    private sealed class ObjectResultAgent : IAgent<CreateTaskRequest, object>
    {
        public ValueTask<object> ExecuteAsync(
            CreateTaskRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult<object>(new CreateTaskResult($"Plan: {request.Goal}"));
    }

    private sealed class TestPolicy : IPolicy<CreateTaskRequest, PolicyDecision>
    {
        public PolicyId PolicyId => new("policy.local-safe");

        public ValueTask<PolicyDecision> EvaluateAsync(
            CreateTaskRequest request,
            PolicyContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(PolicyDecision.Allow());
    }

    private sealed class ObjectResultPolicy : IPolicy<CreateTaskRequest, object>
    {
        public PolicyId PolicyId => new("policy.object-result");

        public ValueTask<object> EvaluateAsync(
            CreateTaskRequest request,
            PolicyContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult<object>(new object());
    }
}
