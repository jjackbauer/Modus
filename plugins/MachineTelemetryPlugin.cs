using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Modus.Core.Events;
using Modus.Core.Hosting;
using Modus.Core.Messaging;
using Modus.Core.Plugins;

namespace Modus.SamplePlugins.Telemetry;

public sealed class MachineTelemetryPlugin :
    SingletonPlugin<MachineTelemetryPlugin>,
    IMachineTelemetryPluginContract,
    IEventSubscriber,
    ISyncResponder<SyncRequest, SyncResponse<TelemetryOperationPayload>>
{
    private readonly object _sync = new();
    private double _lastAllProcessesCpuMs;
    private DateTimeOffset _lastCpuSampleUtc;

    public MachineTelemetryPlugin()
    {
    }

    public override PluginId PluginId => new PluginId("Plugin.Machine.Telemetry");

    public override ContractName ContractName => new ContractName("Modus.PluginContract");

    public override Version ContractVersion => new(1, 0, 0);

    public override IReadOnlyCollection<OperationName> SupportedOperations => [new OperationName(OperationNameValue)];

    private const string OperationNameValue = "Telemetry.Machine.CollectSnapshot";
    private const string RecurringJobName = "Telemetry.Machine.CollectSnapshot.Every5Seconds";
    private static readonly TimeSpan RecurringInterval = TimeSpan.FromSeconds(5);

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
            _lastAllProcessesCpuMs = SampleAllProcessesCpuMs();
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

    public SyncResponse<TelemetryOperationPayload> Handle(SyncRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(request.Operation.Value, OperationNameValue, StringComparison.Ordinal))
        {
            return new SyncResponse<TelemetryOperationPayload>(
                Success: false,
                Payload: TelemetryOperationPayload.FromError(
                    code: "unsupported-operation",
                    message: $"Operation '{request.Operation.Value}' is not supported by '{OperationNameValue}'."),
                Status: SyncResponseStatus.Rejected,
                CorrelationId: request.CorrelationId);
        }

        var snapshot = BuildTelemetryResult();
        Console.WriteLine($"plugin-telemetry plugin={PluginId} operation={OperationNameValue} payload={JsonSerializer.Serialize(snapshot)}");
        return new SyncResponse<TelemetryOperationPayload>(
            Success: true,
            Payload: TelemetryOperationPayload.FromResult(snapshot),
            CorrelationId: request.CorrelationId);
    }

    private TelemetryResult BuildTelemetryResult()
    {
        var memInfo = GC.GetGCMemoryInfo();
        var totalPhysicalBytes = memInfo.TotalAvailableMemoryBytes;
        var usedMemoryBytes = memInfo.MemoryLoadBytes;
        var memoryLoadPercent = totalPhysicalBytes > 0
            ? (double)usedMemoryBytes / totalPhysicalBytes * 100d
            : 0d;

        var now = DateTimeOffset.UtcNow;
        var currentCpuMs = SampleAllProcessesCpuMs();

        double cpuPercent;
        lock (_sync)
        {
            var elapsedWallMs = (now - _lastCpuSampleUtc).TotalMilliseconds;
            var elapsedCpuMs = currentCpuMs - _lastAllProcessesCpuMs;

            cpuPercent = elapsedWallMs <= 0d
                ? 0d
                : elapsedCpuMs / (elapsedWallMs * Environment.ProcessorCount) * 100d;

            if (cpuPercent < 0d) cpuPercent = 0d;
            if (cpuPercent > 100d) cpuPercent = 100d;

            _lastCpuSampleUtc = now;
            _lastAllProcessesCpuMs = currentCpuMs;
        }

        return new TelemetryResult(
            PluginId: PluginId.Value,
            Operation: OperationNameValue,
            Source: "machine",
            Category: "system",
            CollectedAtUtc: now,
            Measurements:
            [
                new TelemetryMeasurement("cpu.percent", cpuPercent, "percent", "gauge"),
                new TelemetryMeasurement("memory.totalPhysical.bytes", totalPhysicalBytes, "bytes", "gauge"),
                new TelemetryMeasurement("memory.used.bytes", usedMemoryBytes, "bytes", "gauge"),
                new TelemetryMeasurement("memory.load.percent", memoryLoadPercent, "percent", "gauge")
            ],
            Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["processorCount"] = Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture),
                ["osDescription"] = RuntimeInformation.OSDescription,
                ["frameworkDescription"] = RuntimeInformation.FrameworkDescription,
                ["osArchitecture"] = RuntimeInformation.OSArchitecture.ToString()
            });
    }

    private static double SampleAllProcessesCpuMs()
    {
        var total = 0d;
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                total += process.TotalProcessorTime.TotalMilliseconds;
            }
            catch
            {
                // Process may have exited or access may be denied — skip it.
            }
            finally
            {
                process.Dispose();
            }
        }

        return total;
    }
}
