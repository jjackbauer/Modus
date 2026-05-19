using Modus.Core.Plugins;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class StartupAndActivationTests
{
    [Fact]
    public void WatcherOnProjectCreated_GivenPreExistingAndNewProjects_ExpectedEachProjectProcessedExactlyOnce()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-watcher-preexisting-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            var preExistingProjectPath = Path.Combine(pluginsPath, "Plugin.PreExisting.csproj");
            File.WriteAllText(preExistingProjectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

            var watcher = new PluginFolderWatcher();
            var startup = watcher.Start(pluginsPath);

            var duplicateFromCreateEvent = watcher.OnProjectCreated(preExistingProjectPath);

            var newProjectPath = Path.Combine(pluginsPath, "Plugin.NewlyCreated.csproj");
            File.WriteAllText(newProjectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            var newlyCreated = watcher.OnProjectCreated(newProjectPath);

            Assert.True(startup.HostHealthy);
            Assert.False(duplicateFromCreateEvent.EventAccepted);
            Assert.False(duplicateFromCreateEvent.PluginActivated);
            Assert.Contains(
                duplicateFromCreateEvent.Diagnostics,
                x => x.Contains(
                    $"outcome=ignored reason=duplicate file notification path={Path.GetFullPath(preExistingProjectPath)}",
                    StringComparison.Ordinal));

            Assert.True(newlyCreated.HostHealthy);
            Assert.True(newlyCreated.EventAccepted);
            Assert.True(newlyCreated.PluginActivated);
            Assert.Equal("Plugin.NewlyCreated", newlyCreated.PluginId);
            Assert.Contains(
                newlyCreated.Diagnostics,
                x => x.Contains(
                    $"outcome=accepted path={Path.GetFullPath(newProjectPath)}",
                    StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Watcher_GivenNewCsprojCreatedUnderPlugins_ExpectedCreateEventQueuedForProcessing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-watcher-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            var watcher = new PluginFolderWatcher();
            watcher.Start(pluginsPath);
            var projectPath = Path.Combine(pluginsPath, "Plugin.Queue.csproj");
            File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

            var onboarding = watcher.OnProjectCreated(projectPath);

            Assert.True(onboarding.HostHealthy);
            Assert.True(onboarding.EventAccepted);
            Assert.True(onboarding.PluginActivated);
            Assert.Equal("Plugin.Queue", onboarding.PluginId);
            Assert.Contains(
                $"stage=discovery sequence=0001 outcome=accepted path={Path.GetFullPath(projectPath)}",
                onboarding.Diagnostics,
                StringComparer.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Watcher_GivenNonCsprojFileCreated_ExpectedEventIgnoredWithoutPipelineExecution()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-watcher-ignore-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            var watcher = new PluginFolderWatcher();
            watcher.Start(pluginsPath);
            var notePath = Path.Combine(pluginsPath, "README.txt");
            File.WriteAllText(notePath, "not a project file");

            var onboarding = watcher.OnProjectCreated(notePath);

            Assert.True(onboarding.HostHealthy);
            Assert.False(onboarding.EventAccepted);
            Assert.False(onboarding.PluginActivated);
            Assert.Null(onboarding.PluginId);
            Assert.Equal(
                [
                    $"stage=discovery sequence=0001 outcome=ignored reason=non-csproj file path={Path.GetFullPath(notePath)}",
                ],
                onboarding.Diagnostics);
            Assert.DoesNotContain(onboarding.Diagnostics, x => x.Contains("stage=validation", StringComparison.Ordinal));
            Assert.DoesNotContain(onboarding.Diagnostics, x => x.Contains("stage=load", StringComparison.Ordinal));
            Assert.DoesNotContain(onboarding.Diagnostics, x => x.Contains("stage=activation", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Watcher_GivenOutOfScopeProjectAndDuplicateCreateEvent_ExpectedOnlyInScopePluginAcceptedOnce()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-watcher-scope-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            var watcher = new PluginFolderWatcher();
            watcher.Start(pluginsPath);

            var outOfScopeProjectPath = Path.Combine(pluginsPath, "Modus.Utility.csproj");
            File.WriteAllText(outOfScopeProjectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

            var inScopeProjectPath = Path.Combine(pluginsPath, "Plugin.Inventory.csproj");
            File.WriteAllText(inScopeProjectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

            var outOfScope = watcher.OnProjectCreated(outOfScopeProjectPath);
            var firstInScope = watcher.OnProjectCreated(inScopeProjectPath);
            var duplicateInScope = watcher.OnProjectCreated(inScopeProjectPath);

            Assert.True(outOfScope.HostHealthy);
            Assert.False(outOfScope.EventAccepted);
            Assert.False(outOfScope.PluginActivated);
            Assert.Contains(
                outOfScope.Diagnostics,
                x => x.Contains(
                    $"outcome=ignored reason=out-of-scope plugin project path={Path.GetFullPath(outOfScopeProjectPath)}",
                    StringComparison.Ordinal));

            Assert.True(firstInScope.HostHealthy);
            Assert.True(firstInScope.EventAccepted);
            Assert.True(firstInScope.PluginActivated);
            Assert.Equal("Plugin.Inventory", firstInScope.PluginId);
            Assert.Equal(["Plugin.Inventory"], firstInScope.ActivePluginIds);

            Assert.True(duplicateInScope.HostHealthy);
            Assert.False(duplicateInScope.EventAccepted);
            Assert.False(duplicateInScope.PluginActivated);
            Assert.Contains(
                duplicateInScope.Diagnostics,
                x => x.Contains(
                    $"outcome=ignored reason=duplicate file notification path={Path.GetFullPath(inScopeProjectPath)}",
                    StringComparison.Ordinal));
            Assert.Equal(["Plugin.Inventory"], duplicateInScope.ActivePluginIds);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Watcher_GivenMultipleCreateEvents_ExpectedDeterministicMonotonicDiscoveryOrdering()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-watcher-order-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            var watcher = new PluginFolderWatcher();
            watcher.Start(pluginsPath);

            var firstProjectPath = Path.Combine(pluginsPath, "Plugin.First.csproj");
            File.WriteAllText(firstProjectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

            var secondProjectPath = Path.Combine(pluginsPath, "Plugin.Second.csproj");
            File.WriteAllText(secondProjectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

            var first = watcher.OnProjectCreated(firstProjectPath);
            var second = watcher.OnProjectCreated(secondProjectPath);

            Assert.Contains(
                $"stage=discovery sequence=0001 outcome=accepted path={Path.GetFullPath(firstProjectPath)}",
                first.Diagnostics,
                StringComparer.Ordinal);
            Assert.Contains(
                $"stage=discovery sequence=0002 outcome=accepted path={Path.GetFullPath(secondProjectPath)}",
                second.Diagnostics,
                StringComparer.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Startup_GivenHostInitialization_ExpectedPluginFolderWatcherRegisteredForPluginsPath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-startup-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            var watcher = new PluginFolderWatcher();

            var result = watcher.Start(pluginsPath);
            var expectedPath = Path.GetFullPath(pluginsPath);

            Assert.True(result.HostHealthy);
            Assert.True(result.WatcherRegistered);
            Assert.True(result.PluginsDirectoryExists);
            Assert.Equal(expectedPath, result.PluginsPath);
            Assert.Equal(
                [
                    "stage=startup pipeline=plugin-loader outcome=initialized",
                    $"stage=startup outcome=success watcher=registered path={expectedPath}",
                ],
                result.Diagnostics);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Startup_GivenMissingPluginsDirectory_ExpectedHostUnhealthyAndReportsDiagnostic()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-startup-missing-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(root);

        try
        {
            var watcher = new PluginFolderWatcher();

            var result = watcher.Start(pluginsPath);
            var expectedPath = Path.GetFullPath(pluginsPath);

            Assert.False(result.HostHealthy);
            Assert.True(result.WatcherRegistered);
            Assert.False(result.PluginsDirectoryExists);
            Assert.Equal(expectedPath, result.PluginsPath);
            Assert.Equal(
                [
                    "stage=startup pipeline=plugin-loader outcome=initialized",
                    $"stage=startup outcome=success watcher=registered path={expectedPath}",
                    $"stage=startup outcome=failure reason=plugins directory missing path={expectedPath}",
                ],
                result.Diagnostics);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Startup_GivenPreExistingRuntimeAssemblies_ExpectedStartupPipelineActivatesDiscoveredPlugins()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-startup-runtime-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            var hostAssemblySource = typeof(HostRunner).Assembly.Location;
            var coreContractSource = typeof(IPluginContract).Assembly.Location;

            var hostAssemblyCopy = Path.Combine(pluginsPath, "host-plugin.dll");
            var coreAssemblyCopy = Path.Combine(pluginsPath, "core-plugin.dll");
            File.Copy(hostAssemblySource, hostAssemblyCopy);
            File.Copy(coreContractSource, coreAssemblyCopy);

            var watcher = new PluginFolderWatcher();
            var result = watcher.Start(pluginsPath);

            Assert.True(result.HostHealthy);
            Assert.True(result.WatcherRegistered);
            Assert.True(result.PluginsDirectoryExists);
            Assert.Contains(
                result.Diagnostics,
                x => x.Contains("stage=scan", StringComparison.Ordinal)
                    && x.Contains("outcome=success", StringComparison.Ordinal)
                    && x.Contains("plugin=Modus.Core", StringComparison.Ordinal));
            Assert.Contains(
                result.Diagnostics,
                x => x.Contains("stage=scan", StringComparison.Ordinal)
                    && x.Contains("outcome=success", StringComparison.Ordinal)
                    && x.Contains("plugin=Modus.Host", StringComparison.Ordinal));
            Assert.Contains("stage=activation plugin=Modus.Core outcome=success", result.Diagnostics, StringComparer.Ordinal);
            Assert.Contains("stage=activation plugin=Modus.Host outcome=success", result.Diagnostics, StringComparer.Ordinal);

            var lifecycleDiagnostics = result.Diagnostics
                .Where(x => x.StartsWith("stage=lifecycle ", StringComparison.Ordinal))
                .ToArray();

            Assert.Equal(
                [
                    "stage=lifecycle plugin=Plugin.Timer outcome=started source=Modus.Core",
                ],
                lifecycleDiagnostics);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Discovery_GivenPluginFolder_ExpectedDeterministicDescriptorSetProduced()
    {
        var input = new[]
        {
            new PluginDescriptor("Plugin.C", "Plugin.C", new Version(1, 0), ["Cap.C"], []),
            new PluginDescriptor("Plugin.A", "Plugin.A", new Version(1, 0), ["Cap.A"], []),
            new PluginDescriptor("Plugin.B", "Plugin.B", new Version(1, 0), ["Cap.B"], []),
        };

        var discovered = InMemoryPluginDiscoveryService.Discover(input);

        Assert.Equal(["Plugin.A", "Plugin.B", "Plugin.C"], discovered.Select(x => x.PluginId).ToArray());
    }

    [Fact]
    public void Loader_GivenInvalidPluginAssembly_ExpectedValidationErrorAndNoActivation()
    {
        var runtime = new InMemoryHostRuntime();
        var invalid = new PluginDescriptor("Plugin.Invalid", "Plugin.Invalid", new Version(1, 0), ["Cap.Invalid"], [], IsValidAssembly: false);

        var result = runtime.Start([invalid]);

        Assert.True(result.Started);
        Assert.Contains("Plugin.Invalid", result.FailedPluginIds);
        Assert.DoesNotContain("Plugin.Invalid", result.ActivatedPluginIds);
        Assert.Contains(
            "stage=validation plugin=Plugin.Invalid outcome=failure reason=invalid assembly",
            result.Diagnostics,
            StringComparer.Ordinal);
    }

    [Fact]
    public void Loader_GivenValidPluginAssembly_ExpectedRuntimeLoadWithoutThirdPartyDependency()
    {
        var descriptor = new PluginDescriptor("Plugin.Standard", "Plugin.Standard", new Version(1, 0), ["Cap.Standard"], []);

        var loadResult = InMemoryPluginLoader.Load(descriptor);

        Assert.True(loadResult.IsLoaded);
        Assert.False(loadResult.ThirdPartyDependencyDetected);
        Assert.Empty(loadResult.Diagnostics);
    }

    [Fact]
    public void Loader_GivenDisallowedDependency_ExpectedLoadRejectedAndHostContinues()
    {
        var runtime = new InMemoryHostRuntime();
        var disallowed = new PluginDescriptor(
            "Plugin.NonCompliant",
            "Plugin.NonCompliant",
            new Version(1, 0),
            ["Cap.NonCompliant"],
            [],
            IsValidAssembly: true,
            UsesOnlyStandardLibrary: false);

        var result = runtime.Start([disallowed]);

        Assert.True(result.Started);
        Assert.Empty(result.ActivatedPluginIds);
        Assert.Contains("Plugin.NonCompliant", result.FailedPluginIds);
        Assert.Contains(
            "stage=load plugin=Plugin.NonCompliant outcome=failure reason=Third-party dependency detected in runtime plugin path.",
            result.Diagnostics,
            StringComparer.Ordinal);
    }

    [Fact]
    public void Validation_GivenContractViolation_ExpectedLoadNotAttemptedAndFailureRecorded()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-validation-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            var watcher = new PluginFolderWatcher();
            var startup = watcher.Start(pluginsPath);
            var projectPath = Path.Combine(pluginsPath, "Plugin.ContractsInvalid.csproj");
            File.WriteAllText(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ModusContractCompliant>false</ModusContractCompliant></PropertyGroup></Project>");

            var onboarding = watcher.OnProjectCreated(projectPath);

            Assert.True(startup.HostHealthy);
            Assert.True(onboarding.HostHealthy);
            Assert.True(onboarding.EventAccepted);
            Assert.False(onboarding.PluginActivated);
            Assert.Equal("Plugin.ContractsInvalid", onboarding.PluginId);
            Assert.Contains("Plugin.ContractsInvalid", onboarding.FailedPluginIds);
            Assert.Contains(
                "stage=validation plugin=Plugin.ContractsInvalid outcome=failure reason=contract violation",
                onboarding.Diagnostics,
                StringComparer.Ordinal);
            Assert.DoesNotContain(
                onboarding.Diagnostics,
                x => x.Contains("stage=load plugin=Plugin.ContractsInvalid", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Activation_GivenSuccessfullyLoadedPlugin_ExpectedStateProgressionToActive()
    {
        var engine = new InMemoryLifecycleEngine();
        var plugin = new PluginSpec("Plugin.ActivationOk", IsValid: true, FailOnActivation: false);

        var result = engine.HotLoad(plugin);

        Assert.True(result.HostHealthy);
        Assert.False(result.Quarantined);
        Assert.Equal(
            [
                PluginRuntimeState.Discovered,
                PluginRuntimeState.Validated,
                PluginRuntimeState.Loaded,
                PluginRuntimeState.Registered,
                PluginRuntimeState.Activated,
                PluginRuntimeState.Active,
            ],
            result.Transitions);
        Assert.Contains(plugin.PluginId, engine.ActivePluginIds);
    }

    [Fact]
    public void Activation_GivenActivationFailure_ExpectedRollbackOrFailedStateWithoutCorruptingRuntime()
    {
        var runtime = new InMemoryHostRuntime();
        var plugins = new[]
        {
            new PluginDescriptor("Plugin.Healthy", "Plugin.Healthy", new Version(1, 0), ["Cap.Shared"], []),
            new PluginDescriptor("Plugin.Faulty", "Plugin.Faulty", new Version(2, 0), ["Cap.Shared"], [], FailOnActivation: true),
        };

        var result = runtime.Start(plugins);

        Assert.True(result.Started);
        Assert.Contains("Plugin.Healthy", result.ActivatedPluginIds);
        Assert.Contains("Plugin.Faulty", result.FailedPluginIds);
        Assert.Equal("Plugin.Healthy", result.CapabilityOwners["Cap.Shared"]);
        Assert.Contains(
            "stage=activation plugin=Plugin.Faulty outcome=failure reason=activation exception",
            result.Diagnostics,
            StringComparer.Ordinal);
    }

    [Fact]
    public void Isolation_GivenPluginValidationFailure_ExpectedHostProcessesSubsequentPluginEvents()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-isolation-validation-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            var watcher = new PluginFolderWatcher();
            var startup = watcher.Start(pluginsPath);

            var invalidProjectPath = Path.Combine(pluginsPath, "Plugin.InvalidValidation.csproj");
            File.WriteAllText(
                invalidProjectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ModusContractCompliant>false</ModusContractCompliant></PropertyGroup></Project>");

            var validProjectPath = Path.Combine(pluginsPath, "Plugin.ValidAfterFailure.csproj");
            File.WriteAllText(
                validProjectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

            var failedOnboarding = watcher.OnProjectCreated(invalidProjectPath);
            var successfulOnboarding = watcher.OnProjectCreated(validProjectPath);

            Assert.True(startup.HostHealthy);

            Assert.True(failedOnboarding.HostHealthy);
            Assert.True(failedOnboarding.EventAccepted);
            Assert.False(failedOnboarding.PluginActivated);
            Assert.Contains("Plugin.InvalidValidation", failedOnboarding.FailedPluginIds);

            Assert.True(successfulOnboarding.HostHealthy);
            Assert.True(successfulOnboarding.EventAccepted);
            Assert.True(successfulOnboarding.PluginActivated);
            Assert.Equal("Plugin.ValidAfterFailure", successfulOnboarding.PluginId);
            Assert.Contains("Plugin.ValidAfterFailure", successfulOnboarding.ActivePluginIds);
            Assert.Contains("Plugin.InvalidValidation", successfulOnboarding.FailedPluginIds);
            Assert.Contains(
                $"stage=discovery sequence=0002 outcome=accepted path={Path.GetFullPath(validProjectPath)}",
                successfulOnboarding.Diagnostics,
                StringComparer.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Isolation_GivenPluginActivationException_ExpectedHostHealthyAndFaultContained()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-isolation-activation-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            var watcher = new PluginFolderWatcher();
            var startup = watcher.Start(pluginsPath);

            var failingProjectPath = Path.Combine(pluginsPath, "Plugin.ThrowsOnActivation.csproj");
            File.WriteAllText(
                failingProjectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ModusFailOnActivation>true</ModusFailOnActivation></PropertyGroup></Project>");

            var healthyProjectPath = Path.Combine(pluginsPath, "Plugin.Continues.csproj");
            File.WriteAllText(
                healthyProjectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

            var failedOnboarding = watcher.OnProjectCreated(failingProjectPath);
            var successfulOnboarding = watcher.OnProjectCreated(healthyProjectPath);

            Assert.True(startup.HostHealthy);

            Assert.True(failedOnboarding.HostHealthy);
            Assert.True(failedOnboarding.EventAccepted);
            Assert.False(failedOnboarding.PluginActivated);
            Assert.Equal("Plugin.ThrowsOnActivation", failedOnboarding.PluginId);
            Assert.Contains("Plugin.ThrowsOnActivation", failedOnboarding.FailedPluginIds);
            Assert.Contains(
                "stage=activation plugin=Plugin.ThrowsOnActivation outcome=failure reason=activation exception",
                failedOnboarding.Diagnostics,
                StringComparer.Ordinal);

            Assert.True(successfulOnboarding.HostHealthy);
            Assert.True(successfulOnboarding.EventAccepted);
            Assert.True(successfulOnboarding.PluginActivated);
            Assert.Equal("Plugin.Continues", successfulOnboarding.PluginId);
            Assert.Contains("Plugin.Continues", successfulOnboarding.ActivePluginIds);
            Assert.DoesNotContain("Plugin.ThrowsOnActivation", successfulOnboarding.ActivePluginIds);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Registration_GivenValidPlugins_ExpectedDeterministicActivationOrder()
    {
        var runtime = new InMemoryHostRuntime();
        var plugins = new[]
        {
            new PluginDescriptor("Plugin.C", "Plugin.C", new Version(1, 0), ["Cap.C"], ["Plugin.A"]),
            new PluginDescriptor("Plugin.B", "Plugin.B", new Version(1, 0), ["Cap.B"], ["Plugin.A"]),
            new PluginDescriptor("Plugin.A", "Plugin.A", new Version(1, 0), ["Cap.A"], []),
        };

        var result = runtime.Start(plugins);

        Assert.True(result.Started);
        Assert.Equal(["Plugin.A", "Plugin.B", "Plugin.C"], result.ActivatedPluginIds);
    }

    [Fact]
    public void Registration_GivenCapabilityConflicts_ExpectedDeterministicResolutionPolicyApplied()
    {
        var runtime = new InMemoryHostRuntime();
        var plugins = new[]
        {
            new PluginDescriptor("Plugin.Low", "Plugin.Low", new Version(1, 0), ["Cap.Shared"], []),
            new PluginDescriptor("Plugin.High", "Plugin.High", new Version(2, 0), ["Cap.Shared"], []),
        };

        var result = runtime.Start(plugins);

        Assert.True(result.Started);
        Assert.Equal("Plugin.High", result.CapabilityOwners["Cap.Shared"]);
    }

    [Fact]
    public void Registration_GivenFaultyPlugin_ExpectedIsolationAndHostContinuity()
    {
        var runtime = new InMemoryHostRuntime();
        var plugins = new[]
        {
            new PluginDescriptor("Plugin.Healthy", "Plugin.Healthy", new Version(1, 0), ["Cap.Healthy"], []),
            new PluginDescriptor("Plugin.Faulty", "Plugin.Faulty", new Version(1, 0), ["Cap.Faulty"], [], FailOnActivation: true),
        };

        var result = runtime.Start(plugins);

        Assert.True(result.Started);
        Assert.Contains("Plugin.Healthy", result.ActivatedPluginIds);
        Assert.Contains("Plugin.Faulty", result.FailedPluginIds);
        Assert.Contains(
            "stage=activation plugin=Plugin.Faulty outcome=failure reason=activation exception",
            result.Diagnostics,
            StringComparer.Ordinal);
    }

    [Fact]
    public void Registration_GivenDependencyFailsValidation_ExpectedDependentPluginSkippedFromActivation()
    {
        var runtime = new InMemoryHostRuntime();
        var plugins = new[]
        {
            new PluginDescriptor(
                "Plugin.DependencyInvalid",
                "Plugin.DependencyInvalid",
                new Version(1, 0),
                ["Cap.Dependency"],
                [],
                IsContractCompliant: false),
            new PluginDescriptor(
                "Plugin.Dependent",
                "Plugin.Dependent",
                new Version(1, 0),
                ["Cap.Dependent"],
                ["Plugin.DependencyInvalid"]),
        };

        var result = runtime.Start(plugins);

        Assert.True(result.Started);
        Assert.DoesNotContain("Plugin.Dependent", result.ActivatedPluginIds);
        Assert.Contains("Plugin.DependencyInvalid", result.FailedPluginIds);
        Assert.Contains("Plugin.Dependent", result.FailedPluginIds);
        Assert.Contains(
            "stage=activation plugin=Plugin.Dependent outcome=failure reason=dependency unavailable: Plugin.DependencyInvalid",
            result.Diagnostics,
            StringComparer.Ordinal);
    }

    [Fact]
    public void Deduplication_GivenRepeatedCsprojCreateNotifications_ExpectedSinglePluginActivation()
    {
        var runtime = new InMemoryHostRuntime();
        var repeated = new PluginDescriptor("Plugin.Payments", "Plugin.Payments", new Version(1, 0), ["Cap.Payments"], []);

        var result = runtime.Start([repeated, repeated, repeated]);

        Assert.True(result.Started);
        Assert.Single(result.ActivatedPluginIds, "Plugin.Payments");
        Assert.DoesNotContain("Plugin.Payments", result.FailedPluginIds);
    }

    [Fact]
    public void Deduplication_GivenAlreadyActivePluginProject_ExpectedNoDuplicateLoadAndDiagnosticLogged()
    {
        var runtime = new InMemoryHostRuntime();
        var first = new PluginDescriptor("Plugin.Catalog", "Plugin.Catalog", new Version(1, 0), ["Cap.Catalog"], []);
        var duplicate = new PluginDescriptor("Plugin.Catalog", "Plugin.Catalog", new Version(1, 0), ["Cap.Catalog"], []);

        var result = runtime.Start([first, duplicate]);

        Assert.True(result.Started);
        Assert.Single(result.ActivatedPluginIds, "Plugin.Catalog");
        Assert.Contains(result.Diagnostics, x => x.Contains("duplicate plugin ignored: Plugin.Catalog", StringComparison.Ordinal));
    }

    [Fact]
    public void Diagnostics_GivenSuccessfulPipeline_ExpectedDiscoveryValidationLoadActivationMessagesInOrder()
    {
        var runtime = new InMemoryHostRuntime();
        var plugin = new PluginDescriptor("Plugin.Observable", "Plugin.Observable", new Version(1, 0), ["Cap.Observable"], []);

        var result = runtime.Start([plugin]);

        Assert.Equal(
            [
                "stage=discovery plugin=Plugin.Observable outcome=success",
                "stage=validation plugin=Plugin.Observable outcome=success",
                "stage=load plugin=Plugin.Observable outcome=success",
                "stage=registration plugin=Plugin.Observable outcome=success",
                "stage=activation plugin=Plugin.Observable outcome=success",
                "stage=operation plugin=Plugin.Observable operation=Op.Plugin.Observable.HealthCheck outcome=success",
            ],
            result.Diagnostics);
    }

    [Fact]
    public void Diagnostics_GivenPipelineFailure_ExpectedFailureStageAndReasonIncludedInDiagnosticOutput()
    {
        var runtime = new InMemoryHostRuntime();
        var invalid = new PluginDescriptor("Plugin.Bad", "Plugin.Bad", new Version(1, 0), ["Cap.Bad"], [], IsValidAssembly: false);

        var result = runtime.Start([invalid]);

        Assert.Contains(
            "stage=validation plugin=Plugin.Bad outcome=failure reason=invalid assembly",
            result.Diagnostics,
            StringComparer.Ordinal);
    }

    [Fact]
    public void Startup_GivenValidPluginSet_ExpectedHostStartsAndAllPluginsActivated()
    {
        var runtime = new InMemoryHostRuntime();
        var plugins = new[]
        {
            new PluginDescriptor("Plugin.Auth", "Plugin.Auth", new Version(1, 0), ["Cap.Auth"], []),
            new PluginDescriptor("Plugin.Billing", "Plugin.Billing", new Version(1, 0), ["Cap.Billing"], []),
            new PluginDescriptor("Plugin.Notifications", "Plugin.Notifications", new Version(1, 0), ["Cap.Notifications"], []),
        };

        var result = runtime.Start(plugins);

        Assert.True(result.Started);
        Assert.Equal(3, result.ActivatedPluginIds.Count);
        Assert.Empty(result.FailedPluginIds);
    }
}
