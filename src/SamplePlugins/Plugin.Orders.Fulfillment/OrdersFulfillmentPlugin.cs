using Modus.Core.Messaging;
using Modus.Core.Plugins;

namespace Modus.SamplePlugins.Orders;

public sealed class OrdersFulfillmentPlugin :
    ScopedPlugin<OrdersFulfillmentPlugin>,
    ISyncResponder<SyncRequest, SyncResponse<OrdersFulfillmentOperationPayload>>
{
    private int _lifecycleStage;
    private const string AllocateInventoryOperation = "Orders.AllocateInventory";
    private const string CreateShipmentOperation = "Orders.CreateShipment";

    public override PluginId PluginId => new PluginId("Plugin.Orders.Fulfillment");

    public override ContractName ContractName => new ContractName("Modus.PluginContract");

    public override Version ContractVersion => new(1, 0, 0);

    public override IReadOnlyCollection<OperationName> SupportedOperations =>
        [new OperationName(AllocateInventoryOperation), new OperationName(CreateShipmentOperation)];

    public SyncResponse<OrdersFulfillmentOperationPayload> Handle(SyncRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.Equals(request.Operation.Value, AllocateInventoryOperation, StringComparison.Ordinal))
        {
            return new SyncResponse<OrdersFulfillmentOperationPayload>(
                Success: true,
                Payload: OrdersFulfillmentOperationPayload.Success(
                    pluginId: PluginId.Value,
                    operation: request.Operation.Value,
                    outcome: "inventory-reserved",
                    message: "Inventory allocation completed for shipment preparation."),
                CorrelationId: request.CorrelationId);
        }

        if (string.Equals(request.Operation.Value, CreateShipmentOperation, StringComparison.Ordinal))
        {
            return new SyncResponse<OrdersFulfillmentOperationPayload>(
                Success: true,
                Payload: OrdersFulfillmentOperationPayload.Success(
                    pluginId: PluginId.Value,
                    operation: request.Operation.Value,
                    outcome: "shipment-created",
                    message: "Shipment creation completed for allocated order."),
                CorrelationId: request.CorrelationId);
        }

        return new SyncResponse<OrdersFulfillmentOperationPayload>(
            Success: false,
            Payload: OrdersFulfillmentOperationPayload.Unsupported(
                pluginId: PluginId.Value,
                requestedOperation: request.Operation.Value,
                supportedOperations: [AllocateInventoryOperation, CreateShipmentOperation]),
            Status: SyncResponseStatus.Rejected,
            CorrelationId: request.CorrelationId);
    }

    public override void Load(PluginLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        EnsureLifecycleStage(expectedStage: 0, operation: nameof(Load));
        _lifecycleStage = 1;
    }

    public override void Start(PluginStartContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        EnsureLifecycleStage(expectedStage: 1, operation: nameof(Start));
        _lifecycleStage = 2;
    }

    public override void Stop(PluginStopContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        EnsureLifecycleStage(expectedStage: 2, operation: nameof(Stop));
        _lifecycleStage = 3;
    }

    public override void Unload(PluginUnloadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        EnsureLifecycleStage(expectedStage: 3, operation: nameof(Unload));
        _lifecycleStage = 4;
    }

    public override void RegisterSchedules(IPluginScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        scheduler.ScheduleAt(
            jobName: new JobName("Orders.CreateShipment.Nightly"),
            runAt: DateTimeOffset.UtcNow.AddHours(8),
            operation: new OperationName("Orders.CreateShipment"));
    }

    private void EnsureLifecycleStage(int expectedStage, string operation)
    {
        if (_lifecycleStage != expectedStage)
        {
            throw new InvalidOperationException(
                $"Lifecycle operation '{operation}' expected stage {expectedStage} but found {_lifecycleStage}.");
        }
    }
}

public sealed record OrdersFulfillmentOperationPayload(
    string Code,
    string PluginId,
    string Operation,
    string Message,
    string? Outcome,
    IReadOnlyList<string> SupportedOperations)
{
    public static OrdersFulfillmentOperationPayload Success(
        string pluginId,
        string operation,
        string outcome,
        string message)
    {
        return new OrdersFulfillmentOperationPayload(
            Code: "ok",
            PluginId: pluginId,
            Operation: operation,
            Message: message,
            Outcome: outcome,
            SupportedOperations: []);
    }

    public static OrdersFulfillmentOperationPayload Unsupported(
        string pluginId,
        string requestedOperation,
        IReadOnlyList<string> supportedOperations)
    {
        return new OrdersFulfillmentOperationPayload(
            Code: "unsupported-operation",
            PluginId: pluginId,
            Operation: requestedOperation,
            Message: $"Operation '{requestedOperation}' is not supported by plugin '{pluginId}'.",
            Outcome: null,
            SupportedOperations: supportedOperations);
    }
}
