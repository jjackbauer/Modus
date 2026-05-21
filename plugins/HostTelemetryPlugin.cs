using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Modus.Core.Events;
using Modus.Core.Hosting;
using Modus.Core.Messaging;
using Modus.Core.Plugins;

namespace Modus.SamplePlugins.Telemetry;

public sealed class HostTelemetryPlugin :
    SingletonPlugin<HostTelemetryPlugin>,
    IHostTelemetryPluginContract,
    IEventSubscriber,
    ISyncResponder
{
    private readonly object _sync = new();
    private TimeSpan _lastCpuTime;
    private DateTimeOffset _lastCpuSampleUtc;

    public HostTelemetryPlugin()
    {
    }

    public override PluginId PluginId => new PluginId("Plugin.Host.Telemetry");

    public override ContractName ContractName => new ContractName("Modus.PluginContract");

    public override Version ContractVersion => new(1, 0, 0);

    public override IReadOnlyCollection<OperationName> SupportedOperations => [new OperationName(OperationNameValue)];

    private const string OperationNameValue = "Telemetry.Host.CollectSnapshot";
    private const string RecurringJobName = "Telemetry.Host.CollectSnapshot.EverySecond";
    private static readonly TimeSpan RecurringInterval = TimeSpan.FromSeconds(1);

    public override void Load(PluginLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    public override void Start(PluginStartContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (_sync)
        {
            _lastCpuSampleUtc = DateTimeOffset.UtcNow;
            _lastCpuTime = Process.GetCurrentProcess().TotalProcessorTime;
        }
    }

    public override void Stop(PluginStopContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    public override void Unload(PluginUnloadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    public override void RegisterSchedules(IPluginScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        scheduler.ScheduleRecurring(new JobName(RecurringJobName), RecurringInterval, new OperationName(OperationNameValue));
    }

    public void Subscribe(IEventPublisher publisher)
    {
        ArgumentNullException.ThrowIfNull(publisher);
    }

    public SyncResponse Handle(SyncRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(request.Operation.Value, OperationNameValue, StringComparison.Ordinal))
        {
            return new SyncResponse(
                Success: false,
                Payload: "unsupported-operation",
                Status: SyncResponseStatus.Rejected,
                CorrelationId: request.CorrelationId);
        }

        var snapshot = BuildTelemetryResult();
        Console.WriteLine($"plugin-telemetry plugin={PluginId} operation={OperationNameValue} payload={JsonSerializer.Serialize(snapshot)}");
        return new SyncResponse(
            Success: true,
            PayloadObject: snapshot,
            CorrelationId: request.CorrelationId);
    }

    private TelemetryResult BuildTelemetryResult()
    {
        var process = Process.GetCurrentProcess();
        var now = DateTimeOffset.UtcNow;
        var totalCpu = process.TotalProcessorTime;

        double cpuPercent;

        lock (_sync)
        {
            var elapsedWallMs = (now - _lastCpuSampleUtc).TotalMilliseconds;
            var elapsedCpuMs = (totalCpu - _lastCpuTime).TotalMilliseconds;

            cpuPercent = elapsedWallMs <= 0d
                ? 0d
                : (elapsedCpuMs / (elapsedWallMs * Environment.ProcessorCount)) * 100d;

            if (cpuPercent < 0d)
            {
                cpuPercent = 0d;
            }

            _lastCpuSampleUtc = now;
            _lastCpuTime = totalCpu;
        }

        var workingSetBytes = process.WorkingSet64;
        var privateMemoryBytes = process.PrivateMemorySize64;
        var managedHeapBytes = GC.GetTotalMemory(forceFullCollection: false);
        var gcGen0 = GC.CollectionCount(0);
        var gcGen1 = GC.CollectionCount(1);
        var gcGen2 = GC.CollectionCount(2);

        return new TelemetryResult(
            PluginId: PluginId.Value,
            Operation: OperationNameValue,
            Source: "host",
            Category: "runtime",
            CollectedAtUtc: now,
            Measurements:
            [
                new TelemetryMeasurement("cpu.percent", cpuPercent, "percent", "gauge"),
                new TelemetryMeasurement("memory.workingSet.bytes", workingSetBytes, "bytes", "gauge"),
                new TelemetryMeasurement("memory.private.bytes", privateMemoryBytes, "bytes", "gauge"),
                new TelemetryMeasurement("memory.managedHeap.bytes", managedHeapBytes, "bytes", "gauge"),
                new TelemetryMeasurement("gc.collections.gen0", gcGen0, "count", "counter"),
                new TelemetryMeasurement("gc.collections.gen1", gcGen1, "count", "counter"),
                new TelemetryMeasurement("gc.collections.gen2", gcGen2, "count", "counter")
            ],
            Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["machineName"] = Environment.MachineName,
                ["processId"] = process.Id.ToString(CultureInfo.InvariantCulture),
                ["processName"] = process.ProcessName,
                ["processorCount"] = Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture)
            });
    }
}
