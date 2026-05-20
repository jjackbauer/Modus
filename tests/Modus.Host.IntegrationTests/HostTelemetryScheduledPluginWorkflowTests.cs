using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Modus.SamplePlugins.Telemetry;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class HostTelemetryScheduledPluginWorkflowTests
{
    [Fact]
    public void TelemetryPluginContract_GivenCompliantImplementation_ExpectedValidationPassesScheduledCapabilities()
    {
        var plugin = new HostTelemetryPlugin();
        var scheduler = new RecordingPluginScheduler();

        var validation = PluginContractValidator.Validate(
            plugin,
            new PluginContractValidationPolicy
            {
                RequireScheduledEventsCapability = true,
                RequireDeterministicRegistrationLifecycle = false,
            });

        plugin.RegisterSchedules(scheduler);

        Assert.True(validation.IsValid);
        Assert.Empty(validation.MissingCapabilities);

        var recurring = Assert.Single(scheduler.RecurringSchedules);
        Assert.Equal(new JobName("Telemetry.Host.CollectSnapshot.EverySecond"), recurring.JobName);
        Assert.Equal(TimeSpan.FromSeconds(1), recurring.Interval);
        Assert.Equal(new OperationName("Telemetry.Host.CollectSnapshot"), recurring.Operation);
        Assert.Contains(recurring.Operation, plugin.SupportedOperations);
    }

    [Fact]
    public void TelemetryPluginOperations_GivenHandleTelemetryCollection_ExpectedPayloadContainsCpuMemoryAndGcMetrics()
    {
        var plugin = new HostTelemetryPlugin();
        plugin.Start(new PluginStartContext(plugin.PluginId, CancellationToken.None));

        var response = plugin.Handle(SyncRequest.ForStandardPath(new OperationName("Telemetry.Host.CollectSnapshot"), correlationId: new CorrelationId("corr-telemetry")));

        Assert.True(response.Success);
        Assert.Equal(SyncResponseStatus.Success, response.Status);
        Assert.Equal(new CorrelationId("corr-telemetry"), response.CorrelationId);
        Assert.Contains("cpuPercent=", response.Payload, StringComparison.Ordinal);
        Assert.Contains("workingSetBytes=", response.Payload, StringComparison.Ordinal);
        Assert.Contains("managedHeapBytes=", response.Payload, StringComparison.Ordinal);
        Assert.Contains("gcGen0=", response.Payload, StringComparison.Ordinal);
        Assert.Contains("gcGen1=", response.Payload, StringComparison.Ordinal);
        Assert.Contains("gcGen2=", response.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public void TelemetryPluginHostStartup_GivenPluginsPathContainsTelemetryAssembly_ExpectedLifecycleSchedulingAndOperationDiagnostics()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-telemetry-runtime-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            var telemetryAssemblySource = typeof(HostTelemetryPlugin).Assembly.Location;
            var telemetryAssemblyCopy = Path.Combine(pluginsPath, Path.GetFileName(telemetryAssemblySource));
            File.Copy(telemetryAssemblySource, telemetryAssemblyCopy);

            var watcher = new PluginFolderWatcher();
            var result = watcher.Start(pluginsPath);

            Assert.True(result.HostHealthy);
            Assert.Contains(
                result.Diagnostics,
                static x => x.Contains("stage=startup outcome=success watcher=registered", StringComparison.Ordinal));
            Assert.Contains(
                result.Diagnostics,
                static x => x.Contains("stage=activation plugin=Plugin.Host.Telemetry outcome=success", StringComparison.Ordinal));
            Assert.Contains(
                result.Diagnostics,
                static x => x.Contains("stage=lifecycle plugin=Plugin.Host.Telemetry outcome=started source=Plugin.Host.Telemetry", StringComparison.Ordinal));
            Assert.Contains(
                result.Diagnostics,
                static x => x.Contains("stage=scheduling plugin=Plugin.Host.Telemetry job=Telemetry.Host.CollectSnapshot.EverySecond", StringComparison.Ordinal)
                    && x.Contains("outcome=registered", StringComparison.Ordinal));
            Assert.Contains(
                result.Diagnostics,
                static x => x.Contains("stage=operation plugin=Plugin.Host.Telemetry operation=Telemetry.Host.CollectSnapshot", StringComparison.Ordinal)
                    && x.Contains("source=scheduled", StringComparison.Ordinal)
                    && x.Contains("outcome=success", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class RecordingPluginScheduler : IPluginScheduler
    {
        public List<RecurringSchedule> RecurringSchedules { get; } = [];

        public void ScheduleRecurring(JobName jobName, TimeSpan interval, OperationName operation)
        {
            RecurringSchedules.Add(new RecurringSchedule(jobName, interval, operation));
        }

        public void ScheduleAt(JobName jobName, DateTimeOffset runAt, OperationName operation)
        {
        }
    }

    private sealed record RecurringSchedule(JobName JobName, TimeSpan Interval, OperationName Operation);
}
