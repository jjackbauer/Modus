using Modus.Core.Plugins;

namespace Modus.SamplePlugins.Payments;

public sealed class PaymentsGatewayPlugin : IPluginContract, IPluginLifecycle, IPluginOperationCatalog, IPluginScheduledEvents
{
    public PluginId PluginId => new PluginId("Plugin.Payments.Gateway");

    public ContractName ContractName => new ContractName("Modus.PluginContract");

    public Version ContractVersion => new(1, 0, 0);

    public IReadOnlyCollection<OperationName> SupportedOperations =>
        [new OperationName("Payments.EmitSettlement"), new OperationName("Payments.SyncLedger")];

    public void Load(PluginLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    public void Start(PluginStartContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    public void Stop(PluginStopContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    public void Unload(PluginUnloadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    public void RegisterSchedules(IPluginScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        scheduler.ScheduleRecurring(
            jobName: new JobName("Payments.SyncLedger.EveryHour"),
            interval: TimeSpan.FromHours(1),
            operation: new OperationName("Payments.SyncLedger"));
    }
}
