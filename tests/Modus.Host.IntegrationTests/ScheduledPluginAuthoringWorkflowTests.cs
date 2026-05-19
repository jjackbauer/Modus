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
        Assert.Equal("Payments.SyncLedger.EveryHour", recurring.JobName);
        Assert.Equal(TimeSpan.FromHours(1), recurring.Interval);
        Assert.Equal("Payments.SyncLedger", recurring.Operation);
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
        Assert.Contains(nameof(IPluginScheduledEvents), validation.MissingCapabilities, StringComparer.Ordinal);
    }

    private sealed class RecordingPluginScheduler : IPluginScheduler
    {
        public List<RecurringSchedule> RecurringSchedules { get; } = [];

        public List<OneTimeSchedule> OneTimeSchedules { get; } = [];

        public void ScheduleRecurring(string jobName, TimeSpan interval, string operation)
        {
            RecurringSchedules.Add(new RecurringSchedule(jobName, interval, operation));
        }

        public void ScheduleAt(string jobName, DateTimeOffset runAt, string operation)
        {
            OneTimeSchedules.Add(new OneTimeSchedule(jobName, runAt, operation));
        }
    }

    private sealed record RecurringSchedule(string JobName, TimeSpan Interval, string Operation);

    private sealed record OneTimeSchedule(string JobName, DateTimeOffset RunAt, string Operation);

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
            PluginId = pluginId;
            _operation = operation;
            _recurringInterval = recurringInterval;
        }

        public string PluginId { get; }

        public string ContractName => "Modus.PluginContract";

        public Version ContractVersion => new(1, 0);

        public IReadOnlyCollection<string> SupportedOperations => ["Payments.EmitSettlement", _operation];

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
                jobName: "Payments.SyncLedger.EveryHour",
                interval: _recurringInterval,
                operation: _operation);
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
            PluginId = pluginId;
            SupportedOperations = [operation];
        }

        public string PluginId { get; }

        public string ContractName => "Modus.PluginContract";

        public Version ContractVersion => new(1, 0);

        public IReadOnlyCollection<string> SupportedOperations { get; }

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