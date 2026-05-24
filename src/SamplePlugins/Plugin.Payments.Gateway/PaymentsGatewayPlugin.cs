using Modus.Core.Messaging;
using Modus.Core.Plugins;

namespace Modus.SamplePlugins.Payments;

public sealed class PaymentsGatewayPlugin :
    SingletonPlugin<PaymentsGatewayPlugin>,
    ISyncResponder<SyncRequest, SyncResponse<PaymentsGatewayOperationPayload>>
{
    private int _lifecycleStage;
    private const string SyncLedgerOperation = "Payments.SyncLedger";
    private const string EmitSettlementOperation = "Payments.EmitSettlement";

    public override PluginId PluginId => new PluginId("Plugin.Payments.Gateway");

    public override ContractName ContractName => new ContractName("Modus.PluginContract");

    public override Version ContractVersion => new(1, 0, 0);

    public override IReadOnlyCollection<OperationName> SupportedOperations =>
        [new OperationName(EmitSettlementOperation), new OperationName(SyncLedgerOperation)];

    public SyncResponse<PaymentsGatewayOperationPayload> Handle(SyncRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.Equals(request.Operation.Value, SyncLedgerOperation, StringComparison.Ordinal))
        {
            return new SyncResponse<PaymentsGatewayOperationPayload>(
                Success: true,
                Payload: PaymentsGatewayOperationPayload.Success(
                    pluginId: PluginId.Value,
                    operation: request.Operation.Value,
                    outcome: "ledger-synchronized",
                    message: "Ledger synchronization completed."),
                CorrelationId: request.CorrelationId);
        }

        if (string.Equals(request.Operation.Value, EmitSettlementOperation, StringComparison.Ordinal))
        {
            return new SyncResponse<PaymentsGatewayOperationPayload>(
                Success: true,
                Payload: PaymentsGatewayOperationPayload.Success(
                    pluginId: PluginId.Value,
                    operation: request.Operation.Value,
                    outcome: "settlement-emitted",
                    message: "Settlement event emitted to downstream systems."),
                CorrelationId: request.CorrelationId);
        }

        return new SyncResponse<PaymentsGatewayOperationPayload>(
            Success: false,
            Payload: PaymentsGatewayOperationPayload.Unsupported(
                pluginId: PluginId.Value,
                requestedOperation: request.Operation.Value,
                supportedOperations: [SyncLedgerOperation, EmitSettlementOperation]),
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
        scheduler.ScheduleRecurring(
            jobName: new JobName("Payments.SyncLedger.EveryHour"),
            interval: TimeSpan.FromHours(1),
            operation: new OperationName("Payments.SyncLedger"));
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

public sealed record PaymentsGatewayOperationPayload(
    string Code,
    string PluginId,
    string Operation,
    string Message,
    string? Outcome,
    IReadOnlyList<string> SupportedOperations)
{
    public static PaymentsGatewayOperationPayload Success(
        string pluginId,
        string operation,
        string outcome,
        string message)
    {
        return new PaymentsGatewayOperationPayload(
            Code: "ok",
            PluginId: pluginId,
            Operation: operation,
            Message: message,
            Outcome: outcome,
            SupportedOperations: []);
    }

    public static PaymentsGatewayOperationPayload Unsupported(
        string pluginId,
        string requestedOperation,
        IReadOnlyList<string> supportedOperations)
    {
        return new PaymentsGatewayOperationPayload(
            Code: "unsupported-operation",
            PluginId: pluginId,
            Operation: requestedOperation,
            Message: $"Operation '{requestedOperation}' is not supported by plugin '{pluginId}'.",
            Outcome: null,
            SupportedOperations: supportedOperations);
    }
}
