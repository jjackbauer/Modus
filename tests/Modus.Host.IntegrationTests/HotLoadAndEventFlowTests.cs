using Modus.Core.Events;
using Modus.Core.Plugins;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class HotLoadAndEventFlowTests
{
    [Fact]
    public void LifecycleStateMachine_GivenValidPlugin_ExpectedOrderedStateProgressionToActive()
    {
        var engine = new InMemoryLifecycleEngine();
        var plugin = new PluginSpec("Plugin.Valid", IsValid: true, FailOnActivation: false);

        var result = engine.HotLoad(plugin);

        Assert.True(result.HostHealthy);
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
    }

    [Fact]
    public void LifecycleStateMachine_GivenValidationFailure_ExpectedTransitionToFailedWithoutHostTermination()
    {
        var engine = new InMemoryLifecycleEngine();
        var plugin = new PluginSpec("Plugin.Invalid", IsValid: false, FailOnActivation: false);

        var result = engine.HotLoad(plugin);

        Assert.True(result.HostHealthy);
        Assert.Equal([PluginRuntimeState.Discovered, PluginRuntimeState.Failed], result.Transitions);
        Assert.Contains("validation failed", result.Diagnostics, StringComparer.Ordinal);
    }

    [Fact]
    public void Rollback_GivenActivationFailureAfterRegistration_ExpectedStateTransitionToRollbackPendingThenFailed()
    {
        var engine = new InMemoryLifecycleEngine();
        var plugin = new PluginSpec("Plugin.Rollback", IsValid: true, FailOnActivation: true);

        var result = engine.HotLoad(plugin);

        Assert.True(result.HostHealthy);
        Assert.Equal(
            [
                PluginRuntimeState.Discovered,
                PluginRuntimeState.Validated,
                PluginRuntimeState.Loaded,
                PluginRuntimeState.Registered,
                PluginRuntimeState.RollbackPending,
                PluginRuntimeState.Failed,
            ],
            result.Transitions);
        Assert.DoesNotContain("Plugin.Rollback", engine.ActivePluginIds);
        Assert.Contains("rollback complete", result.Diagnostics, StringComparer.Ordinal);
    }

    [Fact]
    public void HotUnload_GivenPluginDeactivationRequest_ExpectedTransitionFromActiveToDeactivatingToUnloaded()
    {
        var engine = new InMemoryLifecycleEngine();
        var plugin = new PluginSpec("Plugin.Unload", IsValid: true, FailOnActivation: false);
        engine.HotLoad(plugin);

        var unload = engine.HotUnload(plugin.PluginId);

        Assert.True(unload.HostHealthy);
        Assert.Equal([PluginRuntimeState.Deactivating, PluginRuntimeState.Unloaded], unload.Transitions);
        Assert.DoesNotContain(plugin.PluginId, engine.ActivePluginIds);
    }

    [Fact]
    public void LifecyclePhases_GivenHotLoad_ExpectedDeterministicPhaseDiagnostics()
    {
        var engine = new InMemoryLifecycleEngine();
        var plugin = new PluginSpec("Plugin.Phases", IsValid: true, FailOnActivation: false);

        var result = engine.HotLoad(plugin);

        Assert.Equal(
            [
                "phase=discovered plugin=Plugin.Phases outcome=success",
                "phase=validated plugin=Plugin.Phases outcome=success",
                "phase=loaded plugin=Plugin.Phases outcome=success",
                "phase=registered plugin=Plugin.Phases outcome=success",
                "phase=activated plugin=Plugin.Phases outcome=success",
                "phase=active plugin=Plugin.Phases outcome=success",
            ],
            result.Diagnostics);
    }

    [Fact]
    public void LifecyclePhases_GivenHotUnload_ExpectedDeterministicPhaseDiagnosticsAndRegistryCleanup()
    {
        var engine = new InMemoryLifecycleEngine();
        var plugin = new PluginSpec("Plugin.Unload.Phases", IsValid: true, FailOnActivation: false);
        engine.HotLoad(plugin);

        var unload = engine.HotUnload(plugin.PluginId);

        Assert.Equal(
            [
                "phase=deactivating plugin=Plugin.Unload.Phases outcome=success",
                "phase=unloaded plugin=Plugin.Unload.Phases outcome=success",
            ],
            unload.Diagnostics);
        Assert.DoesNotContain(plugin.PluginId, engine.ActivePluginIds);
    }

    [Fact]
    public void RetryPolicy_GivenRepeatedPluginFailures_ExpectedPluginQuarantinedAfterConfiguredThreshold()
    {
        var engine = new InMemoryLifecycleEngine(quarantineThreshold: 3);
        var plugin = new PluginSpec("Plugin.Flaky", IsValid: true, FailOnActivation: true);

        engine.HotLoad(plugin);
        engine.HotLoad(plugin);
        var third = engine.HotLoad(plugin);

        Assert.True(third.HostHealthy);
        Assert.True(third.Quarantined);
        Assert.Equal(3, engine.GetFailureCount(plugin.PluginId));
        Assert.Contains(plugin.PluginId, engine.QuarantinedPluginIds);
    }

    [Fact]
    public void RetryPolicy_GivenSuccessfulActivationBetweenFailures_ExpectedConsecutiveFailureCounterResets()
    {
        var engine = new InMemoryLifecycleEngine(quarantineThreshold: 3);
        const string pluginId = "Plugin.Flaky.Reset";

        engine.HotLoad(new PluginSpec(pluginId, IsValid: true, FailOnActivation: true));
        engine.HotLoad(new PluginSpec(pluginId, IsValid: true, FailOnActivation: true));

        var recovered = engine.HotLoad(new PluginSpec(pluginId, IsValid: true, FailOnActivation: false));
        var failedAfterRecovery = engine.HotLoad(new PluginSpec(pluginId, IsValid: true, FailOnActivation: true));

        Assert.True(recovered.HostHealthy);
        Assert.False(recovered.Quarantined);
        Assert.False(failedAfterRecovery.Quarantined);
        Assert.Equal(1, engine.GetFailureCount(pluginId));
        Assert.DoesNotContain(pluginId, engine.QuarantinedPluginIds);
    }

    [Fact]
    public void RetryPolicy_GivenAlreadyQuarantinedPlugin_ExpectedHotLoadRejectedWithoutRetry()
    {
        var engine = new InMemoryLifecycleEngine(quarantineThreshold: 2);
        const string pluginId = "Plugin.Flaky.Quarantined";

        engine.HotLoad(new PluginSpec(pluginId, IsValid: true, FailOnActivation: true));
        var secondFailure = engine.HotLoad(new PluginSpec(pluginId, IsValid: true, FailOnActivation: true));
        var rejected = engine.HotLoad(new PluginSpec(pluginId, IsValid: true, FailOnActivation: true));

        Assert.True(secondFailure.Quarantined);
        Assert.True(rejected.Quarantined);
        Assert.Equal([PluginRuntimeState.Discovered, PluginRuntimeState.Failed], rejected.Transitions);
        Assert.Contains(
            $"phase=discovered plugin={pluginId} outcome=rejected reason=quarantined",
            rejected.Diagnostics,
            StringComparer.Ordinal);
        Assert.Contains("plugin quarantined", rejected.Diagnostics, StringComparer.Ordinal);
        Assert.Equal(2, engine.GetFailureCount(pluginId));
    }

    [Fact]
    public void EventFlow_GivenPublishedEvent_ExpectedCrossModuleHandlerExecutionThroughContracts()
    {
        var engine = new InMemoryLifecycleEngine();
        var auth = new PluginSpec("Plugin.Auth", IsValid: true, FailOnActivation: false);
        var billing = new PluginSpec("Plugin.Billing", IsValid: true, FailOnActivation: false);
        engine.HotLoad(auth);
        engine.HotLoad(billing);

        var dispatch = engine.Publish(new DomainEvent("OrderPlaced"));

        Assert.Equal(2, dispatch.DeliveredCount);
        Assert.Equal(1, engine.GetReceivedEvents("Plugin.Auth"));
        Assert.Equal(1, engine.GetReceivedEvents("Plugin.Billing"));
    }

    [Fact]
    public void EventFlow_GivenPluginHotUnloaded_ExpectedUnloadedPluginStopsReceivingEvents()
    {
        var engine = new InMemoryLifecycleEngine();
        var auth = new PluginSpec("Plugin.Auth.Unload", IsValid: true, FailOnActivation: false);
        var billing = new PluginSpec("Plugin.Billing.Unload", IsValid: true, FailOnActivation: false);

        engine.HotLoad(auth);
        engine.HotLoad(billing);

        var beforeUnload = engine.Publish(new DomainEvent("OrderPlaced"));
        var unload = engine.HotUnload(billing.PluginId);
        var afterUnload = engine.Publish(new DomainEvent("OrderRefunded"));

        Assert.True(unload.HostHealthy);
        Assert.Equal([PluginRuntimeState.Deactivating, PluginRuntimeState.Unloaded], unload.Transitions);
        Assert.Equal(2, beforeUnload.DeliveredCount);
        Assert.Equal(1, afterUnload.DeliveredCount);
        Assert.Equal(2, engine.GetReceivedEvents(auth.PluginId));
        Assert.Equal(1, engine.GetReceivedEvents(billing.PluginId));
        Assert.DoesNotContain(billing.PluginId, engine.ActivePluginIds);
    }

    [Fact]
    public void HotLoadIntegration_GivenRuntimePluginAddAndRemove_ExpectedStableHostState()
    {
        var engine = new InMemoryLifecycleEngine();

        for (var i = 0; i < 10; i++)
        {
            var pluginId = $"Plugin.Cycle.{i}";
            var plugin = new PluginSpec(pluginId, IsValid: true, FailOnActivation: false);

            var load = engine.HotLoad(plugin);
            Assert.True(load.HostHealthy);

            var unload = engine.HotUnload(pluginId);
            Assert.True(unload.HostHealthy);
        }

        Assert.Empty(engine.ActivePluginIds);
        Assert.Empty(engine.QuarantinedPluginIds);
        Assert.True(engine.HostHealthy);
    }

    [Fact]
    public void EndToEnd_GivenHostStartedAndNewCsprojInPlugins_ExpectedPluginValidatedLoadedAndActivated()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-e2e-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            var watcher = new PluginFolderWatcher();
            var startup = watcher.Start(pluginsPath);
            var projectPath = Path.Combine(pluginsPath, "Plugin.Inventory.csproj");
            File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

            var onboarding = watcher.OnProjectCreated(projectPath);

            Assert.True(startup.HostHealthy);
            Assert.True(startup.WatcherRegistered);
            Assert.True(startup.PluginsDirectoryExists);

            Assert.True(onboarding.HostHealthy);
            Assert.True(onboarding.EventAccepted);
            Assert.True(onboarding.PluginActivated);
            Assert.Equal("Plugin.Inventory", onboarding.PluginId);
            Assert.Contains("Plugin.Inventory", onboarding.ActivePluginIds);
            Assert.Contains("stage=activation plugin=Plugin.Inventory outcome=success", onboarding.Diagnostics, StringComparer.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EndToEnd_GivenPluginOnboardingFailure_ExpectedHostRemainsHealthyAndContinuesWatching()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-e2e-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            var watcher = new PluginFolderWatcher();
            var startup = watcher.Start(pluginsPath);

            var badProjectPath = Path.Combine(pluginsPath, "Plugin.Bad.csproj");
            File.WriteAllText(
                badProjectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ModusIsValidAssembly>false</ModusIsValidAssembly></PropertyGroup></Project>");

            var badOnboarding = watcher.OnProjectCreated(badProjectPath);

            var goodProjectPath = Path.Combine(pluginsPath, "Plugin.Good.csproj");
            File.WriteAllText(goodProjectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

            var goodOnboarding = watcher.OnProjectCreated(goodProjectPath);

            Assert.True(startup.HostHealthy);

            Assert.True(badOnboarding.HostHealthy);
            Assert.True(badOnboarding.EventAccepted);
            Assert.False(badOnboarding.PluginActivated);
            Assert.Contains("Plugin.Bad", badOnboarding.FailedPluginIds);

            Assert.True(goodOnboarding.HostHealthy);
            Assert.True(goodOnboarding.PluginActivated);
            Assert.Contains("Plugin.Good", goodOnboarding.ActivePluginIds);
            Assert.DoesNotContain("Plugin.Good", goodOnboarding.FailedPluginIds);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
