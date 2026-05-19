using Modus.Core.Plugins;

namespace Modus.SamplePlugins.Payments;

public sealed class PaymentsGatewayPlugin : IPluginContract, IPluginLifecycle, IPluginOperationCatalog, IPluginScheduledEvents
{
    public string PluginId => "Plugin.Payments.Gateway";

    public string ContractName => "Modus.PluginContract";

    public Version ContractVersion => new(1, 0, 0);

    public IReadOnlyCollection<string> SupportedOperations =>
        ["Payments.EmitSettlement", "Payments.SyncLedger"];

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
            jobName: "Payments.SyncLedger.EveryHour",
            interval: TimeSpan.FromHours(1),
            operation: "Payments.SyncLedger");
    }
}
