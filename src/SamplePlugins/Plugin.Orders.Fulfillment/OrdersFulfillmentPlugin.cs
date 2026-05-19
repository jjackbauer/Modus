using Modus.Core.Plugins;

namespace Modus.SamplePlugins.Orders;

public sealed class OrdersFulfillmentPlugin : IPluginContract, IPluginLifecycle, IPluginOperationCatalog, IPluginScheduledEvents
{
    public string PluginId => "Plugin.Orders.Fulfillment";

    public string ContractName => "Modus.PluginContract";

    public Version ContractVersion => new(1, 0, 0);

    public IReadOnlyCollection<string> SupportedOperations =>
        ["Orders.AllocateInventory", "Orders.CreateShipment"];

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
        scheduler.ScheduleAt(
            jobName: "Orders.CreateShipment.Nightly",
            runAt: DateTimeOffset.UtcNow.AddHours(8),
            operation: "Orders.CreateShipment");
    }
}
