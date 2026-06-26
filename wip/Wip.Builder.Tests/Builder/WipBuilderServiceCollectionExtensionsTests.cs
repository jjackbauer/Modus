using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Xunit;

namespace Wip.Builder.Tests.Builder;

public sealed class WipBuilderServiceCollectionExtensionsTests
{
    [Fact]
    public void AddWipCapabilities_GivenEmptyServiceCollection_ReturnsBuilderBoundToSameCollection()
    {
        var services = new ServiceCollection();

        var builder = services.AddWipCapabilities();

        Assert.Same(services, builder.Services);
    }

    [Fact]
    public void AddWipCapabilities_GivenTypedCapabilityRegistration_RegistersResolvableCapabilityForExternalComposition()
    {
        var services = new ServiceCollection();
        var builder = services.AddWipCapabilities();

        builder.AddTool<ExternalEchoTool, ExternalEchoRequest, ExternalEchoResult>(
            capabilityId: new CapabilityId("tool.external.echo"),
            displayName: "External echo tool");

        using var provider = services.BuildServiceProvider();
        var capability = provider.GetService<ExternalEchoTool>();
        var descriptor = Assert.Single(builder.CapabilityDescriptors);

        Assert.NotNull(capability);
        Assert.Equal("tool.external.echo", descriptor.CapabilityId.Value);
        Assert.Equal(typeof(ExternalEchoRequest), descriptor.RequestType);
        Assert.Equal(typeof(ExternalEchoResult), descriptor.ResultType);
        Assert.Equal(typeof(ExternalEchoTool), descriptor.CapabilityType);
    }

    [Fact]
    public void AddWipCapabilities_GivenDuplicateCapabilityIdAndDefaultPolicy_RejectsDuplicateDeterministically()
    {
        var services = new ServiceCollection();
        var builder = services.AddWipCapabilities();

        builder.AddTool<ExternalEchoTool, ExternalEchoRequest, ExternalEchoResult>(
            capabilityId: new CapabilityId("tool.external.duplicate"),
            displayName: "External echo tool");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            builder.AddTool<ExternalReplacementTool, ExternalReplacementRequest, ExternalReplacementResult>(
                capabilityId: new CapabilityId("tool.external.duplicate"),
                displayName: "External replacement tool"));

        Assert.Contains("tool.external.duplicate", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ReplaceExisting", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddWipCapabilities_GivenDuplicateCapabilityIdAndReplaceExistingPolicy_ReplacesRegistrationDeterministically()
    {
        var services = new ServiceCollection();
        var builder = services.AddWipCapabilities(options =>
            options.DuplicateCapabilityBehavior = DuplicateCapabilityRegistrationBehavior.ReplaceExisting);

        builder.AddTool<ExternalEchoTool, ExternalEchoRequest, ExternalEchoResult>(
            capabilityId: new CapabilityId("tool.external.replace"),
            displayName: "External echo tool");

        builder.AddTool<ExternalReplacementTool, ExternalReplacementRequest, ExternalReplacementResult>(
            capabilityId: new CapabilityId("tool.external.replace"),
            displayName: "External replacement tool");

        using var provider = services.BuildServiceProvider();
        var descriptor = Assert.Single(builder.CapabilityDescriptors);
        var originalService = provider.GetService<ExternalEchoTool>();
        var replacementService = provider.GetService<ExternalReplacementTool>();

        Assert.Equal("tool.external.replace", descriptor.CapabilityId.Value);
        Assert.Equal(typeof(ExternalReplacementTool), descriptor.CapabilityType);
        Assert.Equal(typeof(ExternalReplacementRequest), descriptor.RequestType);
        Assert.Equal(typeof(ExternalReplacementResult), descriptor.ResultType);
        Assert.Null(originalService);
        Assert.NotNull(replacementService);
    }

    private sealed record ExternalEchoRequest(string Input);

    private sealed record ExternalEchoResult(string Output);

    private sealed record ExternalReplacementRequest(string Input);

    private sealed record ExternalReplacementResult(string Output);

    private sealed class ExternalEchoTool : ITool<ExternalEchoRequest, ExternalEchoResult>
    {
        public ValueTask<ExternalEchoResult> ExecuteAsync(
            ExternalEchoRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new ExternalEchoResult(request.Input));
    }

    private sealed class ExternalReplacementTool : ITool<ExternalReplacementRequest, ExternalReplacementResult>
    {
        public ValueTask<ExternalReplacementResult> ExecuteAsync(
            ExternalReplacementRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new ExternalReplacementResult($"replacement:{request.Input}"));
    }
}
