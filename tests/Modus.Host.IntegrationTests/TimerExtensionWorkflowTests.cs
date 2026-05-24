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
            schedule: new RecurringSchedule(new JobName("Timer.Print.Utc.EveryFiveSeconds"), TimeSpan.FromSeconds(5), new OperationName("Timer.Print.Utc")));
        var newExtension = new RecordingTimerTaskExtension(
            extensionName: "new",
            operation: "Timer.Cleanup.Expired",
            payload: "cleanup",
            schedule: new RecurringSchedule(new JobName("Timer.Cleanup.Expired.EveryMinute"), TimeSpan.FromMinutes(1), new OperationName("Timer.Cleanup.Expired")));
        var plugin = new TimerPlugin(existingExtension, newExtension);
        var scheduler = new RecordingPluginScheduler();

        plugin.RegisterSchedules(scheduler);
        var response = plugin.Handle(SyncRequest.ForStandardPath(new OperationName("Timer.Cleanup.Expired")));

        Assert.Equal(
            new[] { new OperationName("Timer.Cleanup.Expired"), new OperationName("Timer.Print.Utc") },
            plugin.SupportedOperations);
        Assert.Equal("cleanup", response.Payload.TimestampUtcIso8601);
        Assert.Equal(0, existingExtension.HandleCalls);
        Assert.Equal(1, newExtension.HandleCalls);
        Assert.Equal(2, scheduler.RecurringSchedules.Count);
        Assert.Contains(
            scheduler.RecurringSchedules,
            static schedule => schedule.JobName == new JobName("Timer.Print.Utc.EveryFiveSeconds")
                && schedule.Interval == TimeSpan.FromSeconds(5)
                && schedule.Operation == new OperationName("Timer.Print.Utc"));
        Assert.Contains(
            scheduler.RecurringSchedules,
            static schedule => schedule.JobName == new JobName("Timer.Cleanup.Expired.EveryMinute")
                && schedule.Interval == TimeSpan.FromMinutes(1)
                && schedule.Operation == new OperationName("Timer.Cleanup.Expired"));
    }

    [Fact]
    public void TimerExtensionWorkflow_GivenKnownAndUnknownOperations_ExpectedDispatchRoutesOwnerAndRejectsUnsupportedOperations()
    {
        var extensionA = new RecordingTimerTaskExtension(
            extensionName: "A",
            operation: "Timer.Print.Utc",
            payload: "owner-A",
            schedule: new RecurringSchedule(new JobName("Timer.Print.Utc.EveryFiveSeconds"), TimeSpan.FromSeconds(5), new OperationName("Timer.Print.Utc")));
        var extensionB = new RecordingTimerTaskExtension(
            extensionName: "B",
            operation: "Timer.Cleanup.Expired",
            payload: "owner-B",
            schedule: new RecurringSchedule(new JobName("Timer.Cleanup.Expired.EveryMinute"), TimeSpan.FromMinutes(1), new OperationName("Timer.Cleanup.Expired")));
        var plugin = new TimerPlugin(extensionA, extensionB);

        var known = plugin.Handle(SyncRequest.ForStandardPath(new OperationName("Timer.Cleanup.Expired"), correlationId: new CorrelationId("corr-known")));
        var unknown = plugin.Handle(SyncRequest.ForStandardPath(new OperationName("Timer.Unknown.Operation"), correlationId: new CorrelationId("corr-unknown")));

        Assert.True(known.Success);
        Assert.Equal(SyncResponseStatus.Success, known.Status);
        Assert.Equal("owner-B", known.Payload.TimestampUtcIso8601);
        Assert.Equal(new CorrelationId("corr-known"), known.CorrelationId);
        Assert.Equal(0, extensionA.HandleCalls);
        Assert.Equal(1, extensionB.HandleCalls);

        Assert.False(unknown.Success);
        Assert.Equal(SyncResponseStatus.Rejected, unknown.Status);
        Assert.NotNull(unknown.Payload.Error);
        Assert.Equal("unsupported-operation", unknown.Payload.Error!.Code);
        Assert.Equal(new CorrelationId("corr-unknown"), unknown.CorrelationId);
        Assert.Equal(0, extensionA.HandleCalls);
        Assert.Equal(1, extensionB.HandleCalls);
    }

    private sealed class RecordingPluginScheduler : IPluginScheduler
    {
        public List<RecurringSchedule> RecurringSchedules { get; } = [];

        public void ScheduleRecurring(JobName jobName, TimeSpan interval, OperationName operation)
        {
            RecurringSchedules.Add(new RecurringSchedule(jobName, interval, operation));
        }

        public void ScheduleAt(JobName jobName, DateTimeOffset runAt, OperationName operation)
        {
        }
    }

    private sealed record RecurringSchedule(JobName JobName, TimeSpan Interval, OperationName Operation);

    private sealed class RecordingTimerTaskExtension : IScheduledTimerTaskExtension
    {
        private readonly string _payload;
        private readonly RecurringSchedule _schedule;

        public RecordingTimerTaskExtension(string extensionName, string operation, string payload, RecurringSchedule schedule)
        {
            ExtensionName = extensionName;
            _payload = payload;
            _schedule = schedule;
            SupportedOperations = [new OperationName(operation)];
        }

        public string ExtensionName { get; }

        public int HandleCalls { get; private set; }

        public IReadOnlyCollection<OperationName> SupportedOperations { get; }

        public void RegisterSchedules(IPluginScheduler scheduler)
        {
            scheduler.ScheduleRecurring(_schedule.JobName, _schedule.Interval, _schedule.Operation);
        }

        public SyncResponse<ISyncPayload> Handle(SyncRequest request)
        {
            HandleCalls++;
            return new SyncResponse<ISyncPayload>(
                Success: true,
                Payload: new TimerWriteCurrentTimeResult(_payload),
                CorrelationId: request.CorrelationId);
        }
    }
}
