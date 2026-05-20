using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Modus.Host.Diagnostics;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class FaultIsolationAndContinuityTests
{
    [Fact]
    public void Registration_GivenLoadFailure_ExpectedIsolationBoundaryAndHostContinuityDiagnostics()
    {
        var runtime = new InMemoryHostRuntime();

        var unhealthy = new PluginDescriptor(
            new PluginId("Plugin.UnhealthyLoad"),
            "Plugin.UnhealthyLoad",
            new Version(1, 0, 0),
            [new CapabilityName("Cap.UnhealthyLoad")],
            [],
            UsesOnlyStandardLibrary: false);

        var healthy = new PluginDescriptor(
            new PluginId("Plugin.Healthy"),
            "Plugin.Healthy",
            new Version(1, 0, 0),
            [new CapabilityName("Cap.Healthy")],
            []);

        var result = runtime.Start([unhealthy, healthy]);

        Assert.True(result.Started);
        Assert.Contains("Plugin.UnhealthyLoad", result.FailedPluginIds);
        Assert.Contains("Plugin.Healthy", result.ActivatedPluginIds);
        Assert.Contains(
            "stage=isolation plugin=Plugin.UnhealthyLoad failed-stage=load outcome=isolated",
            result.Diagnostics,
            StringComparer.Ordinal);
        Assert.Contains(
            "stage=continuity outcome=preserved",
            result.Diagnostics,
            StringComparer.Ordinal);
    }

    [Fact]
    public void Registration_GivenActivationFailure_ExpectedIsolationBoundaryAndHostContinuityDiagnostics()
    {
        var runtime = new InMemoryHostRuntime();

        var faulty = new PluginDescriptor(
            new PluginId("Plugin.Faulty"),
            "Plugin.Faulty",
            new Version(1, 0, 0),
            [new CapabilityName("Cap.Faulty")],
            [],
            FailOnActivation: true);

        var healthy = new PluginDescriptor(
            new PluginId("Plugin.Healthy"),
            "Plugin.Healthy",
            new Version(1, 0, 0),
            [new CapabilityName("Cap.Healthy")],
            []);

        var result = runtime.Start([faulty, healthy]);

        Assert.True(result.Started);
        Assert.Contains("Plugin.Faulty", result.FailedPluginIds);
        Assert.Contains("Plugin.Healthy", result.ActivatedPluginIds);
        Assert.Contains(
            "stage=rollback plugin=Plugin.Faulty outcome=success reverted=activation,registration",
            result.Diagnostics,
            StringComparer.Ordinal);
        Assert.Contains(
            "stage=isolation plugin=Plugin.Faulty failed-stage=activation outcome=isolated",
            result.Diagnostics,
            StringComparer.Ordinal);
        Assert.Contains(
            "stage=continuity outcome=preserved",
            result.Diagnostics,
            StringComparer.Ordinal);
    }

    [Fact]
    public void PluginFailureReporter_StageFailure_GivenTypedPluginId_ProducesExpectedDiagnosticString()
    {
        var reporter = new PluginFailureReporter();

        var result = reporter.StageFailure("load", new PluginId("Plugin.Test"), "load failed");

        Assert.Equal("stage=load plugin=Plugin.Test outcome=failure reason=load failed", result);
    }

    [Fact]
    public void PluginFailureReporter_OperationFailure_GivenTypedOperationName_ProducesExpectedDiagnosticString()
    {
        var reporter = new PluginFailureReporter();

        var result = reporter.OperationFailure(new PluginId("Plugin.Test"), new OperationName("Op.HealthCheck"), "operation exception");

        Assert.Equal("stage=operation plugin=Plugin.Test operation=Op.HealthCheck outcome=failure reason=operation exception", result);
    }

    [Fact]
    public void PluginFailureReporter_Isolation_GivenTypedPluginId_ProducesExpectedDiagnosticString()
    {
        var reporter = new PluginFailureReporter();

        var result = reporter.Isolation("activation", new PluginId("Plugin.Test"));

        Assert.Equal("stage=isolation plugin=Plugin.Test failed-stage=activation outcome=isolated", result);
    }
}
