using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Plugins;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Descriptors;
using Wip.Abstractions.Identifiers;
using Wip.Builder;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Plugins;
using Wip.Factory.Modus;
using Xunit;

namespace Wip.Factory.Agents.Tests;

public sealed record PingRequest(string Value);

public sealed record PingResult(string Value);

public sealed class PingAgent : IAgent<PingRequest, PingResult>
{
    public ValueTask<PingResult> ExecuteAsync(
        PingRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken) => ValueTask.FromResult(new PingResult(request.Value));
}

public sealed class EchoAgent : IAgent<PingRequest, PingResult>
{
    public ValueTask<PingResult> ExecuteAsync(
        PingRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken) => ValueTask.FromResult(new PingResult("echo:" + request.Value));
}

internal sealed class PingContributor : IFactoryCapabilityContributor
{
    private readonly string _displayName;

    public PingContributor(string displayName = "Ping") => _displayName = displayName;

    public void RegisterCapabilities(WipBuilder builder, PluginOrigin origin)
        => builder.AddAgent<PingAgent, PingRequest, PingResult>(
            new CapabilityId("factory.agent.ping"),
            _displayName,
            new CapabilityVersion("1.2.3"),
            pluginOrigin: origin);

    public IReadOnlyList<FactoryAgentDefinition> GetAgentDefinitions() =>
    [
        new FactoryAgentDefinition(
            new AgentDefinitionId("factory.ping"),
            "Ping",
            "Return the supplied value",
            ["echo a value"],
            ["workspace.read"],
            [new EvaluationPolicyId("eval://ping/v1")],
            ["factory.agent.ping"])
    ];
}

internal sealed class ThrowingContributor : IFactoryCapabilityContributor
{
    public void RegisterCapabilities(WipBuilder builder, PluginOrigin origin)
        => throw new InvalidOperationException("contributor exploded");

    public IReadOnlyList<FactoryAgentDefinition> GetAgentDefinitions() => [];
}

internal sealed class DuplicateContributor : IFactoryCapabilityContributor
{
    public void RegisterCapabilities(WipBuilder builder, PluginOrigin origin)
        => builder.AddAgent<EchoAgent, PingRequest, PingResult>(
            new CapabilityId("factory.agent.ping"),
            "Duplicate Ping",
            new CapabilityVersion("9.9.9"),
            pluginOrigin: origin);

    public IReadOnlyList<FactoryAgentDefinition> GetAgentDefinitions() => [];
}

public sealed class CapabilityContributorTests
{
    private static FactoryContributorRegistration Pack(
        string pluginId,
        IFactoryCapabilityContributor contributor) =>
        new(
            new PluginId(pluginId),
            new CapabilityVersion("1.0.0"),
            "TestAssembly",
            contributor);

    [Fact]
    public async Task Contributor_GivenApprovedPluginPack_RegistersTypedCapabilityInHostBuilder()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);

        var result = new FactoryCapabilityBinder().Bind([Pack("Plugin.Factory.Ping", new PingContributor())], builder);

        Assert.Empty(result.Diagnostics);
        var descriptor = Assert.Single(result.RegisteredDescriptors);
        Assert.Equal(CapabilityKind.Agent, descriptor.Kind);
        Assert.Equal(typeof(PingRequest), descriptor.RequestType);
        Assert.Equal(typeof(PingResult), descriptor.ResultType);
        Assert.Single(result.AgentDefinitions);

        await using var provider = services.BuildServiceProvider();

        // The capability must be resolvable from the host container and actually execute.
        // WipBuilder registers the concrete capability type, so resolution goes through the descriptor.
        var agent = (ICapability<PingRequest, PingResult>)provider.GetRequiredService(descriptor.CapabilityType);
        var executed = await agent.ExecuteAsync(
            new PingRequest("hello"),
            new CapabilityContext(new SessionId("session-1"), "worktree"),
            CancellationToken.None);

        Assert.Equal("hello", executed.Value);
    }

    [Fact]
    public void Contributor_GivenPluginCapability_DescriptorCarriesPluginOriginAndVersion()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);

        var result = new FactoryCapabilityBinder()
            .Bind([Pack("Plugin.Factory.Origin", new PingContributor())], builder);

        var descriptor = Assert.Single(result.RegisteredDescriptors);
        Assert.NotNull(descriptor.PluginOrigin);
        Assert.Equal("Plugin.Factory.Origin", descriptor.PluginOrigin!.PluginId);
        Assert.Equal("TestAssembly", descriptor.PluginOrigin.AssemblyName);
        Assert.Equal("1.2.3", descriptor.Version.Value);
    }

    [Fact]
    public void Contributor_GivenThrowingPack_OtherPacksRemainActiveAndDiagnosticNamesStage()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);

        var result = new FactoryCapabilityBinder().Bind(
            [
                Pack("Plugin.Factory.Broken", new ThrowingContributor()),
                Pack("Plugin.Factory.Ping", new PingContributor())
            ],
            builder);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("capability-registration", diagnostic.Stage);
        Assert.Equal("Plugin.Factory.Broken", diagnostic.PluginId);
        Assert.Contains("contributor exploded", diagnostic.Message, StringComparison.Ordinal);

        Assert.Contains(result.FailedPluginIds, static id => id.Value == "Plugin.Factory.Broken");
        Assert.Contains(result.ActivePluginIds, static id => id.Value == "Plugin.Factory.Ping");

        // The healthy pack must still be registered, and the broken pack must contribute nothing.
        var descriptor = Assert.Single(result.RegisteredDescriptors);
        Assert.Equal(typeof(PingAgent), descriptor.CapabilityType);

        using var provider = services.BuildServiceProvider();
        var agent = provider.GetRequiredService(descriptor.CapabilityType);
        Assert.IsType<PingAgent>(agent);
    }

    [Fact]
    public void Contributor_GivenDuplicateCapabilityId_RejectsRegistrationWithoutReplacement()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);

        var result = new FactoryCapabilityBinder().Bind(
            [
                Pack("Plugin.Factory.Ping", new PingContributor()),
                Pack("Plugin.Factory.Duplicate", new DuplicateContributor())
            ],
            builder);

        // The first registration must survive; the duplicate must not replace it.
        var descriptor = Assert.Single(result.RegisteredDescriptors);
        Assert.Equal(typeof(PingAgent), descriptor.CapabilityType);
        Assert.Equal("1.2.3", descriptor.Version.Value);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("Plugin.Factory.Duplicate", diagnostic.PluginId);
        Assert.Contains("Duplicate capability ID", diagnostic.Message, StringComparison.Ordinal);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<PingAgent>(provider.GetRequiredService(descriptor.CapabilityType));
    }
}
