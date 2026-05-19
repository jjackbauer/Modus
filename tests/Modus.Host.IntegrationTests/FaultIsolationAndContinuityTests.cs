using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class FaultIsolationAndContinuityTests
{
    [Fact]
    public void Registration_GivenLoadFailure_ExpectedIsolationBoundaryAndHostContinuityDiagnostics()
    {
        var runtime = new InMemoryHostRuntime();

        var unhealthy = new PluginDescriptor(
            "Plugin.UnhealthyLoad",
            "Plugin.UnhealthyLoad",
            new Version(1, 0, 0),
            ["Cap.UnhealthyLoad"],
            [],
            UsesOnlyStandardLibrary: false);

        var healthy = new PluginDescriptor(
            "Plugin.Healthy",
            "Plugin.Healthy",
            new Version(1, 0, 0),
            ["Cap.Healthy"],
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
            "Plugin.Faulty",
            "Plugin.Faulty",
            new Version(1, 0, 0),
            ["Cap.Faulty"],
            [],
            FailOnActivation: true);

        var healthy = new PluginDescriptor(
            "Plugin.Healthy",
            "Plugin.Healthy",
            new Version(1, 0, 0),
            ["Cap.Healthy"],
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
}
