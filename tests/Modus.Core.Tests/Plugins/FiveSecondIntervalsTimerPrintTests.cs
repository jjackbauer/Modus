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

        var response = extension.Handle(SyncRequest.ForStandardPath("Timer.WriteCurrentTime", "corr-1"));

        Assert.True(response.Success);
        Assert.Equal(SyncResponseStatus.Success, response.Status);
        Assert.Equal("corr-1", response.CorrelationId);
        Assert.Equal(fixedTimestamp.ToString("O", CultureInfo.InvariantCulture), response.Payload);
        Assert.Equal([response.Payload], writes);
    }

    [Fact]
    public void FiveSecondIntervalsTimerPrint_GivenUnsupportedOperation_ExpectedReturnsRejectedUnsupportedOperationPayload()
    {
        var extension = CreateConcreteExtension(() => DateTimeOffset.UtcNow, _ => { }, TimeSpan.FromSeconds(5));

        var response = extension.Handle(SyncRequest.ForStandardPath("Timer.Unknown", "corr-unsupported"));

        Assert.False(response.Success);
        Assert.Equal(SyncResponseStatus.Rejected, response.Status);
        Assert.Equal("unsupported-operation", response.Payload);
        Assert.Equal("corr-unsupported", response.CorrelationId);
    }

    [Fact]
    public void FiveSecondIntervalsTimerPrint_GivenRegisterSchedules_ExpectedRegistersRecurringJobEveryFiveSeconds()
    {
        var extension = CreateConcreteExtension(() => DateTimeOffset.UtcNow, _ => { }, TimeSpan.FromSeconds(5));
        var scheduler = new RecordingScheduler();

        extension.RegisterSchedules(scheduler);

        var recurring = Assert.Single(scheduler.RecurringSchedules);
        Assert.Equal("Timer.WriteCurrentTime.Every5Seconds", recurring.JobName);
        Assert.Equal(TimeSpan.FromSeconds(5), recurring.Interval);
        Assert.Equal("Timer.WriteCurrentTime", recurring.Operation);
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
        public List<(string JobName, TimeSpan Interval, string Operation)> RecurringSchedules { get; } = new();

        public void ScheduleRecurring(string jobName, TimeSpan interval, string operation)
        {
            RecurringSchedules.Add((jobName, interval, operation));
        }

        public void ScheduleAt(string jobName, DateTimeOffset runAt, string operation)
        {
        }
    }
}