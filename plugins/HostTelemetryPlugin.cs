using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
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

    public override string PluginId => "Plugin.Host.Telemetry";

    public override string ContractName => "Modus.PluginContract";

    public override Version ContractVersion => new(1, 0, 0);

    public override IReadOnlyCollection<string> SupportedOperations => [OperationName];

    private const string OperationName = "Telemetry.Host.CollectSnapshot";
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
        scheduler.ScheduleRecurring(RecurringJobName, RecurringInterval, OperationName);
    }

    protected override void RegisterPluginServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        base.RegisterPluginServices(services);
        // Also register this plugin as the contract implementation
        services.AddPluginServiceInstance<IHostTelemetryPluginContract>(this);
    }

    public void Subscribe(IEventPublisher publisher)
    {
        ArgumentNullException.ThrowIfNull(publisher);
    }

    public SyncResponse Handle(SyncRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(request.Operation, OperationName, StringComparison.Ordinal))
        {
            return new SyncResponse(
                Success: false,
                Payload: "unsupported-operation",
                Status: SyncResponseStatus.Rejected,
                CorrelationId: request.CorrelationId);
        }

        var snapshot = BuildTelemetrySnapshot();
        Console.WriteLine($"plugin-telemetry plugin={PluginId} operation={OperationName} {snapshot}");
        return new SyncResponse(
            Success: true,
            Payload: snapshot,
            CorrelationId: request.CorrelationId);
    }

    private string BuildTelemetrySnapshot()
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

        return string.Create(
            CultureInfo.InvariantCulture,
            $"cpuPercent={cpuPercent:F2};workingSetBytes={workingSetBytes};privateMemoryBytes={privateMemoryBytes};managedHeapBytes={managedHeapBytes};gcGen0={gcGen0};gcGen1={gcGen1};gcGen2={gcGen2}");
    }
}
