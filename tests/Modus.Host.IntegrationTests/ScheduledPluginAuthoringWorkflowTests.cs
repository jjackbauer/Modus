using Modus.Core.Events;
using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class ScheduledPluginAuthoringWorkflowTests
{
    [Fact]
    public void ScheduledPluginAuthoringWorkflow_GivenRecurringScheduleRegistration_ExpectedSchedulerReceivesDeterministicJobIntervalAndOperation()
    {
        var plugin = new ScheduledWorkflowPlugin(
            pluginId: "Plugin.Payments.Scheduled",
            operation: "Payments.SyncLedger",
            recurringInterval: TimeSpan.FromHours(1));
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
        Assert.Equal(new JobName("Payments.SyncLedger.EveryHour"), recurring.JobName);
        Assert.Equal(TimeSpan.FromHours(1), recurring.Interval);
        Assert.Equal(new OperationName("Payments.SyncLedger"), recurring.Operation);
        Assert.Contains(recurring.Operation, plugin.SupportedOperations);
    }

    [Fact]
    public void ScheduledPluginAuthoringWorkflow_GivenScheduleRegistrationOmitted_ExpectedValidationOrRuntimeDiagnosticsExposeMissingCapability()
    {
        var plugin = new StandardOnlyWorkflowPlugin(
            pluginId: "Plugin.Payments.StandardOnly",
            operation: "Payments.SyncLedger");

        var validation = PluginContractValidator.Validate(
            plugin,
            new PluginContractValidationPolicy
            {
                RequireScheduledEventsCapability = true,
                RequireDeterministicRegistrationLifecycle = false,
            });

        Assert.False(validation.IsValid);
        Assert.Contains(new CapabilityName(nameof(IPluginScheduledEvents)), validation.MissingCapabilities);
    }

    private sealed class RecordingPluginScheduler : IPluginScheduler
    {
        public List<RecurringSchedule> RecurringSchedules { get; } = [];

        public List<OneTimeSchedule> OneTimeSchedules { get; } = [];

        public void ScheduleRecurring(JobName jobName, TimeSpan interval, OperationName operation)
        {
            RecurringSchedules.Add(new RecurringSchedule(jobName, interval, operation));
        }

        public void ScheduleAt(JobName jobName, DateTimeOffset runAt, OperationName operation)
        {
            OneTimeSchedules.Add(new OneTimeSchedule(jobName, runAt, operation));
        }
    }

    private sealed record RecurringSchedule(JobName JobName, TimeSpan Interval, OperationName Operation);

    private sealed record OneTimeSchedule(JobName JobName, DateTimeOffset RunAt, OperationName Operation);

    private sealed class ScheduledWorkflowPlugin :
        IPluginContract,
        IPluginLifecycle,
        IPluginOperationCatalog,
        IPluginScheduledEvents,
        IEventSubscriber,
        ISyncResponder
    {
        private readonly string _operation;
        private readonly TimeSpan _recurringInterval;

        public ScheduledWorkflowPlugin(string pluginId, string operation, TimeSpan recurringInterval)
        {
            PluginId = new PluginId(pluginId);
            _operation = operation;
            _recurringInterval = recurringInterval;
        }

        public PluginId PluginId { get; }

        public ContractName ContractName => new ContractName("Modus.PluginContract");

        public Version ContractVersion => new(1, 0);

        public IReadOnlyCollection<OperationName> SupportedOperations => [new OperationName("Payments.EmitSettlement"), new OperationName(_operation)];

        public void Load(PluginLoadContext context)
        {
        }

        public void Start(PluginStartContext context)
        {
        }

        public void Stop(PluginStopContext context)
        {
        }

        public void Unload(PluginUnloadContext context)
        {
        }

        public void RegisterSchedules(IPluginScheduler scheduler)
        {
            ArgumentNullException.ThrowIfNull(scheduler);
            scheduler.ScheduleRecurring(
                jobName: new JobName("Payments.SyncLedger.EveryHour"),
                interval: _recurringInterval,
                operation: new OperationName(_operation));
        }

        public void Subscribe(IEventPublisher publisher)
        {
        }

        public SyncResponse Handle(SyncRequest request)
        {
            return new SyncResponse(Success: true, Payload: "ok");
        }
    }

    private sealed class StandardOnlyWorkflowPlugin :
        IPluginContract,
        IPluginLifecycle,
        IPluginOperationCatalog,
        IEventSubscriber,
        ISyncResponder
    {
        public StandardOnlyWorkflowPlugin(string pluginId, string operation)
        {
            PluginId = new PluginId(pluginId);
            SupportedOperations = [new OperationName(operation)];
        }

        public PluginId PluginId { get; }

        public ContractName ContractName => new ContractName("Modus.PluginContract");

        public Version ContractVersion => new(1, 0);

        public IReadOnlyCollection<OperationName> SupportedOperations { get; }

        public void Load(PluginLoadContext context)
        {
        }

        public void Start(PluginStartContext context)
        {
        }

        public void Stop(PluginStopContext context)
        {
        }

        public void Unload(PluginUnloadContext context)
        {
        }

        public void Subscribe(IEventPublisher publisher)
        {
        }

        public SyncResponse Handle(SyncRequest request)
        {
            return new SyncResponse(Success: true, Payload: "ok");
        }
    }
}