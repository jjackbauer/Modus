using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Events;
using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Xunit;

namespace Modus.Core.Tests.Plugins;

public sealed class PluginFolderConcernMapTests
{
    [Fact]
    public void PluginFolders_GivenCurrentPluginsDirectory_ExpectedConcernMappedTargetsAreComplete()
    {
        var expectedFolders = new[]
        {
            "Contracts",
            "Contracts/Specialized",
            "Lifecycle",
            "Validation",
            "Registration",
            "ServiceLifetime",
            "Base",
            "Implementation",
            "Extensions"
        };

        Assert.Equal(expectedFolders, PluginConcernFolderMap.TargetFolders);

        foreach (var fileName in PluginConcernFolderMap.CurrentRootFileToTargetFolder.Keys)
        {
            Assert.Contains(fileName, PluginConcernFolderMap.CurrentPluginRootFiles);
        }

        foreach (var rootFile in PluginConcernFolderMap.CurrentPluginRootFiles)
        {
            Assert.True(
                PluginConcernFolderMap.CurrentRootFileToTargetFolder.ContainsKey(rootFile),
                $"Missing target concern assignment for '{rootFile}'.");
        }
    }

    [Fact]
    public void PluginFolders_GivenTargetConcernMap_ExpectedNoConcernOwnsUnrelatedTypes()
    {
        foreach (var kvp in PluginConcernFolderMap.CurrentRootFileToTargetFolder)
        {
            var fileName = kvp.Key;
            var folder = kvp.Value;

            Assert.Contains(folder, PluginConcernFolderMap.TargetFolders);

            if (folder == "Contracts")
            {
                Assert.StartsWith("I", fileName);
                Assert.DoesNotContain("Telemetry", fileName);
            }

            if (folder == "Contracts/Specialized")
            {
                Assert.True(
                    fileName.StartsWith("IHostTelemetry", StringComparison.Ordinal)
                    || fileName.StartsWith("IMachineTelemetry", StringComparison.Ordinal),
                    $"Unexpected specialized contract file '{fileName}'.");
                Assert.StartsWith("I", fileName);
            }

            if (folder == "Lifecycle")
            {
                Assert.True(
                    fileName.StartsWith("PluginRuntime", StringComparison.Ordinal)
                    || fileName.StartsWith("PluginLifecycle", StringComparison.Ordinal),
                    $"Unexpected lifecycle file '{fileName}'.");
            }

            if (folder == "Validation")
            {
                Assert.True(
                    fileName.Contains("Validation", StringComparison.Ordinal)
                    || fileName.EndsWith("Validator.cs", StringComparison.Ordinal),
                    $"Unexpected validation file '{fileName}'.");
                Assert.DoesNotContain("Registration", fileName);
            }

            if (folder == "Registration")
            {
                Assert.Contains("Registration", fileName);
                Assert.DoesNotContain("Validation", fileName);
            }

            if (folder == "ServiceLifetime")
            {
                Assert.StartsWith("PluginServiceLifetime", fileName);
            }

            if (folder == "Base")
            {
                Assert.Equal("PluginBase.cs", fileName);
            }

            if (folder == "Implementation")
            {
                Assert.DoesNotContain("IPlugin", fileName);
                Assert.DoesNotContain("Validation", fileName);
            }

            if (folder == "Extensions")
            {
                Assert.EndsWith("Extensions.cs", fileName);
            }
        }
    }

    [Fact]
    public void SpecializedContractsMove_GivenTelemetryContracts_ExpectedIsolationFromGenericContracts()
    {
        Assert.Equal(
            "Contracts/Specialized",
            PluginConcernFolderMap.CurrentRootFileToTargetFolder["IHostTelemetryPluginContract.cs"]);
        Assert.Equal(
            "Contracts/Specialized",
            PluginConcernFolderMap.CurrentRootFileToTargetFolder["IMachineTelemetryPluginContract.cs"]);

        var genericContracts = PluginConcernFolderMap.CurrentRootFileToTargetFolder
            .Where(kvp => kvp.Key.StartsWith("IPlugin", StringComparison.Ordinal) || kvp.Key == "IScheduledTimerTaskExtension.cs")
            .ToArray();

        Assert.NotEmpty(genericContracts);
        Assert.All(genericContracts, kvp => Assert.Equal("Contracts", kvp.Value));

        var repoRoot = FindRepositoryRoot();
        var specializedFolder = Path.Combine(repoRoot, "src", "Modus.Core", "Plugins", "Contracts", "Specialized");

        Assert.True(File.Exists(Path.Combine(specializedFolder, "IHostTelemetryPluginContract.cs")));
        Assert.True(File.Exists(Path.Combine(specializedFolder, "IMachineTelemetryPluginContract.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Modus.Core", "Plugins", "IHostTelemetryPluginContract.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Modus.Core", "Plugins", "IMachineTelemetryPluginContract.cs")));
    }

