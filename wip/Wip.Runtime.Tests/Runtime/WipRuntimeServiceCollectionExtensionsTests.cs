using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Builder;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Runtime.Tests.Runtime;

public sealed class WipRuntimeServiceCollectionExtensionsTests
{
    [Fact]
    public void AddWipRuntime_GivenEmptyServiceCollection_RegistersRuntimeOrchestratorAndDefaultDependencies()
    {
        var services = new ServiceCollection();

        var builder = services.AddWipRuntime();

        using var provider = services.BuildServiceProvider();
        var orchestrator = provider.GetService<WipRuntimeOrchestrator>();
        var sessionStore = provider.GetService<ISessionStore>();
        var eventPublisher = provider.GetService<ISessionEventPublisher>();

        Assert.NotNull(orchestrator);
        Assert.NotNull(sessionStore);
        Assert.NotNull(eventPublisher);
        Assert.Same(services, builder.Services);
    }

    [Fact]
    public void AddWipRuntime_GivenExternalCapabilityRegistration_ResolvesCapabilityWithoutShellHostComposition()
    {
        var services = new ServiceCollection();
        var builder = services.AddWipRuntime();

        builder.AddTool<RuntimeExternalTool, RuntimeExternalRequest, RuntimeExternalResult>(
            capabilityId: new CapabilityId("tool.runtime.external"),
            displayName: "Runtime external tool");

        using var provider = services.BuildServiceProvider();
        var tool = provider.GetService<RuntimeExternalTool>();
        var descriptor = Assert.Single(builder.CapabilityDescriptors);

        Assert.NotNull(tool);
        Assert.Equal("tool.runtime.external", descriptor.CapabilityId.Value);
        Assert.Equal(typeof(RuntimeExternalRequest), descriptor.RequestType);
        Assert.Equal(typeof(RuntimeExternalResult), descriptor.ResultType);
        Assert.Equal(typeof(RuntimeExternalTool), descriptor.CapabilityType);
    }

    [Fact]
    public void AddWipRuntime_GivenReplaceExistingDuplicatePolicy_ReplacesDuplicateCapabilityDeterministically()
    {
        var services = new ServiceCollection();
        var builder = services.AddWipRuntime(options =>
            options.DuplicateCapabilityBehavior = DuplicateCapabilityRegistrationBehavior.ReplaceExisting);

        builder.AddTool<RuntimeExternalTool, RuntimeExternalRequest, RuntimeExternalResult>(
            capabilityId: new CapabilityId("tool.runtime.replace"),
            displayName: "Runtime external tool");

        builder.AddTool<RuntimeReplacementTool, RuntimeReplacementRequest, RuntimeReplacementResult>(
            capabilityId: new CapabilityId("tool.runtime.replace"),
            displayName: "Runtime replacement tool");

        using var provider = services.BuildServiceProvider();
        var descriptor = Assert.Single(builder.CapabilityDescriptors);
        var originalService = provider.GetService<RuntimeExternalTool>();
        var replacementService = provider.GetService<RuntimeReplacementTool>();

        Assert.Equal("tool.runtime.replace", descriptor.CapabilityId.Value);
        Assert.Equal(typeof(RuntimeReplacementTool), descriptor.CapabilityType);
        Assert.Equal(typeof(RuntimeReplacementRequest), descriptor.RequestType);
        Assert.Equal(typeof(RuntimeReplacementResult), descriptor.ResultType);
        Assert.Null(originalService);
        Assert.NotNull(replacementService);
    }

    private sealed record RuntimeExternalRequest(string Message);

    private sealed record RuntimeExternalResult(string Message);

    private sealed record RuntimeReplacementRequest(string Message);

    private sealed record RuntimeReplacementResult(string Message);

    private sealed class RuntimeExternalTool : ITool<RuntimeExternalRequest, RuntimeExternalResult>
    {
        public ValueTask<RuntimeExternalResult> ExecuteAsync(
            RuntimeExternalRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new RuntimeExternalResult(request.Message));
    }

    private sealed class RuntimeReplacementTool : ITool<RuntimeReplacementRequest, RuntimeReplacementResult>
    {
        public ValueTask<RuntimeReplacementResult> ExecuteAsync(
            RuntimeReplacementRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new RuntimeReplacementResult($"replacement:{request.Message}"));
    }
}
