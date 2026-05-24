using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Modus.Core.Hosting;
using Modus.Host.Plugins.Host;
using Microsoft.Extensions.DependencyInjection;
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
        var operationPayload = Assert.IsType<TelemetryOperationPayload>(response.Payload);
        Assert.Null(operationPayload.Error);
        Assert.NotNull(operationPayload.Result);
        var payload = operationPayload.Result!;
        Assert.Equal("Plugin.Host.Telemetry", payload.PluginId);
        Assert.Equal("Telemetry.Host.CollectSnapshot", payload.Operation);
        Assert.Equal("host", payload.Source);
        Assert.Equal("runtime", payload.Category);
        Assert.Contains(payload.Measurements, static m => m is { Name: "cpu.percent", Unit: "percent", Kind: "gauge" } && m.Value >= 0d);
        Assert.Contains(payload.Measurements, static m => m is { Name: "memory.workingSet.bytes", Unit: "bytes", Kind: "gauge" } && m.Value >= 0d);
        Assert.Contains(payload.Measurements, static m => m is { Name: "memory.managedHeap.bytes", Unit: "bytes", Kind: "gauge" });
        Assert.Contains(payload.Measurements, static m => m is { Name: "gc.collections.gen0", Unit: "count", Kind: "counter" });
        Assert.Contains(payload.Measurements, static m => m is { Name: "gc.collections.gen1", Unit: "count", Kind: "counter" });
        Assert.Contains(payload.Measurements, static m => m is { Name: "gc.collections.gen2", Unit: "count", Kind: "counter" });
        Assert.False(string.IsNullOrWhiteSpace(payload.Metadata["machineName"]));
        Assert.False(string.IsNullOrWhiteSpace(payload.Metadata["processName"]));
        Assert.True(int.TryParse(payload.Metadata["processId"], out _));
        Assert.True(int.TryParse(payload.Metadata["processorCount"], out var processorCount));
        Assert.True(processorCount > 0);
    }

    [Fact]
    [Trait("ChecklistItem", "Refactor machine and host telemetry plugin handlers to return typed measurements and structured metadata instead of log-only outputs")]
    public void MachineTelemetryPluginOperations_GivenHandleTelemetryCollection_ExpectedPayloadContainsStructuredMeasurementsAndMetadata()
    {
        var plugin = new MachineTelemetryPlugin();
        plugin.Start(new PluginStartContext(plugin.PluginId, CancellationToken.None));

        var response = plugin.Handle(SyncRequest.ForStandardPath(new OperationName("Telemetry.Machine.CollectSnapshot"), correlationId: new CorrelationId("corr-machine")));

        Assert.True(response.Success);
        Assert.Equal(SyncResponseStatus.Success, response.Status);
        Assert.Equal(new CorrelationId("corr-machine"), response.CorrelationId);
        var operationPayload = Assert.IsType<TelemetryOperationPayload>(response.Payload);
        Assert.Null(operationPayload.Error);
        Assert.NotNull(operationPayload.Result);
        var payload = operationPayload.Result!;
        Assert.Equal("Plugin.Machine.Telemetry", payload.PluginId);
        Assert.Equal("Telemetry.Machine.CollectSnapshot", payload.Operation);
        Assert.Equal("machine", payload.Source);
        Assert.Equal("system", payload.Category);
        Assert.Contains(payload.Measurements, static m => m is { Name: "cpu.percent", Unit: "percent", Kind: "gauge" } && m.Value >= 0d);
        Assert.Contains(payload.Measurements, static m => m is { Name: "memory.totalPhysical.bytes", Unit: "bytes", Kind: "gauge" } && m.Value >= 0d);
        Assert.Contains(payload.Measurements, static m => m is { Name: "memory.used.bytes", Unit: "bytes", Kind: "gauge" } && m.Value >= 0d);
        Assert.Contains(payload.Measurements, static m => m is { Name: "memory.load.percent", Unit: "percent", Kind: "gauge" } && m.Value >= 0d);
        Assert.True(int.TryParse(payload.Metadata["processorCount"], out var processorCount));
        Assert.True(processorCount > 0);
        Assert.False(string.IsNullOrWhiteSpace(payload.Metadata["osDescription"]));
        Assert.False(string.IsNullOrWhiteSpace(payload.Metadata["frameworkDescription"]));
    }

    [Fact]
    public void TelemetryPluginHostStartup_GivenPluginsPathContainsTelemetryAssembly_ExpectedLifecycleSchedulingAndDiOnlyOperationDiagnostics()
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
                    && x.Contains("outcome=ignored", StringComparison.Ordinal)
                    && x.Contains("reason=unresolvable-via-di", StringComparison.Ordinal)
                    && x.Contains($"lifecycleType={typeof(HostTelemetryPlugin).FullName}", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", "Verify unresolved scheduled plugin types produce deterministic `unresolvable-via-di` diagnostics without crashing host runtime [depends on DI resolver path]")]
    [Trait("AuditArtifact", "iterative-implementation-unresolvable-scheduled-di-diagnostics-2026-05-22")]
    public async Task TelemetryPluginHostStartup_GivenServiceProviderCannotResolveScheduledPluginType_ExpectedHostHealthyAndDeterministicUnresolvableDiagnostic()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-telemetry-runtime-unresolvable-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            var telemetryAssemblySource = typeof(HostTelemetryPlugin).Assembly.Location;
            var telemetryAssemblyCopy = Path.Combine(pluginsPath, Path.GetFileName(telemetryAssemblySource));
            File.Copy(telemetryAssemblySource, telemetryAssemblyCopy);

            var options = new PluginHostingOptions
            {
                PluginsPath = pluginsPath,
                RunOnce = true,
            };

            var services = new ServiceCollection();
            using var provider = services.BuildServiceProvider(validateScopes: true);
            var runner = new HostRunner(options, provider);

            var result = await runner.StartAsync(CancellationToken.None);

            Assert.True(result.WatcherRegistered);
            Assert.True(result.HostHealthy);
            Assert.Contains(
                result.Diagnostics,
                static x => x.Contains("stage=startup outcome=success watcher=registered", StringComparison.Ordinal));
            Assert.Contains(
                result.Diagnostics,
                static x => x.Contains("stage=activation plugin=Plugin.Host.Telemetry outcome=success", StringComparison.Ordinal));
            Assert.Contains(
                result.Diagnostics,
                static x => x.Contains("stage=operation plugin=Plugin.Host.Telemetry", StringComparison.Ordinal)
                    && x.Contains("source=scheduled", StringComparison.Ordinal)
                    && x.Contains("outcome=ignored", StringComparison.Ordinal)
                    && x.Contains("reason=unresolvable-via-di", StringComparison.Ordinal)
                    && x.Contains($"lifecycleType={typeof(HostTelemetryPlugin).FullName}", StringComparison.Ordinal));
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
