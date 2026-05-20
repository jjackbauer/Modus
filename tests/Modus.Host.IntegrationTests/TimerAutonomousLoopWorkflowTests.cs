using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class TimerAutonomousLoopWorkflowTests
{
    [Fact]
    public void TimerAutonomousLoopWorkflow_GivenDefaultTimerPlugin_ExpectedRegisterSchedulesUsesFiveSecondRecurringInterval()
    {
        var plugin = new TimerPlugin();
        var scheduler = new RecordingPluginScheduler();

        plugin.RegisterSchedules(scheduler);

        var recurring = Assert.Single(scheduler.RecurringSchedules);
        Assert.Equal(new JobName("Timer.WriteCurrentTime.Every5Seconds"), recurring.JobName);
        Assert.Equal(TimeSpan.FromSeconds(5), recurring.Interval);
        Assert.Equal(new OperationName("Timer.WriteCurrentTime"), recurring.Operation);
    }

    [Fact]
    public void TimerAutonomousLoopWorkflow_GivenLifecycleStartWithMultipleExtensions_ExpectedDefaultExtensionOperationExecutesAtFiveSecondCadence()
    {
        var defaultExtension = new RecordingTimerTaskExtension("Timer.Default.Op");
        var secondaryExtension = new RecordingTimerTaskExtension("Timer.Secondary.Op");
        var plugin = new TimerPlugin(defaultExtension, secondaryExtension);

        plugin.Start(new PluginStartContext(plugin.PluginId, CancellationToken.None));

        var dispatchObserved = SpinWait.SpinUntil(
            () => defaultExtension.HandleCalls > 0 || secondaryExtension.HandleCalls > 0,
            millisecondsTimeout: 7000);

        plugin.Stop(new PluginStopContext(plugin.PluginId, CancellationToken.None));

        Assert.True(dispatchObserved);
        Assert.True(defaultExtension.HandleCalls > 0);
        Assert.Equal(0, secondaryExtension.HandleCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TimerAutonomousLoopWorkflow_GivenStopOrUnload_ExpectedLoopCancelsAndNoFurtherInvocationsOccur(bool stopPath)
    {
        var writes = new List<string>();
        var plugin = new TimerPlugin(
            utcNowProvider: () => DateTimeOffset.UtcNow,
            writeLine: value =>
            {
                lock (writes)
                {
                    writes.Add(value);
                }
            },
            recurringInterval: TimeSpan.FromMilliseconds(20));

        plugin.Start(new PluginStartContext(plugin.PluginId, CancellationToken.None));

        var writesObserved = SpinWait.SpinUntil(
            () =>
            {
                lock (writes)
                {
                    return writes.Count >= 2;
                }
            },
            millisecondsTimeout: 1000);

        Assert.True(writesObserved);

        if (stopPath)
        {
            plugin.Stop(new PluginStopContext(plugin.PluginId, CancellationToken.None));
        }
        else
        {
            plugin.Unload(new PluginUnloadContext(
                plugin.PluginId,
                PluginUnloadReason.GracefulShutdown,
                DateTimeOffset.UtcNow.AddMinutes(1),
                CancellationToken.None));
        }

        int countAfterCancellation;
        lock (writes)
        {
            countAfterCancellation = writes.Count;
        }

        await Task.Delay(80);

        lock (writes)
        {
            Assert.Equal(countAfterCancellation, writes.Count);
        }
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
        private readonly string _operation;

        public RecordingTimerTaskExtension(string operation)
        {
            _operation = operation;
            SupportedOperations = [new OperationName(operation)];
        }

        public int HandleCalls { get; private set; }

        public IReadOnlyCollection<OperationName> SupportedOperations { get; }

        public void RegisterSchedules(IPluginScheduler scheduler)
        {
            scheduler.ScheduleRecurring(new JobName($"{_operation}.Every5Seconds"), TimeSpan.FromSeconds(5), new OperationName(_operation));
        }

        public SyncResponse Handle(SyncRequest request)
        {
            HandleCalls++;
            return new SyncResponse(Success: true, Payload: _operation, CorrelationId: request.CorrelationId);
        }
    }
}
