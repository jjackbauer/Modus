using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Hosting;
using Modus.Host.Hosting;
using Modus.Host.Plugins.Scanning;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class PortabilityContractsTests
{
    [Fact]
    public void PortabilityContracts_GivenPublicApiInspection_ExpectedStableCoreSignaturesForEmbeddingConsumers()
    {
        var contractType = typeof(IPluginHostPortabilityContract);
        var optionsType = typeof(PluginHostingOptions);

        Assert.Equal("Modus.Core.Hosting", contractType.Namespace);
        Assert.Equal(typeof(string), contractType.GetProperty(nameof(IPluginHostPortabilityContract.ContractName))?.PropertyType);
        Assert.Equal(typeof(Version), contractType.GetProperty(nameof(IPluginHostPortabilityContract.ContractVersion))?.PropertyType);

        Assert.True(contractType.IsAssignableFrom(optionsType));
        Assert.Equal(typeof(string), optionsType.GetProperty(nameof(PluginHostingOptions.PluginsPath))?.PropertyType);
        Assert.Equal(typeof(bool), optionsType.GetProperty(nameof(PluginHostingOptions.RunOnce))?.PropertyType);
    }

    [Fact]
    public void PortabilityContracts_GivenCoreAssemblyInspection_ExpectedNoHostNamespaceTypeDependencies()
    {
        var coreAssembly = typeof(IPluginHostPortabilityContract).Assembly;
        var hostReferences = coreAssembly
            .GetReferencedAssemblies()
            .Where(x => x.Name is not null && x.Name.StartsWith("Modus.Host", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(hostReferences);
    }

    [Fact]
    public void PortabilityOptions_GivenRelativePathConfiguration_ExpectedDeterministicNormalizedOptions()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-portability-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var options = new PluginHostingOptions
            {
                PluginsPath = " plugins\\portable ",
                RunOnce = true,
            };

            var normalized = options.Normalize(root);

            Assert.Equal(PluginHostingOptions.DefaultContractName, normalized.ContractName);
            Assert.Equal(PluginHostingOptions.DefaultContractVersion, normalized.ContractVersion);
            Assert.Equal(Path.GetFullPath(Path.Combine(root, "plugins", "portable")), normalized.PluginsPath);
            Assert.True(normalized.RunOnce);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AddPluginHostingCore_GivenEmptyServiceCollection_ExpectedCorePortabilityContractsRegistered()
    {
        var services = new ServiceCollection();

        services.AddPluginHostingCore();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<PluginHostingOptions>();
        var contract = provider.GetRequiredService<IPluginHostPortabilityContract>();

        Assert.Same(options, contract);
        Assert.Equal("plugins", options.PluginsPath);
        Assert.False(options.RunOnce);
        Assert.Equal(PluginHostingOptions.DefaultContractName, contract.ContractName);
        Assert.Equal(PluginHostingOptions.DefaultContractVersion, contract.ContractVersion);
    }

    [Fact]
    public void AddPluginHostingCore_GivenMultipleInvocations_ExpectedIdempotentServiceRegistration()
    {
        var services = new ServiceCollection();

        services.AddPluginHostingCore();
        services.AddPluginHostingCore();

        var optionsRegistrations = services.Count(descriptor => descriptor.ServiceType == typeof(PluginHostingOptions));
        var contractRegistrations = services.Count(descriptor => descriptor.ServiceType == typeof(IPluginHostPortabilityContract));

        Assert.Equal(1, optionsRegistrations);
        Assert.Equal(1, contractRegistrations);
    }

    [Fact]
    public void AddPluginHostingCore_GivenServiceRegistrationInspection_ExpectedHostAgnosticDescriptorsOnly()
    {
        var services = new ServiceCollection();

        services.AddPluginHostingCore();

        var hostDescriptors = services
            .Where(descriptor =>
            {
                var implementationType = descriptor.ImplementationType;
                var implementationTypeName = implementationType?.FullName ?? string.Empty;
                var serviceTypeName = descriptor.ServiceType.FullName ?? string.Empty;
                return implementationTypeName.StartsWith("Modus.Host", StringComparison.Ordinal)
                    || serviceTypeName.StartsWith("Modus.Host", StringComparison.Ordinal);
            })
            .ToArray();

        Assert.Empty(hostDescriptors);
    }

    [Fact]
    public void PortabilityContracts_GivenConsumerSnapshot_ExpectedDeterministicContractValuesAcrossAccesses()
    {
        var services = new ServiceCollection();
        services.AddPluginHostingCore();

        using var provider = services.BuildServiceProvider();
        var contract = provider.GetRequiredService<IPluginHostPortabilityContract>();

        var firstSnapshot = (contract.ContractName, contract.ContractVersion.ToString());
        var secondSnapshot = (contract.ContractName, contract.ContractVersion.ToString());

        Assert.Equal(firstSnapshot, secondSnapshot);
        Assert.Equal(PluginHostingOptions.DefaultContractName, firstSnapshot.ContractName);
        Assert.Equal(PluginHostingOptions.DefaultContractVersion.ToString(), firstSnapshot.Item2);
    }

    [Fact]
    public void AddModusPluginHosting_GivenValidConfigureDelegate_ExpectedHostRunnerAndRuntimeDependenciesRegistered()
    {
        var services = new ServiceCollection();
        const string customPath = "custom/plugins";

        services.AddModusPluginHosting(opts => opts.PluginsPath = customPath);

        using var provider = services.BuildServiceProvider();

        var runner = provider.GetRequiredService<HostRunner>();
        var runtime = provider.GetRequiredService<InMemoryHostRuntime>();
        var options = provider.GetRequiredService<PluginHostingOptions>();

        Assert.NotNull(runner);
        Assert.NotNull(runtime);
        Assert.Equal(customPath, options.PluginsPath);
    }

    [Fact]
    public void AddModusPluginHosting_GivenNullConfigureDelegate_ExpectedDefaultOptionsApplied()
    {
        var services = new ServiceCollection();

        services.AddModusPluginHosting(configure: null);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<PluginHostingOptions>();

        Assert.Equal("plugins", options.PluginsPath);
        Assert.False(options.RunOnce);
        Assert.Equal(PluginHostingOptions.DefaultContractName, options.ContractName);
        Assert.Equal(PluginHostingOptions.DefaultContractVersion, options.ContractVersion);
    }

    [Fact]
    public void AddModusPluginHosting_GivenMultipleInvocations_ExpectedNoAmbiguousRuntimeResolution()
    {
        var services = new ServiceCollection();

        services.AddModusPluginHosting();
        services.AddModusPluginHosting();

        var runnerRegistrations = services.Count(d => d.ServiceType == typeof(HostRunner));
        var runtimeRegistrations = services.Count(d => d.ServiceType == typeof(InMemoryHostRuntime));

        Assert.Equal(1, runnerRegistrations);
        Assert.Equal(1, runtimeRegistrations);

        using var provider = services.BuildServiceProvider();
        var runner = provider.GetRequiredService<HostRunner>();
        Assert.NotNull(runner);
    }

    [Fact]
    [Trait("ChecklistItem", "di-registration-lambda")]
    public void AddModusPluginHostingRuntime_GivenProviderBuild_ExpectedPluginFolderWatcherResolvableFromDi()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new PluginHostingOptions());

        services.AddModusPluginHostingRuntime();

        using var provider = services.BuildServiceProvider();

        var watcher1 = provider.GetRequiredService<PluginFolderWatcher>();
        var watcher2 = provider.GetRequiredService<PluginFolderWatcher>();
        var runner = provider.GetRequiredService<HostRunner>();

        Assert.NotNull(runner);
        Assert.Same(watcher1, watcher2);
    }
}