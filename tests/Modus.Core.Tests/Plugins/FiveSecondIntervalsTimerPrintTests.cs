using System.Globalization;
using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Xunit;

namespace Modus.Core.Tests.Plugins;

public sealed class FiveSecondIntervalsTimerPrintTests
{
    [Fact]
    public void TimerPlugin_GivenParameterlessConstruction_ExpectedLoadsFiveSecondIntervalsTimerPrintByDefault()
    {
        var plugin = new TimerPlugin();
        var extensionsField = typeof(TimerPlugin).GetField("_scheduledTaskExtensions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(extensionsField);

        var extensions = Assert.IsAssignableFrom<IReadOnlyCollection<IScheduledTimerTaskExtension>>(extensionsField!.GetValue(plugin));
        var extension = Assert.Single(extensions);
        Assert.Equal("FiveSecondIntervalsTimerPrint", extension.GetType().Name);
    }

    [Fact]
    public void FiveSecondIntervalsTimerPrint_GivenHandleWriteCurrentTime_ExpectedWritesInvariantIsoTimestampAndReturnsSuccess()
    {
        var fixedTimestamp = new DateTimeOffset(2026, 05, 17, 12, 34, 56, TimeSpan.Zero);
        var writes = new List<string>();
        var extension = CreateConcreteExtension(() => fixedTimestamp, writes.Add, TimeSpan.FromSeconds(5));

        var response = extension.Handle(SyncRequest.ForStandardPath(new OperationName("Timer.WriteCurrentTime"), new CorrelationId("corr-1")));

        Assert.True(response.Success);
        Assert.Equal(SyncResponseStatus.Success, response.Status);
        Assert.Equal(new CorrelationId("corr-1"), response.CorrelationId);
        var payload = Assert.IsType<TimerWriteCurrentTimeResult>(response.Payload);
        Assert.Equal(fixedTimestamp.ToString("O", CultureInfo.InvariantCulture), payload.TimestampUtcIso8601);
        Assert.Equal([payload.TimestampUtcIso8601], writes);
    }

    [Fact]
    public void FiveSecondIntervalsTimerPrint_GivenUnsupportedOperation_ExpectedReturnsRejectedUnsupportedOperationPayload()
    {
        var extension = CreateConcreteExtension(() => DateTimeOffset.UtcNow, _ => { }, TimeSpan.FromSeconds(5));

        var response = extension.Handle(SyncRequest.ForStandardPath(new OperationName("Timer.Unknown"), new CorrelationId("corr-unsupported")));

        Assert.False(response.Success);
        Assert.Equal(SyncResponseStatus.Rejected, response.Status);
        var error = Assert.IsType<SyncErrorPayload>(response.Payload);
        Assert.Equal("unsupported-operation", error.Code);
        Assert.Equal(new CorrelationId("corr-unsupported"), response.CorrelationId);
    }

    [Fact]
    public void FiveSecondIntervalsTimerPrint_GivenRegisterSchedules_ExpectedRegistersRecurringJobEveryFiveSeconds()
    {
        var extension = CreateConcreteExtension(() => DateTimeOffset.UtcNow, _ => { }, TimeSpan.FromSeconds(5));
        var scheduler = new RecordingScheduler();

        extension.RegisterSchedules(scheduler);

        var recurring = Assert.Single(scheduler.RecurringSchedules);
        Assert.Equal(new JobName("Timer.WriteCurrentTime.Every5Seconds"), recurring.JobName);
        Assert.Equal(TimeSpan.FromSeconds(5), recurring.Interval);
        Assert.Equal(new OperationName("Timer.WriteCurrentTime"), recurring.Operation);
    }

    private static IScheduledTimerTaskExtension CreateConcreteExtension(
        Func<DateTimeOffset> utcNowProvider,
        Action<string> writeLine,
        TimeSpan recurringInterval)
    {
        var extensionType = typeof(IPluginContract).Assembly.GetType("Modus.Core.Plugins.FiveSecondIntervalsTimerPrint");

        Assert.NotNull(extensionType);

        var instance = Activator.CreateInstance(extensionType!, utcNowProvider, writeLine, recurringInterval);
        return Assert.IsAssignableFrom<IScheduledTimerTaskExtension>(instance);
    }

    private sealed class RecordingScheduler : IPluginScheduler
    {
        public List<(JobName JobName, TimeSpan Interval, OperationName Operation)> RecurringSchedules { get; } = new();

        public void ScheduleRecurring(JobName jobName, TimeSpan interval, OperationName operation)
        {
            RecurringSchedules.Add((jobName, interval, operation));
        }

        public void ScheduleAt(JobName jobName, DateTimeOffset runAt, OperationName operation)
        {
        }
    }
}