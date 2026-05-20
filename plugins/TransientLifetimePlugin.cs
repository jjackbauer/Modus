using Modus.Core.Events;
using Modus.Core.Messaging;
using Modus.Core.Plugins;

namespace Modus.SamplePlugins.Lifetime;

/// <summary>
/// Demonstrates Transient DI lifetime: a new instance is created on every resolution
/// from the DI container. No instance is shared between consumers or across calls.
/// </summary>
public sealed class TransientLifetimePlugin :
    TransientPlugin<TransientLifetimePlugin>,
    IEventSubscriber,
    ISyncResponder
{
    private static int _totalCreated;

    private readonly int _creationIndex;
    private int _invocationCount;

    public Guid InstanceId { get; } = Guid.NewGuid();

    public TransientLifetimePlugin()
    {
        _creationIndex = Interlocked.Increment(ref _totalCreated);
    }

    public override PluginId PluginId => new PluginId("Plugin.Lifetime.Transient");

    public override ContractName ContractName => new ContractName("Modus.PluginContract");

    public override Version ContractVersion => new(1, 0, 0);

    public override IReadOnlyCollection<OperationName> SupportedOperations =>
        [new OperationName(OperationNameValue)];

    private const string OperationNameValue = "Lifetime.Transient.PrintId";
    private const string RecurringJobName = "Lifetime.Transient.PrintId.Every4Seconds";
    private static readonly TimeSpan RecurringInterval = TimeSpan.FromSeconds(4);

    public override void Load(PluginLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    public override void Start(PluginStartContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Console.WriteLine($"lifetime-demo plugin={PluginId} lifetime=Transient instance-id={InstanceId:N} creation-index={_creationIndex} event=started");
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

        var count = Interlocked.Increment(ref _invocationCount);
        Console.WriteLine($"lifetime-demo plugin={PluginId} lifetime=Transient instance-id={InstanceId:N} creation-index={_creationIndex} invocation={count}");

        return new SyncResponse(
            Success: true,
            Payload: $"lifetime=Transient instance-id={InstanceId:N} invocation={count}",
            CorrelationId: request.CorrelationId);
    }
}
