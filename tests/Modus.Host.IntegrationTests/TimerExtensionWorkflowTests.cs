using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class TimerExtensionWorkflowTests
{
    [Fact]
    public void TimerExtensionWorkflow_GivenNewScheduledTaskExtension_ExpectedTimerPluginSupportsOperationWithoutCoreOrchestrationChanges()
    {
        var existingExtension = new RecordingTimerTaskExtension(
            extensionName: "existing",
            operation: "Timer.Print.Utc",
            payload: "existing",
            schedule: new RecurringSchedule("Timer.Print.Utc.EveryFiveSeconds", TimeSpan.FromSeconds(5), "Timer.Print.Utc"));
        var newExtension = new RecordingTimerTaskExtension(
            extensionName: "new",
            operation: "Timer.Cleanup.Expired",
            payload: "cleanup",
            schedule: new RecurringSchedule("Timer.Cleanup.Expired.EveryMinute", TimeSpan.FromMinutes(1), "Timer.Cleanup.Expired"));
        var plugin = new TimerPlugin(existingExtension, newExtension);
        var scheduler = new RecordingPluginScheduler();

        plugin.RegisterSchedules(scheduler);
        var response = plugin.Handle(SyncRequest.ForStandardPath("Timer.Cleanup.Expired"));

        Assert.Equal(new[] { "Timer.Cleanup.Expired", "Timer.Print.Utc" }, plugin.SupportedOperations);
        Assert.Equal("cleanup", response.Payload);
        Assert.Equal(0, existingExtension.HandleCalls);
        Assert.Equal(1, newExtension.HandleCalls);
        Assert.Equal(2, scheduler.RecurringSchedules.Count);
        Assert.Contains(
            scheduler.RecurringSchedules,
            static schedule => schedule.JobName == "Timer.Print.Utc.EveryFiveSeconds"
                && schedule.Interval == TimeSpan.FromSeconds(5)
                && schedule.Operation == "Timer.Print.Utc");
        Assert.Contains(
            scheduler.RecurringSchedules,
            static schedule => schedule.JobName == "Timer.Cleanup.Expired.EveryMinute"
                && schedule.Interval == TimeSpan.FromMinutes(1)
                && schedule.Operation == "Timer.Cleanup.Expired");
    }

    [Fact]
    public void TimerExtensionWorkflow_GivenKnownAndUnknownOperations_ExpectedDispatchRoutesOwnerAndRejectsUnsupportedOperations()
    {
        var extensionA = new RecordingTimerTaskExtension(
            extensionName: "A",
            operation: "Timer.Print.Utc",
            payload: "owner-A",
            schedule: new RecurringSchedule("Timer.Print.Utc.EveryFiveSeconds", TimeSpan.FromSeconds(5), "Timer.Print.Utc"));
        var extensionB = new RecordingTimerTaskExtension(
            extensionName: "B",
            operation: "Timer.Cleanup.Expired",
            payload: "owner-B",
            schedule: new RecurringSchedule("Timer.Cleanup.Expired.EveryMinute", TimeSpan.FromMinutes(1), "Timer.Cleanup.Expired"));
        var plugin = new TimerPlugin(extensionA, extensionB);

        var known = plugin.Handle(SyncRequest.ForStandardPath("Timer.Cleanup.Expired", correlationId: "corr-known"));
        var unknown = plugin.Handle(SyncRequest.ForStandardPath("Timer.Unknown.Operation", correlationId: "corr-unknown"));

        Assert.True(known.Success);
        Assert.Equal(SyncResponseStatus.Success, known.Status);
        Assert.Equal("owner-B", known.Payload);
        Assert.Equal("corr-known", known.CorrelationId);
        Assert.Equal(0, extensionA.HandleCalls);
        Assert.Equal(1, extensionB.HandleCalls);

        Assert.False(unknown.Success);
        Assert.Equal(SyncResponseStatus.Rejected, unknown.Status);
        Assert.Equal("unsupported-operation", unknown.Payload);
        Assert.Equal("corr-unknown", unknown.CorrelationId);
        Assert.Equal(0, extensionA.HandleCalls);
        Assert.Equal(1, extensionB.HandleCalls);
    }

    private sealed class RecordingPluginScheduler : IPluginScheduler
    {
        public List<RecurringSchedule> RecurringSchedules { get; } = [];

        public void ScheduleRecurring(string jobName, TimeSpan interval, string operation)
        {
            RecurringSchedules.Add(new RecurringSchedule(jobName, interval, operation));
        }

        public void ScheduleAt(string jobName, DateTimeOffset runAt, string operation)
        {
        }
    }

    private sealed record RecurringSchedule(string JobName, TimeSpan Interval, string Operation);

    private sealed class RecordingTimerTaskExtension : IScheduledTimerTaskExtension
    {
        private readonly string _payload;
        private readonly RecurringSchedule _schedule;

        public RecordingTimerTaskExtension(string extensionName, string operation, string payload, RecurringSchedule schedule)
        {
            ExtensionName = extensionName;
            _payload = payload;
            _schedule = schedule;
            SupportedOperations = [operation];
        }

        public string ExtensionName { get; }

        public int HandleCalls { get; private set; }

        public IReadOnlyCollection<string> SupportedOperations { get; }

        public void RegisterSchedules(IPluginScheduler scheduler)
        {
            scheduler.ScheduleRecurring(_schedule.JobName, _schedule.Interval, _schedule.Operation);
        }

        public SyncResponse Handle(SyncRequest request)
        {
            HandleCalls++;
            return new SyncResponse(Success: true, Payload: _payload, CorrelationId: request.CorrelationId);
        }
    }
}