    [Fact]
    public void SpecializedContractsMove_GivenHostAndPluginConsumers_ExpectedTelemetryContractImportsStillCompile()
    {
        IHostTelemetryPluginContract hostConsumer = new HostTelemetryConsumerPlugin("host-plugin");
        IMachineTelemetryPluginContract machineConsumer = new MachineTelemetryConsumerPlugin("machine-plugin");

        Assert.IsAssignableFrom<IPluginContract>(hostConsumer);
        Assert.IsAssignableFrom<IPluginContract>(machineConsumer);

        Assert.Equal("host-plugin", hostConsumer.PluginId);
        Assert.Equal("machine-plugin", machineConsumer.PluginId);
        Assert.Equal("Modus.PluginContract", hostConsumer.ContractName);
        Assert.Equal("Modus.PluginContract", machineConsumer.ContractName);
        Assert.Equal(new Version(1, 0), hostConsumer.ContractVersion);
        Assert.Equal(new Version(1, 0), machineConsumer.ContractVersion);
    }

    [Fact]
    public void LifecycleMove_GivenLifecycleTypesRelocated_ExpectedRuntimeStateTransitionsRemainDeterministic()
    {
        var expected = new Dictionary<PluginRuntimeState, PluginRuntimeState[]>(
            new[]
            {
                new KeyValuePair<PluginRuntimeState, PluginRuntimeState[]>(PluginRuntimeState.Discovered, [PluginRuntimeState.Validated, PluginRuntimeState.Failed]),
                new KeyValuePair<PluginRuntimeState, PluginRuntimeState[]>(PluginRuntimeState.Validated, [PluginRuntimeState.Loaded, PluginRuntimeState.Failed]),
                new KeyValuePair<PluginRuntimeState, PluginRuntimeState[]>(PluginRuntimeState.Loaded, [PluginRuntimeState.Registered, PluginRuntimeState.Failed]),
                new KeyValuePair<PluginRuntimeState, PluginRuntimeState[]>(PluginRuntimeState.Registered, [PluginRuntimeState.Activated, PluginRuntimeState.RollbackPending]),
                new KeyValuePair<PluginRuntimeState, PluginRuntimeState[]>(PluginRuntimeState.Activated, [PluginRuntimeState.Active, PluginRuntimeState.RollbackPending]),
                new KeyValuePair<PluginRuntimeState, PluginRuntimeState[]>(PluginRuntimeState.Active, [PluginRuntimeState.Deactivating]),
                new KeyValuePair<PluginRuntimeState, PluginRuntimeState[]>(PluginRuntimeState.Deactivating, [PluginRuntimeState.Unloaded]),
                new KeyValuePair<PluginRuntimeState, PluginRuntimeState[]>(PluginRuntimeState.RollbackPending, [PluginRuntimeState.Failed]),
                new KeyValuePair<PluginRuntimeState, PluginRuntimeState[]>(PluginRuntimeState.Failed, [PluginRuntimeState.Discovered]),
                new KeyValuePair<PluginRuntimeState, PluginRuntimeState[]>(PluginRuntimeState.Unloaded, []),
            });

        foreach (var state in Enum.GetValues<PluginRuntimeState>())
        {
            var nextStates = PluginRuntimeStateTransitions.GetAllowedNextStates(state).ToArray();
            Assert.Equal(expected[state], nextStates);
        }
    }

