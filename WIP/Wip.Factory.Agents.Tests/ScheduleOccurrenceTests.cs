using Modus.Core.Plugins;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Scheduling;
using Wip.Factory.Modus;
using Wip.Factory.Persistence;
using Xunit;

namespace Wip.Factory.Agents.Tests;

public sealed class ScheduleOccurrenceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "wip-factory-schedule-tests", Guid.NewGuid().ToString("N"));

    private static readonly ScheduleId Strategic = new("schedule.strategic-architecture");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private sealed class CapturingScheduler : IPluginScheduler
    {
        public List<(JobName JobName, TimeSpan Interval, OperationName Operation)> Recurring { get; } = [];

        public List<(JobName JobName, DateTimeOffset At, OperationName Operation)> OneOff { get; } = [];

        public void ScheduleRecurring(JobName jobName, TimeSpan interval, OperationName operation)
            => Recurring.Add((jobName, interval, operation));

        public void ScheduleAt(JobName jobName, DateTimeOffset at, OperationName operation)
            => OneOff.Add((jobName, at, operation));
    }

    [Fact]
    public async Task Scheduling_GivenRestart_DurableOccurrenceReplaysExactlyOnceThroughPluginScheduler()
    {
        var scheduler = new CapturingScheduler();
        var store = new LocalScheduleOccurrenceStore(_root);
        var source = new FactoryPluginScheduleSource(scheduler, store);

        // The cadence is declared on Modus' scheduler, not on a factory-owned timer.
        source.Register(
            Strategic,
            new JobName("Factory.StrategicArchitecture.EveryDay"),
            TimeSpan.FromDays(1),
            new OperationName("Factory.StrategicArchitecture"));

        var registration = Assert.Single(scheduler.Recurring);
        Assert.Equal("Factory.StrategicArchitecture.EveryDay", registration.JobName.Value);
        Assert.Equal(TimeSpan.FromDays(1), registration.Interval);

        var dueAt = new DateTimeOffset(2026, 11, 18, 3, 0, 0, TimeSpan.Zero);

        var first = await source.EmitDueAsync(Strategic, dueAt, "W-A");
        Assert.NotNull(first);
        Assert.Equal("schedule.strategic-architecture", first!.Data["scheduleId"]);
        Assert.Equal("schedule", first.Data["trigger"]);

        // The same due instant, delivered again in the same host, must not produce a second occurrence.
        Assert.Null(await source.EmitDueAsync(Strategic, dueAt, "W-A"));

        // A restart replays the durable occurrence record rather than the in-memory one.
        var restartedStore = new LocalScheduleOccurrenceStore(_root);
        var restartedSource = new FactoryPluginScheduleSource(new CapturingScheduler(), restartedStore);

        Assert.Null(await restartedSource.EmitDueAsync(Strategic, dueAt, "W-A"));

        var occurrences = await restartedStore.ReadAllAsync();
        var only = Assert.Single(occurrences);
        Assert.Equal(Strategic, only.ScheduleId);
        Assert.Equal(dueAt, only.DueAtUtc);

        // A later due instant is a genuinely new occurrence.
        var next = await restartedSource.EmitDueAsync(Strategic, dueAt.AddDays(1), "W-A");
        Assert.NotNull(next);
        Assert.NotEqual(first.Id, next!.Id);
        Assert.Equal(2, (await restartedStore.ReadAllAsync()).Count);
    }
}
