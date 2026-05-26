using System.Reflection;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Descriptors;
using Wip.Abstractions.Identifiers;
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
            .Where(static method => method.Name == "ExecuteAsync")
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
}