    [Fact]
    public void LifecycleMove_GivenLifecycleContextsRelocated_ExpectedLifecycleConsumersResolveTypes()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var loadContext = new PluginLoadContext("plugin-a", cancellationToken);
        var startContext = new PluginStartContext("plugin-a", cancellationToken);
        var stopContext = new PluginStopContext("plugin-a", cancellationToken);
        var unloadContext = new PluginUnloadContext(
            "plugin-a",
            PluginUnloadReason.Reload,
            new DateTimeOffset(2026, 5, 19, 0, 0, 0, TimeSpan.Zero),
            cancellationToken);

        Assert.Equal("plugin-a", loadContext.PluginId);
        Assert.Equal("plugin-a", startContext.PluginId);
        Assert.Equal("plugin-a", stopContext.PluginId);
        Assert.Equal(PluginUnloadReason.Reload, unloadContext.UnloadReason);

        var repoRoot = FindRepositoryRoot();
        var lifecycleFolder = Path.Combine(repoRoot, "src", "Modus.Core", "Plugins", "Lifecycle");

        Assert.True(File.Exists(Path.Combine(lifecycleFolder, "PluginLifecycleContexts.cs")));
        Assert.True(File.Exists(Path.Combine(lifecycleFolder, "PluginRuntimeState.cs")));
        Assert.True(File.Exists(Path.Combine(lifecycleFolder, "PluginRuntimeStateTransitions.cs")));

        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Modus.Core", "Plugins", "PluginLifecycleContexts.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Modus.Core", "Plugins", "PluginRuntimeState.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Modus.Core", "Plugins", "PluginRuntimeStateTransitions.cs")));
    }

    [Fact]
    public void ValidationMove_GivenContractValidatorRelocated_ExpectedMandatoryCapabilityChecksUnaffected()
    {
        var repoRoot = FindRepositoryRoot();
        var validationFolder = Path.Combine(repoRoot, "src", "Modus.Core", "Plugins", "Validation");

        Assert.True(File.Exists(Path.Combine(validationFolder, "ContractValidationResult.cs")));
        Assert.True(File.Exists(Path.Combine(validationFolder, "PluginContractValidationPolicy.cs")));
        Assert.True(File.Exists(Path.Combine(validationFolder, "PluginContractValidator.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Modus.Core", "Plugins", "ContractValidationResult.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Modus.Core", "Plugins", "PluginContractValidationPolicy.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Modus.Core", "Plugins", "PluginContractValidator.cs")));

        var plugin = new ValidationCompliantPlugin("validation-plugin");
        var result = PluginContractValidator.Validate(plugin);

        Assert.True(result.IsValid);
        Assert.Empty(result.MissingCapabilities);
    }

    [Fact]
    public void ValidationMove_GivenVersionValidatorRelocated_ExpectedVersionRulesRemainStable()
    {
        var repoRoot = FindRepositoryRoot();
        var validationFolder = Path.Combine(repoRoot, "src", "Modus.Core", "Plugins", "Validation");

        Assert.True(File.Exists(Path.Combine(validationFolder, "PluginVersionValidator.cs")));
        Assert.True(File.Exists(Path.Combine(validationFolder, "VersionValidationResult.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Modus.Core", "Plugins", "PluginVersionValidator.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Modus.Core", "Plugins", "VersionValidationResult.cs")));

        var incompatible = PluginVersionValidator.Validate(new Version(0, 9), new Version(1, 0));
        var compatible = PluginVersionValidator.Validate(new Version(1, 0), new Version(1, 0));

        Assert.False(incompatible.IsCompatible);
        Assert.Equal("Contract version mismatch.", incompatible.Error);
        Assert.True(compatible.IsCompatible);
        Assert.Null(compatible.Error);
    }

    [Fact]
    public void RegistrationMove_GivenRegistrationPolicyRelocated_ExpectedDeterministicOrderingPreserved()
    {
        var repoRoot = FindRepositoryRoot();
        var registrationFolder = Path.Combine(repoRoot, "src", "Modus.Core", "Plugins", "Registration");

        Assert.True(File.Exists(Path.Combine(registrationFolder, "DeterministicPluginRegistrationPolicy.cs")));
        Assert.True(File.Exists(Path.Combine(registrationFolder, "PluginRegistrationStep.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Modus.Core", "Plugins", "DeterministicPluginRegistrationPolicy.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Modus.Core", "Plugins", "PluginRegistrationStep.cs")));

        var plugin = new RegistrationCompliantPlugin([" warmup ", "ping", "PING", "rebuild"]);
        IPluginRegistrationPolicy policy = new DeterministicPluginRegistrationPolicy();

        var firstPlan = policy.BuildRegistrationPlan(plugin);
        var secondPlan = policy.BuildRegistrationPlan(plugin);

        Assert.Equal(firstPlan, secondPlan);
        Assert.Equal(5, firstPlan.Count);
        Assert.Equal(new PluginRegistrationStep(1, PluginRegistrationStepKind.RegisterOperation, "operation:ping"), firstPlan[0]);
        Assert.Equal(new PluginRegistrationStep(2, PluginRegistrationStepKind.RegisterOperation, "operation:rebuild"), firstPlan[1]);
        Assert.Equal(new PluginRegistrationStep(3, PluginRegistrationStepKind.RegisterOperation, "operation:warmup"), firstPlan[2]);
        Assert.Equal(new PluginRegistrationStep(4, PluginRegistrationStepKind.SubscribeEvents, "events:subscribe"), firstPlan[3]);
        Assert.Equal(new PluginRegistrationStep(5, PluginRegistrationStepKind.RegisterSchedules, "schedules:register"), firstPlan[4]);
    }

    [Fact]
    public void RegistrationMove_GivenRegistrationStepRelocated_ExpectedActivationPipelineReferencesCompile()
    {
        var plugin = new ValidationCompliantPlugin("registration-validation-plugin");
        IPluginRegistrationPolicy policy = plugin;

        IReadOnlyList<PluginRegistrationStep> plan = policy.BuildRegistrationPlan(plugin);

        Assert.NotEmpty(plan);
        Assert.Equal(1, plan[0].Sequence);
        Assert.Equal(PluginRegistrationStepKind.RegisterOperation, plan[0].Kind);

        var validationPolicy = new PluginContractValidationPolicy
        {
            RequireDeterministicRegistrationLifecycle = true,
        };

        var validation = PluginContractValidator.Validate(plugin, validationPolicy);
        Assert.True(validation.IsValid);
    }

    [Fact]
    public void ServiceLifetimeMove_GivenLifetimeMappingRelocated_ExpectedLifetimeResolutionSemanticsUnchanged()
    {
        var repoRoot = FindRepositoryRoot();
        var serviceLifetimeFolder = Path.Combine(repoRoot, "src", "Modus.Core", "Plugins", "ServiceLifetime");

        Assert.True(File.Exists(Path.Combine(serviceLifetimeFolder, "PluginServiceLifetime.cs")));
        Assert.True(File.Exists(Path.Combine(serviceLifetimeFolder, "PluginServiceLifetimeMapping.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Modus.Core", "Plugins", "PluginServiceLifetime.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Modus.Core", "Plugins", "PluginServiceLifetimeMapping.cs")));

        Assert.Equal(ServiceLifetime.Singleton, PluginServiceLifetimeMapping.ToServiceLifetime(PluginServiceLifetime.Singleton));
        Assert.Equal(ServiceLifetime.Scoped, PluginServiceLifetimeMapping.ToServiceLifetime(PluginServiceLifetime.Scoped));
        Assert.Equal(ServiceLifetime.Transient, PluginServiceLifetimeMapping.ToServiceLifetime(PluginServiceLifetime.Transient));
    }

    [Fact]
    public void ServiceLifetimeMove_GivenDependencyRegistration_ExpectedSingletonScopedTransientMappingsRemainValid()
    {
        var services = new ServiceCollection();

        services.AddPluginService<ISingletonProbe, SingletonProbe>(PluginServiceLifetime.Singleton);
        services.AddPluginService<IScopedProbe, ScopedProbe>(PluginServiceLifetime.Scoped);
        services.AddPluginService<ITransientProbe, TransientProbe>(PluginServiceLifetime.Transient);

        var singletonDescriptor = Assert.Single(services, x => x.ServiceType == typeof(ISingletonProbe));
        var scopedDescriptor = Assert.Single(services, x => x.ServiceType == typeof(IScopedProbe));
        var transientDescriptor = Assert.Single(services, x => x.ServiceType == typeof(ITransientProbe));

        Assert.Equal(ServiceLifetime.Singleton, singletonDescriptor.Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, scopedDescriptor.Lifetime);
        Assert.Equal(ServiceLifetime.Transient, transientDescriptor.Lifetime);
    }

    [Fact]
    public void BaseAndImplementationMove_GivenPluginBaseRelocated_ExpectedDerivedPluginsCompile()
    {
        var repoRoot = FindRepositoryRoot();
        var baseFolder = Path.Combine(repoRoot, "src", "Modus.Core", "Plugins", "Base");

        Assert.True(File.Exists(Path.Combine(baseFolder, "PluginBase.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Modus.Core", "Plugins", "PluginBase.cs")));

        var plugin = new DerivedBasePlugin("base-move-plugin");

        Assert.Equal("base-move-plugin", plugin.PluginId);
        Assert.Equal("Modus.PluginContract", plugin.ContractName);
        Assert.Equal(new Version(1, 0), plugin.ContractVersion);
    }

    [Fact]
    public void BaseAndImplementationMove_GivenTimerPluginRelocated_ExpectedScheduledExecutionContractStillSatisfied()
    {
        var repoRoot = FindRepositoryRoot();
        var implementationFolder = Path.Combine(repoRoot, "src", "Modus.Core", "Plugins", "Implementation");

        Assert.True(File.Exists(Path.Combine(implementationFolder, "TimerPlugin.cs")));
        Assert.True(File.Exists(Path.Combine(implementationFolder, "FiveSecondIntervalsTimerPrint.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Modus.Core", "Plugins", "TimerPlugin.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Modus.Core", "Plugins", "FiveSecondIntervalsTimerPrint.cs")));

        var utcNow = new DateTimeOffset(2026, 5, 19, 10, 30, 0, TimeSpan.Zero);
        var writes = new List<string>();
        var plugin = new TimerPlugin(() => utcNow, writes.Add);
        var scheduler = new RecordingScheduler();

        plugin.RegisterSchedules(scheduler);

        var response = plugin.Handle(SyncRequest.ForStandardPath("Timer.WriteCurrentTime"));

        Assert.True(response.Success);
        Assert.Single(writes, utcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Single(scheduler.RecurringSchedules);
        var schedule = scheduler.RecurringSchedules[0];
        Assert.Equal("Timer.WriteCurrentTime.Every5Seconds", schedule.JobName);
        Assert.Equal(TimeSpan.FromSeconds(5), schedule.Interval);
        Assert.Equal("Timer.WriteCurrentTime", schedule.Operation);
    }

    [Fact]
    [Trait("ChecklistItem", "Core.PluginsRefactor.ExtensionsPartition.MoveDIRegistrationHelpers")]
    public void ExtensionsMove_GivenServiceCollectionExtensionsRelocated_ExpectedExtensionMethodsRemainDiscoverable()
    {
        var repoRoot = FindRepositoryRoot();
        var extensionsFolder = Path.Combine(repoRoot, "src", "Modus.Core", "Plugins", "Extensions");

        Assert.True(File.Exists(Path.Combine(extensionsFolder, "PluginDependencyRegisterServiceCollectionExtensions.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Modus.Core", "Plugins", "PluginDependencyRegisterServiceCollectionExtensions.cs")));

        var services = new ServiceCollection();
        services.AddPluginService<ISingletonProbe, SingletonProbe>(PluginServiceLifetime.Singleton);

        var descriptor = Assert.Single(services, x => x.ServiceType == typeof(ISingletonProbe));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    [Trait("ChecklistItem", "Core.PluginsRefactor.ReferenceAndNamespaceStabilization.UpdateImpactedReferences")]
    public void ReferenceUpdate_GivenRelocatedPluginFiles_ExpectedAllProjectReferencesCompile()
    {
        Assert.Equal("Modus.Core.Plugins", PluginConcernFolderMap.CanonicalPluginNamespace);

        var coreAssembly = typeof(IPluginContract).Assembly;
        var pluginTypes = coreAssembly
            .GetTypes()
            .Where(type => type.Namespace == PluginConcernFolderMap.CanonicalPluginNamespace)
            .Where(type => typeof(IPluginContract).IsAssignableFrom(type)
                || typeof(IPluginLifecycle).IsAssignableFrom(type)
                || typeof(IPluginRegistrationPolicy).IsAssignableFrom(type)
                || typeof(IPluginOperationCatalog).IsAssignableFrom(type)
                || typeof(IPluginScheduledEvents).IsAssignableFrom(type)
                || typeof(IPluginDependencyRegister).IsAssignableFrom(type)
                || typeof(IPluginScheduler).IsAssignableFrom(type)
                || typeof(IScheduledTimerTaskExtension).IsAssignableFrom(type))
            .ToArray();

        Assert.NotEmpty(pluginTypes);

        var invalidNamespaceTypes = coreAssembly
            .GetTypes()
            .Where(type => type.Namespace is not null)
            .Where(type => PluginConcernFolderMap.ForbiddenFolderDerivedNamespacePrefixes.Any(prefix =>
                type.Namespace!.StartsWith(prefix, StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(invalidNamespaceTypes);

        var repoRoot = FindRepositoryRoot();
        AssertUsingStatement(repoRoot, "src", "Modus.Core", "Hosting", "PluginHostingCoreExtensions.cs");
        AssertUsingStatement(repoRoot, "src", "Modus.Host", "Program.cs");
        AssertUsingStatement(repoRoot, "tests", "Modus.Core.Tests", "Plugins", "PluginContractsTests.cs");
    }

    [Fact]
    [Trait("ChecklistItem", "Core.PluginsRefactor.ReferenceAndNamespaceStabilization.UpdateImpactedReferences")]
    public void ReferenceUpdate_GivenRuntimeTypeDiscovery_ExpectedPluginContractsStillDiscovered()
    {
        var assembly = typeof(IPluginContract).Assembly;

        var pluginContractTypes = assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .Where(type => typeof(IPluginContract).IsAssignableFrom(type))
            .Where(type => type.Namespace == PluginConcernFolderMap.CanonicalPluginNamespace)
            .Select(type => type.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        var scheduledExtensionTypes = assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .Where(type => typeof(IScheduledTimerTaskExtension).IsAssignableFrom(type))
            .Where(type => type.Namespace == PluginConcernFolderMap.CanonicalPluginNamespace)
            .Select(type => type.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Contains("TimerPlugin", pluginContractTypes);
        Assert.DoesNotContain("FiveSecondIntervalsTimerPrint", pluginContractTypes);
        Assert.Contains("FiveSecondIntervalsTimerPrint", scheduledExtensionTypes);
        Assert.DoesNotContain(
            pluginContractTypes,
            static typeName => typeName.Contains("Contracts", StringComparison.Ordinal)
                || typeName.Contains("Lifecycle", StringComparison.Ordinal)
                || typeName.Contains("Validation", StringComparison.Ordinal)
                || typeName.Contains("Registration", StringComparison.Ordinal)
                || typeName.Contains("ServiceLifetime", StringComparison.Ordinal)
                || typeName.Contains("Implementation", StringComparison.Ordinal)
                || typeName.Contains("Extensions", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("ChecklistItem", "Core.PluginsRefactor.RegressionProof.BehavioralEquivalenceGate")]
    public void RegressionProof_GivenFolderDecomposition_ExpectedDiscoveryFlowRemainsStable()
    {
        var assembly = typeof(IPluginContract).Assembly;

        var discoveredContractTypes = assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .Where(type => typeof(IPluginContract).IsAssignableFrom(type))
            .Where(type => type.Namespace == PluginConcernFolderMap.CanonicalPluginNamespace)
            .Select(static type => type.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Contains("TimerPlugin", discoveredContractTypes);
        Assert.DoesNotContain("PluginBase", discoveredContractTypes);
    }

    [Fact]
    [Trait("ChecklistItem", "Core.PluginsRefactor.RegressionProof.BehavioralEquivalenceGate")]
    public void RegressionProof_GivenFolderDecomposition_ExpectedValidationFlowRemainsStable()
    {
        var validationPolicy = new PluginContractValidationPolicy
        {
            RequireDeterministicRegistrationLifecycle = true,
        };

        var compliantResult = PluginContractValidator.Validate(new ValidationCompliantPlugin("regression-valid"), validationPolicy);
        var invalidResult = PluginContractValidator.Validate(new ValidationIncompletePlugin(), validationPolicy);

        Assert.True(compliantResult.IsValid);
        Assert.Empty(compliantResult.MissingCapabilities);

        Assert.False(invalidResult.IsValid);
        Assert.Contains(nameof(IPluginLifecycle), invalidResult.MissingCapabilities);
        Assert.Contains(nameof(IPluginOperationCatalog), invalidResult.MissingCapabilities);
        Assert.Contains(nameof(IPluginRegistrationPolicy), invalidResult.MissingCapabilities);
    }

    [Fact]
    [Trait("ChecklistItem", "Core.PluginsRefactor.RegressionProof.BehavioralEquivalenceGate")]
    public void RegressionProof_GivenFolderDecomposition_ExpectedRegistrationFlowRemainsStable()
    {
        var plugin = new RegistrationCompliantPlugin([" warmup ", "ping", "PING", "rebuild"]);
        IPluginRegistrationPolicy policy = new DeterministicPluginRegistrationPolicy();

        var plan = policy.BuildRegistrationPlan(plugin);

        Assert.Equal(5, plan.Count);
        Assert.Equal(new PluginRegistrationStep(1, PluginRegistrationStepKind.RegisterOperation, "operation:ping"), plan[0]);
        Assert.Equal(new PluginRegistrationStep(2, PluginRegistrationStepKind.RegisterOperation, "operation:rebuild"), plan[1]);
        Assert.Equal(new PluginRegistrationStep(3, PluginRegistrationStepKind.RegisterOperation, "operation:warmup"), plan[2]);
        Assert.Equal(new PluginRegistrationStep(4, PluginRegistrationStepKind.SubscribeEvents, "events:subscribe"), plan[3]);
        Assert.Equal(new PluginRegistrationStep(5, PluginRegistrationStepKind.RegisterSchedules, "schedules:register"), plan[4]);
    }

    [Fact]
    [Trait("ChecklistItem", "Core.PluginsRefactor.RegressionProof.BehavioralEquivalenceGate")]
    public void RegressionProof_GivenFolderDecomposition_ExpectedRuntimeFlowRemainsStable()
    {
        var writes = new List<string>();
        var utcNow = new DateTimeOffset(2026, 5, 19, 14, 15, 16, TimeSpan.Zero);
        var plugin = new TimerPlugin(() => utcNow, writes.Add);
        var scheduler = new RecordingScheduler();

        plugin.Load(new PluginLoadContext(plugin.PluginId, CancellationToken.None));
        plugin.Start(new PluginStartContext(plugin.PluginId, CancellationToken.None));
        plugin.RegisterSchedules(scheduler);

        var response = plugin.Handle(SyncRequest.ForStandardPath("Timer.WriteCurrentTime", "corr-regression-runtime"));

        plugin.Stop(new PluginStopContext(plugin.PluginId, CancellationToken.None));
        plugin.Unload(new PluginUnloadContext(
            plugin.PluginId,
            PluginUnloadReason.GracefulShutdown,
            utcNow.AddMinutes(1),
            CancellationToken.None));

        Assert.True(response.Success);
        Assert.Equal(SyncResponseStatus.Success, response.Status);
        Assert.Equal("corr-regression-runtime", response.CorrelationId);
        Assert.Equal([utcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture)], writes);

        var recurring = Assert.Single(scheduler.RecurringSchedules);
        Assert.Equal("Timer.WriteCurrentTime.Every5Seconds", recurring.JobName);
        Assert.Equal(TimeSpan.FromSeconds(5), recurring.Interval);
        Assert.Equal("Timer.WriteCurrentTime", recurring.Operation);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Modus.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing Modus.slnx.");
    }

    private static void AssertUsingStatement(string repoRoot, params string[] pathSegments)
    {
        var filePath = Path.Combine([repoRoot, .. pathSegments]);
        var source = File.ReadAllText(filePath);

        Assert.Contains("using Modus.Core.Plugins;", source, StringComparison.Ordinal);

        foreach (var forbiddenPrefix in PluginConcernFolderMap.ForbiddenFolderDerivedNamespacePrefixes)
        {
            Assert.DoesNotContain($"using {forbiddenPrefix};", source, StringComparison.Ordinal);
        }
    }

    private sealed class HostTelemetryConsumerPlugin(string pluginId) : IHostTelemetryPluginContract
    {
        public string PluginId { get; } = pluginId;

        public string ContractName { get; } = "Modus.PluginContract";

        public Version ContractVersion { get; } = new(1, 0);
    }

    private sealed class MachineTelemetryConsumerPlugin(string pluginId) : IMachineTelemetryPluginContract
    {
        public string PluginId { get; } = pluginId;

        public string ContractName { get; } = "Modus.PluginContract";

        public Version ContractVersion { get; } = new(1, 0);
    }

    private sealed class ValidationCompliantPlugin(string pluginId) :
        IPluginContract,
        IPluginLifecycle,
        IPluginOperationCatalog,
        IPluginRegistrationPolicy,
        IEventSubscriber,
        ISyncResponder
    {
        public string PluginId { get; } = pluginId;

        public string ContractName { get; } = "Modus.PluginContract";

        public Version ContractVersion { get; } = new(1, 0);

        public IReadOnlyCollection<string> SupportedOperations { get; } = ["ping"];

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
                new PluginRegistrationStep(1, PluginRegistrationStepKind.RegisterOperation, "register-ping"),
                new PluginRegistrationStep(2, PluginRegistrationStepKind.SubscribeEvents, "subscribe-events")
            ];
        }

        public void Subscribe(IEventPublisher publisher)
        {
        }

        public SyncResponse Handle(SyncRequest request)
        {
            return new SyncResponse(Success: true, Payload: "ok");
        }
    }

    private sealed class RegistrationCompliantPlugin(IReadOnlyCollection<string> operations) :
        IPluginContract,
        IPluginLifecycle,
        IPluginOperationCatalog,
        IPluginScheduledEvents,
        IEventSubscriber,
        ISyncResponder
    {
        public string PluginId { get; } = "registration-plugin";

        public string ContractName { get; } = "Modus.PluginContract";

        public Version ContractVersion { get; } = new(1, 0);

        public IReadOnlyCollection<string> SupportedOperations { get; } = operations;

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

        public void Subscribe(IEventPublisher publisher)
        {
        }

        public void RegisterSchedules(IPluginScheduler scheduler)
        {
        }

        public SyncResponse Handle(SyncRequest request)
        {
            return new SyncResponse(Success: true, Payload: "ok");
        }
    }

    private sealed class ValidationIncompletePlugin : IPluginContract, IEventSubscriber, ISyncResponder
    {
        public string PluginId { get; } = "regression-invalid";

        public string ContractName { get; } = "Modus.PluginContract";

        public Version ContractVersion { get; } = new(1, 0);

        public void Subscribe(IEventPublisher publisher)
        {
        }

        public SyncResponse Handle(SyncRequest request)
        {
            return new SyncResponse(Success: false, Payload: "not-supported");
        }
    }

    private interface ISingletonProbe
    {
    }

    private sealed class SingletonProbe : ISingletonProbe
    {
    }

    private interface IScopedProbe
    {
    }

    private sealed class ScopedProbe : IScopedProbe
    {
    }

    private interface ITransientProbe
    {
    }

    private sealed class TransientProbe : ITransientProbe
    {
    }

    private sealed class DerivedBasePlugin(string pluginId) : PluginBase
    {
        public override string PluginId { get; } = pluginId;

        public override Version ContractVersion { get; } = new(1, 0);
    }

    private sealed class RecordingScheduler : IPluginScheduler
    {
        public List<(string JobName, TimeSpan Interval, string Operation)> RecurringSchedules { get; } = [];

        public void ScheduleRecurring(string jobName, TimeSpan interval, string operation)
        {
            RecurringSchedules.Add((jobName, interval, operation));
        }

        public void ScheduleOneTime(string jobName, DateTimeOffset when, string operation)
        {
        }

        public void ScheduleAt(string jobName, DateTimeOffset when, string operation)
        {
        }

        public bool TryCancel(string jobName)
        {
            return false;
        }
    }
}
